namespace Pomo.Core.Systems

open System
open System.Xml.Linq
open System.IO
open System.Globalization
open Microsoft.Xna.Framework
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.Units
open FSharp.UMX
open FSharp.Data.Adaptive

module MapLoader =
  open System.Diagnostics

  let inline private xname name = XName.Get name

  let inline private attr (name: string) (element: XElement) =
    match element.Attribute(xname name) with
    | Null -> ValueNone
    | a -> ValueSome a.Value

  let private attrInt (name: string) (element: XElement) =
    attr name element
    |> ValueOption.bind(fun v ->
      match Int32.TryParse(v, CultureInfo.InvariantCulture) with
      | true, parsed -> ValueSome parsed
      | false, _ -> ValueNone)

  let private attrFloat (name: string) (element: XElement) =
    attr name element
    |> ValueOption.bind(fun v ->
      match
        Single.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture)
      with
      | true, parsed -> ValueSome parsed
      | false, _ -> ValueNone)

  let private attrBool (name: string) (element: XElement) =
    attr name element
    |> ValueOption.bind(fun v ->
      match v.ToLower() with
      | "1"
      | "true" -> ValueSome true
      | "0"
      | "false" -> ValueSome false
      | _ -> ValueNone)

  let inline private parseFloat(s: string) =
    Single.Parse(s, CultureInfo.InvariantCulture)

  let inline private parseBool(s: string) =
    if s = "1" || s.ToLower() = "true" then true else false

  let inline private parseGid(s: string) =
    let raw = UInt32.Parse(s, CultureInfo.InvariantCulture)
    // Mask out the flipping flags (top 3 bits)
    // 0x1FFFFFFF is the mask for the lower 29 bits
    let id = raw &&& 0x1FFFFFFFu
    int id

  let private parseProperties(element: XElement) =
    let propsElem = element.Element(xname "properties")

    if isNull propsElem then
      HashMap.empty
    else
      propsElem.Elements(xname "property")
      |> Seq.map(fun p ->
        let name = p.Attribute(xname "name").Value
        let value = p.Attribute(xname "value").Value
        name, value)
      |> HashMap.ofSeq

  let private parseTileset(element: XElement) : Tileset =
    let firstGid = attrInt "firstgid" element
    let name = attr "name" element
    let tileWidth = attrInt "tilewidth" element
    let tileHeight = attrInt "tileheight" element
    let tileCount = attrInt "tilecount" element
    let columns = attrInt "columns" element

    let tiles =
      element.Elements(xname "tile")
      |> Seq.map(fun t ->
        let id = attrInt "id" t |> ValueOption.defaultValue 0
        let imageElem = t.Element(xname "image")

        let source =
          if not(isNull imageElem) then
            attr "source" imageElem |> ValueOption.defaultValue String.Empty
          else
            String.Empty

        let width =
          if not(isNull imageElem) then
            attrInt "width" imageElem |> ValueOption.defaultValue 0
          else
            0

        let height =
          if not(isNull imageElem) then
            attrInt "height" imageElem |> ValueOption.defaultValue 0
          else
            0

        id,
        {
          Id = %id
          ImageSource = source
          Width = width
          Height = height
          Properties = parseProperties t
        })
      |> HashMap.ofSeq

    {
      FirstGid = firstGid |> ValueOption.defaultValue 0
      Name = name |> ValueOption.defaultValue String.Empty
      TileWidth = tileWidth |> ValueOption.defaultValue 0
      TileHeight = tileHeight |> ValueOption.defaultValue 0
      TileCount = tileCount |> ValueOption.defaultValue 0
      Columns = columns |> ValueOption.defaultValue 0
      Tiles = tiles
    }

  let private parseLayer
    (element: XElement)
    (width: int)
    (height: int)
    : MapLayer =
    let id = attrInt "id" element |> ValueOption.defaultValue 0
    let name = attr "name" element |> ValueOption.defaultValue String.Empty
    let opacity = attrFloat "opacity" element |> ValueOption.defaultValue 1.0f
    let visible = attrBool "visible" element |> ValueOption.defaultValue true

    let dataElem = element.Element(xname "data")
    let encoding = attr "encoding" dataElem |> ValueOption.defaultValue "xml"

    let tiles = Array2D.zeroCreate<MapTile voption> width height

    if encoding = "csv" then
      let csv = dataElem.Value.Trim()

      let rows =
        csv.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)

      rows
      |> Array.iteri(fun y row ->
        let cols = row.Split([| ',' |], StringSplitOptions.RemoveEmptyEntries)

        cols
        |> Array.iteri(fun x gidStr ->
          let gid = parseGid gidStr

          if gid > 0 then
            tiles.[x, y] <- ValueSome { TileId = %gid; X = x; Y = y }
          else
            tiles.[x, y] <- ValueNone))
    else
      // Fallback or error for other encodings (base64, etc.)
      // For now assuming CSV as per Proto.xml
      ()

    {
      Id = %id
      Name = name
      Width = width
      Height = height
      Tiles = tiles
      Opacity = opacity
      Visible = visible
      Properties = parseProperties element
    }

  let private parseObject(element: XElement) : MapObject =
    let id = attrInt "id" element |> ValueOption.defaultValue 0
    let name = attr "name" element |> ValueOption.defaultValue String.Empty

    let parseMapObjectType(s: string) =
      match s.ToLowerInvariant() with
      | "wall" -> ValueSome Wall
      | "zone" -> ValueSome Zone
      | "spawn" -> ValueSome Spawn
      | _ -> ValueNone

    let type' = attr "type" element |> ValueOption.bind parseMapObjectType

    let x = attrFloat "x" element |> ValueOption.defaultValue 0.0f
    let y = attrFloat "y" element |> ValueOption.defaultValue 0.0f
    let width = attrFloat "width" element |> ValueOption.defaultValue 0.0f
    let height = attrFloat "height" element |> ValueOption.defaultValue 0.0f
    let rotation = attrFloat "rotation" element |> ValueOption.defaultValue 0.0f

    let gid = attr "gid" element |> ValueOption.map parseGid

    let points =
      let polygon = element.Element(xname "polygon")
      let polyline = element.Element(xname "polyline")

      if not(isNull polygon) then
        let pts = attr "points" polygon |> ValueOption.defaultValue ""

        ValueSome(
          pts.Split(' ')
          |> Array.map(fun p ->
            let coords = p.Split(',')
            Vector2(parseFloat coords.[0], parseFloat coords.[1]))
          |> IndexList.ofArray
        )
      elif not(isNull polyline) then
        let pts = attr "points" polyline |> ValueOption.defaultValue ""

        ValueSome(
          pts.Split(' ')
          |> Array.map(fun p ->
            let coords = p.Split(',')
            Vector2(parseFloat coords.[0], parseFloat coords.[1]))
          |> IndexList.ofArray
        )
      else
        ValueNone

    {
      Id = %id
      Name = name
      Type = type'
      X = x
      Y = y
      Width = width
      Height = height
      Rotation = rotation
      Gid = gid
      Properties = parseProperties element
      Points = points
    }

  let private parseObjectGroup(element: XElement) : ObjectGroup =
    let id = attrInt "id" element |> ValueOption.defaultValue 0
    let name = attr "name" element |> ValueOption.defaultValue String.Empty
    let opacity = attrFloat "opacity" element |> ValueOption.defaultValue 1.0f
    let visible = attrBool "visible" element |> ValueOption.defaultValue true

    let objects =
      element.Elements(xname "object") |> Seq.map parseObject |> IndexList.ofSeq

    {
      Id = id
      Name = name
      Objects = objects
      Opacity = opacity
      Visible = visible
    }

  let loadMap(path: string) : MapDefinition =
    let doc = XDocument.Load(Path.Combine(AppContext.BaseDirectory, path))
    let mapElem = doc.Element(xname "map")

    let version = attr "version" mapElem |> ValueOption.defaultValue "1.0"

    let tiledVersion =
      attr "tiledversion" mapElem |> ValueOption.defaultValue ""

    let orientationStr =
      attr "orientation" mapElem |> ValueOption.defaultValue "orthogonal"

    let renderOrderStr =
      attr "renderorder" mapElem |> ValueOption.defaultValue "right-down"

    let width = attrInt "width" mapElem |> ValueOption.defaultValue 0
    let height = attrInt "height" mapElem |> ValueOption.defaultValue 0
    let tileWidth = attrInt "tilewidth" mapElem |> ValueOption.defaultValue 0
    let tileHeight = attrInt "tileheight" mapElem |> ValueOption.defaultValue 0
    let infinite = attrBool "infinite" mapElem |> ValueOption.defaultValue false

    let staggerAxisStr = attr "staggeraxis" mapElem
    let staggerIndexStr = attr "staggerindex" mapElem

    let orientation =
      match orientationStr with
      | "orthogonal" -> Orthogonal
      | "isometric" -> Isometric
      | "staggered" -> Staggered
      | "hexagonal" -> Hexagonal
      | _ -> Orthogonal

    let renderOrder =
      match renderOrderStr with
      | "right-down" -> RightDown
      | "right-up" -> RightUp
      | "left-down" -> LeftDown
      | "left-up" -> LeftUp
      | _ -> RightDown

    let staggerAxis =
      match staggerAxisStr with
      | ValueSome "x" -> ValueSome X
      | ValueSome "y" -> ValueSome Y
      | _ -> ValueNone

    let staggerIndex =
      match staggerIndexStr with
      | ValueSome "odd" -> ValueSome Odd
      | ValueSome "even" -> ValueSome Even
      | _ -> ValueNone

    let tilesets =
      mapElem.Elements(xname "tileset")
      |> Seq.map parseTileset
      |> IndexList.ofSeq

    let layers =
      mapElem.Elements(xname "layer")
      |> Seq.map(fun l -> parseLayer l width height)
      |> IndexList.ofSeq

    let objectGroups =
      mapElem.Elements(xname "objectgroup")
      |> Seq.map parseObjectGroup
      |> IndexList.ofSeq

    let properties = parseProperties mapElem

    let key =
      properties |> HashMap.tryFind "MapKey" |> Option.defaultValue "Unknown"

    {
      Key = key
      Version = version
      TiledVersion = tiledVersion
      Orientation = orientation
      RenderOrder = renderOrder
      Width = width
      Height = height
      TileWidth = tileWidth
      TileHeight = tileHeight
      Infinite = infinite
      StaggerAxis = staggerAxis
      StaggerIndex = staggerIndex
      Tilesets = tilesets
      Layers = layers
      ObjectGroups = objectGroups
      BackgroundColor = ValueNone
    }

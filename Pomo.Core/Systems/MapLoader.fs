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

  let private xname name = XName.Get name

  let private attr (name: string) (element: XElement) =
    let a = element.Attribute(xname name)
    if isNull a then None else Some a.Value

  let private attrString (name: string) (def: string) (element: XElement) =
    match attr name element with
    | Some v -> v
    | None -> def

  let private attrInt (name: string) (def: int) (element: XElement) =
    match attr name element with
    | Some v -> Int32.Parse(v, CultureInfo.InvariantCulture)
    | None -> def

  let private attrFloat (name: string) (def: float32) (element: XElement) =
    match attr name element with
    | Some v -> Single.Parse(v, CultureInfo.InvariantCulture)
    | None -> def

  let private attrBool (name: string) (def: bool) (element: XElement) =
    match attr name element with
    | Some v -> if v = "1" || v.ToLower() = "true" then true else false
    | None -> def

  let private parseInt(s: string) =
    Debug.WriteLine(s)
    Int32.Parse(s, CultureInfo.InvariantCulture)

  let private parseFloat(s: string) =
    Single.Parse(s, CultureInfo.InvariantCulture)

  let private parseBool(s: string) =
    if s = "1" || s.ToLower() = "true" then true else false

  let private parseGid(s: string) =
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
    let firstGid = attrInt "firstgid" 1 element
    let name = attrString "name" "" element
    let tileWidth = attrInt "tilewidth" 0 element
    let tileHeight = attrInt "tileheight" 0 element
    let tileCount = attrInt "tilecount" 0 element
    let columns = attrInt "columns" 0 element

    let tiles =
      element.Elements(xname "tile")
      |> Seq.map(fun t ->
        let id = attrInt "id" 0 t
        let imageElem = t.Element(xname "image")

        let source =
          if not(isNull imageElem) then
            attrString "source" "" imageElem
          else
            ""

        let width =
          if not(isNull imageElem) then
            attrInt "width" 0 imageElem
          else
            0

        let height =
          if not(isNull imageElem) then
            attrInt "height" 0 imageElem
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
      FirstGid = firstGid
      Name = name
      TileWidth = tileWidth
      TileHeight = tileHeight
      TileCount = tileCount
      Columns = columns
      Tiles = tiles
    }

  let private parseLayer
    (element: XElement)
    (width: int)
    (height: int)
    : MapLayer =
    let id = attrInt "id" 0 element
    let name = attrString "name" "" element
    let opacity = attrFloat "opacity" 1.0f element
    let visible = attrBool "visible" true element

    let dataElem = element.Element(xname "data")
    let encoding = attrString "encoding" "xml" dataElem

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
    let id = attrInt "id" 0 element
    let name = attrString "name" "" element

    let parseMapObjectType(s: string) =
      match s.ToLowerInvariant() with
      | "wall" -> ValueSome Wall
      | "zone" -> ValueSome Zone
      | "spawn" -> ValueSome Spawn
      | _ -> ValueNone

    let type' = attrString "type" "" element |> parseMapObjectType
    let x = attrFloat "x" 0.0f element
    let y = attrFloat "y" 0.0f element
    let width = attrFloat "width" 0.0f element
    let height = attrFloat "height" 0.0f element
    let rotation = attrFloat "rotation" 0.0f element
    let gid = attr "gid" element |> Option.map parseGid |> ValueOption.ofOption

    let points =
      let polygon = element.Element(xname "polygon")
      let polyline = element.Element(xname "polyline")

      if not(isNull polygon) then
        let pts = attrString "points" "" polygon

        ValueSome(
          pts.Split(' ')
          |> Array.map(fun p ->
            let coords = p.Split(',')
            Vector2(parseFloat coords.[0], parseFloat coords.[1]))
          |> IndexList.ofArray
        )
      elif not(isNull polyline) then
        let pts = attrString "points" "" polyline

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
    let id = attrInt "id" 0 element
    let name = attrString "name" "" element
    let opacity = attrFloat "opacity" 1.0f element
    let visible = attrBool "visible" true element

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

    let version = attrString "version" "1.0" mapElem
    let tiledVersion = attrString "tiledversion" "" mapElem
    let orientationStr = attrString "orientation" "orthogonal" mapElem
    let renderOrderStr = attrString "renderorder" "right-down" mapElem
    let width = attrInt "width" 0 mapElem
    let height = attrInt "height" 0 mapElem
    let tileWidth = attrInt "tilewidth" 0 mapElem
    let tileHeight = attrInt "tileheight" 0 mapElem
    let infinite = attrInt "infinite" 0 mapElem = 1

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
      | Some "x" -> ValueSome X
      | Some "y" -> ValueSome Y
      | _ -> ValueNone

    let staggerIndex =
      match staggerIndexStr with
      | Some "odd" -> ValueSome Odd
      | Some "even" -> ValueSome Even
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

namespace Pomo.Lib

open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX

[<Measure>]
type BlockTypeId

[<Struct>]
type GridDimensions = {
  Width: int
  Height: int
  Depth: int
} with

  static member Default = { Width = 50; Height = 10; Depth = 50 }

[<Struct>]
type Transform = {
  Position: Vector3
  Rotation: Quaternion
  Scale: Vector3
}

// --- Block Types ---

[<Struct>]
type CollisionType =
  | Box
  | Mesh
  | NoCollision

type BlockType = {
  Id: int<BlockTypeId>
  ArchetypeId: int<BlockTypeId>
  VariantKey: string voption
  Name: string
  Model: string
  Category: string
  CollisionType: CollisionType
}

// --- Map Structure ---

[<Struct>]
type EngagementRules =
  | Peaceful
  | PvE
  | PvP
  | FFA

type MapSettings = {
  EngagementRules: EngagementRules
  MaxEnemyEntities: int
  StartingLayer: int
}

[<Struct; RequireQualifiedAccess>]
type MapObjectShape =
  | Box of size: Vector3
  | Sphere of radius: float32

[<Struct>]
type SpawnProperties = {
  IsPlayerSpawn: bool
  EntityGroup: string voption
  MaxSpawns: int
  Faction: string voption
}

[<Struct>]
type TeleportProperties = {
  TargetMap: string voption
  TargetObjectName: string
}

[<Struct>]
type MapObjectData =
  | Spawn of spawn: SpawnProperties
  | Teleport of teleport: TeleportProperties
  | Trigger of triggerId: string

type MapObject = {
  Id: int
  Name: string
  Position: Vector3
  Rotation: Quaternion voption
  Shape: MapObjectShape
  Data: MapObjectData
}

[<Struct>]
type PlacedBlock = {
  Cell: Vector3
  BlockTypeId: int<BlockTypeId>
  Rotation: Quaternion voption
}

type BlockMapDefinition = {
  Version: int
  Key: string
  MapKey: string voption
  Dimensions: GridDimensions
  Palette: Dictionary<int<BlockTypeId>, BlockType>
  Blocks: Dictionary<Vector3, PlacedBlock>
  SpawnCell: Vector3 voption
  Settings: MapSettings
  Objects: MapObject[]
}

module BlockMapDefinition =
  let empty = {
    Version = 1
    Key = "NewMap"
    MapKey = ValueNone
    Dimensions = GridDimensions.Default
    Palette = Dictionary()
    Blocks = Dictionary()
    SpawnCell = ValueNone
    Settings = {
      EngagementRules = EngagementRules.PvE
      MaxEnemyEntities = 0
      StartingLayer = 0
    }
    Objects = Array.empty
  }

// Serialization codecs for domain types
namespace Pomo.Lib

open System.Text.Json
open System.Text.Json.Nodes
open Microsoft.Xna.Framework
open JDeck
open JDeck.Decode
open JDeck.Encoding

module DomainSerializers =
  open System.Collections.Generic
  open FSharp.UMX

  // Vector3: Compact array format [x, y, z]
  // Using JDeck 1.1.0 Json.sequence with JsonNode seq for mixed-type arrays
  let vector3Encoder: Encoder<Vector3> =
    fun v ->
      Json.sequence [ Encode.single v.X; Encode.single v.Y; Encode.single v.Z ]

  let vector3Decoder: Decoder<Vector3> =
    fun json -> decode {
      let! x = Decode.decodeAt Decode.Required.single 0 json
      and! y = Decode.decodeAt Decode.Required.single 1 json
      and! z = Decode.decodeAt Decode.Required.single 2 json
      return Vector3(x, y, z)
    }

  // Quaternion: [x, y, z, w] instead of {"X":x,"Y":y,"Z":z,"W":w}
  let quaternionEncoder: Encoder<Quaternion> =
    fun q ->
      Json.sequence [
        Encode.single q.X
        Encode.single q.Y
        Encode.single q.Z
        Encode.single q.W
      ]

  let quaternionDecoder: Decoder<Quaternion> =
    fun json -> decode {
      let! x = Decode.decodeAt Decode.Required.single 0 json
      and! y = Decode.decodeAt Decode.Required.single 1 json
      and! z = Decode.decodeAt Decode.Required.single 2 json
      and! w = Decode.decodeAt Decode.Required.single 3 json
      return Quaternion(x, y, z, w)
    }

  // GridDimensions: [w, h, d] instead of {"Width":w,"Height":h,"Depth":d}
  let gridDimensionsEncoder: Encoder<GridDimensions> =
    fun d ->
      Json.sequence [
        Encode.int d.Width
        Encode.int d.Height
        Encode.int d.Depth
      ]

  let gridDimensionsDecoder: Decoder<GridDimensions> =
    fun json -> decode {
      let! w = Decode.decodeAt Decode.Required.int 0 json
      and! h = Decode.decodeAt Decode.Required.int 1 json
      and! d = Decode.decodeAt Decode.Required.int 2 json
      return { Width = w; Height = h; Depth = d }
    }

  // CollisionType: Leaf DU
  let collisionTypeEncoder: Encoder<CollisionType> =
    function
    | Box -> Encode.string "Box"
    | Mesh -> Encode.string "Mesh"
    | NoCollision -> Encode.string "NoCollision"

  let collisionTypeDecoder: Decoder<CollisionType> =
    fun json ->
      Decode.Required.string json
      |> Result.map (function
        | "Box" -> Box
        | "Mesh" -> Mesh
        | "NoCollision" -> NoCollision
        | s -> failwith $"Unknown CollisionType: {s}")

  // EngagementRules: Leaf DU
  let engagementRulesEncoder: Encoder<EngagementRules> =
    function
    | Peaceful -> Encode.string "Peaceful"
    | PvE -> Encode.string "PvE"
    | PvP -> Encode.string "PvP"
    | FFA -> Encode.string "FFA"

  let engagementRulesDecoder: Decoder<EngagementRules> =
    fun json ->
      Decode.Required.string json
      |> Result.map (function
        | "Peaceful" -> Peaceful
        | "PvE" -> PvE
        | "PvP" -> PvP
        | "FFA" -> FFA
        | s -> failwith $"Unknown EngagementRules: {s}")

  // MapObjectShape: compact format ["Box", [x, y, z]] or ["Sphere", radius]
  // Using mixed-type array encoding with Json.sequence
  let mapObjectShapeEncoder: Encoder<MapObjectShape> =
    function
    | MapObjectShape.Box size ->
      Json.sequence [ Encode.string "Box"; vector3Encoder size ]
    | MapObjectShape.Sphere radius ->
      Json.sequence [ Encode.string "Sphere"; Encode.single radius ]

  let mapObjectShapeDecoderFactory
    (opts: JsonSerializerOptions)
    : Decoder<MapObjectShape> =
    fun json -> decode {
      let! shape = Decode.decodeAt Decode.Required.string 0 json

      match shape with
      | "Box" ->
        let! size = Decode.decodeAt vector3Decoder 1 json
        return MapObjectShape.Box size
      | "Sphere" ->
        let! radius = Decode.decodeAt Decode.Required.single 1 json
        return MapObjectShape.Sphere radius
      | s -> return! Error(DecodeError.ofError(json, $"Unknown shape: {s}"))
    }


  // MapObjectData: compact format with discriminator
  // Using mixed-type arrays with Json.sequence for variable-length arrays
  let mapObjectDataEncoder: Encoder<MapObjectData> =
    function
    | Spawn props ->
      // Variable length: include optional fields only if present
      let baseNodes = [
        Encode.string "Spawn"
        Encode.boolean props.IsPlayerSpawn
        Encode.int props.MaxSpawns
      ]

      let withEntityGroup =
        match props.EntityGroup with
        | ValueSome g -> baseNodes @ [ Encode.string g ]
        | ValueNone -> baseNodes

      let withFaction =
        match props.Faction with
        | ValueSome f -> withEntityGroup @ [ Encode.string f ]
        | ValueNone -> withEntityGroup

      Json.sequence withFaction
    | Teleport props ->
      let baseNodes = [
        Encode.string "Teleport"
        Encode.string props.TargetObjectName
      ]

      let fullNodes =
        match props.TargetMap with
        | ValueSome m -> baseNodes @ [ Encode.string m ]
        | ValueNone -> baseNodes

      Json.sequence fullNodes
    | Trigger id -> Json.sequence [ Encode.string "Trigger"; Encode.string id ]

  let mapObjectDataDecoderFactory
    (opts: JsonSerializerOptions)
    : Decoder<MapObjectData> =
    fun json -> decode {
      let! discriminator = Decode.decodeAt Decode.Required.string 0 json

      match discriminator with
      | "Spawn" ->
        let! isPlayer = Decode.decodeAt Decode.Required.boolean 1 json
        and! maxSpawns = Decode.decodeAt Decode.Required.int 2 json
        // Use tryDecodeAt for optional fields
        let! entityGroup = Decode.tryDecodeAt Decode.Required.string 3 json
        let! faction = Decode.tryDecodeAt Decode.Required.string 4 json

        return
          Spawn {
            IsPlayerSpawn = isPlayer
            EntityGroup =
              entityGroup
              |> Option.defaultValue ""
              |> function
                | "" -> ValueNone
                | g -> ValueSome g
            MaxSpawns = maxSpawns
            Faction =
              faction
              |> Option.defaultValue ""
              |> function
                | "" -> ValueNone
                | f -> ValueSome f
          }
      | "Teleport" ->
        let! targetObj = Decode.decodeAt Decode.Required.string 1 json
        let! targetMap = Decode.tryDecodeAt Decode.Required.string 2 json

        return
          Teleport {
            TargetObjectName = targetObj
            TargetMap =
              targetMap
              |> Option.defaultValue ""
              |> function
                | "" -> ValueNone
                | m -> ValueSome m
          }
      | "Trigger" ->
        let! triggerId = Decode.decodeAt Decode.Required.string 1 json
        return Trigger triggerId
      | s ->
        return! Error(DecodeError.ofError(json, $"Unknown MapObjectData: {s}"))
    }

  // PlacedBlock: [cell, typeId, rotation?] - compact tuple format
  // Note: BlockTypeId is erased at runtime, stored as int
  let placedBlockEncoder: Encoder<PlacedBlock> =
    fun pb ->
      let baseNodes = [
        vector3Encoder pb.Cell
        Encode.int(pb.BlockTypeId |> UMX.untag)
      ]

      let fullNodes =
        match pb.Rotation with
        | ValueSome rot -> baseNodes @ [ quaternionEncoder rot ]
        | ValueNone -> baseNodes

      Json.sequence fullNodes

  let placedBlockDecoder: Decoder<PlacedBlock> =
    fun json -> decode {
      let! cell = Decode.decodeAt vector3Decoder 0 json

      let! typeId =
        Decode.decodeAt Decode.Required.int 1 json
        |> Result.map(fun i -> i * 1<BlockTypeId>)
      // Use tryDecodeAt for optional rotation
      let! rotationOpt = Decode.tryDecodeAt quaternionDecoder 2 json

      return {
        Cell = cell
        BlockTypeId = typeId
        Rotation =
          rotationOpt
          |> Option.map(fun r -> ValueSome r)
          |> Option.defaultValue ValueNone
      }
    }

  // BlockType encoder - omits VariantKey if ValueNone (no null values)
  let blockTypeEncoder: Encoder<BlockType> =
    fun bt ->
      let obj =
        Json.empty()
        |> Encode.property("Id", Encode.int(bt.Id |> UMX.untag))
        |> Encode.property(
          "ArchetypeId",
          Encode.int(bt.ArchetypeId |> UMX.untag)
        )
        |> Encode.property("Name", Encode.string bt.Name)
        |> Encode.property("Model", Encode.string bt.Model)
        |> Encode.property("Category", Encode.string bt.Category)
        |> Encode.property(
          "CollisionType",
          collisionTypeEncoder bt.CollisionType
        )

      let obj =
        match bt.VariantKey with
        | ValueSome key -> Encode.property ("VariantKey", Encode.string key) obj
        | ValueNone -> obj

      obj :> JsonNode

  // BlockType decoder - uses Optional.Property.get for VariantKey (field may not be present)
  let blockTypeDecoder: Decoder<BlockType> =
    fun json -> decode {
      let! id = Optional.Property.get ("Id", Decode.Required.int) json

      and! archetypeId =
        Optional.Property.get ("ArchetypeId", Decode.Required.int) json

      and! name = Optional.Property.get ("Name", Decode.Required.string) json
      and! model = Optional.Property.get ("Model", Decode.Required.string) json

      and! category =
        Optional.Property.get ("Category", Decode.Required.string) json

      and! collisionType =
        Optional.Property.get ("CollisionType", collisionTypeDecoder) json

      and! variantKey =
        Optional.Property.get ("VariantKey", Decode.Required.string) json

      return {
        Id = id |> Option.defaultValue 0 |> (fun i -> i * 1<BlockTypeId>)
        ArchetypeId =
          archetypeId |> Option.defaultValue 0 |> (fun i -> i * 1<BlockTypeId>)
        Name = name |> Option.defaultValue ""
        Model = model |> Option.defaultValue ""
        Category = category |> Option.defaultValue ""
        CollisionType = collisionType |> Option.defaultValue Box
        VariantKey =
          variantKey |> Option.map ValueSome |> Option.defaultValue ValueNone
      }
    }

  // Palette as array of tuples: [(int, BlockType)]
  let paletteEncoder: Encoder<Dictionary<int<BlockTypeId>, BlockType>> =
    fun palette ->
      palette
      |> Seq.map(fun kv ->
        Json.sequence [
          Encode.int(kv.Key |> UMX.untag)
          blockTypeEncoder kv.Value
        ])
      |> Seq.toArray
      |> Json.sequence

  // Tuple decoder for palette entries
  let private paletteEntryDecoder
    : Decoder<struct (int<BlockTypeId> * BlockType)> =
    fun json -> decode {
      let! id = Decode.decodeAt Decode.Required.int 0 json
      let! bt = Decode.decodeAt blockTypeDecoder 1 json
      return struct (id * 1<BlockTypeId>, bt)
    }

  let paletteDecoder: Decoder<Dictionary<int<BlockTypeId>, BlockType>> =
    fun json ->
      Decode.array (fun i -> paletteEntryDecoder) json
      |> Result.map(fun entries ->
        let dict = Dictionary<int<BlockTypeId>, BlockType>()

        for struct (id, bt) in entries do
          dict.[id] <- bt

        dict)

  // Blocks as array of tuples: [(Vector3, PlacedBlock)]
  let blocksEncoder: Encoder<Dictionary<Vector3, PlacedBlock>> =
    fun blocks ->
      blocks
      |> Seq.map(fun kv ->
        Json.sequence [ vector3Encoder kv.Key; placedBlockEncoder kv.Value ])
      |> Seq.toArray
      |> Json.sequence

  // Tuple decoder for block entries
  let private blocksEntryDecoder: Decoder<struct (Vector3 * PlacedBlock)> =
    fun json -> decode {
      let! cell = Decode.decodeAt vector3Decoder 0 json
      let! pb = Decode.decodeAt placedBlockDecoder 1 json
      return struct (cell, pb)
    }

  let blocksDecoder: Decoder<Dictionary<Vector3, PlacedBlock>> =
    fun json ->
      Decode.array (fun i -> blocksEntryDecoder) json
      |> Result.map(fun entries ->
        let dict = Dictionary<Vector3, PlacedBlock>()

        for struct (cell, pb) in entries do
          dict.[cell] <- pb

        dict)

  // voption<string> encoder
  let voptionStringEncoder: Encoder<string voption> =
    function
    | ValueSome s -> Encode.string s
    | ValueNone -> Encode.Null()

  // voption<Vector3> encoder
  let voptionVector3Encoder: Encoder<Vector3 voption> =
    function
    | ValueSome v -> vector3Encoder v
    | ValueNone -> Encode.Null()

  // voption<Quaternion> encoder
  let voptionQuaternionEncoder: Encoder<Quaternion voption> =
    function
    | ValueSome q -> quaternionEncoder q
    | ValueNone -> Encode.Null()

  // MapSettings encoder/decoder
  let mapSettingsEncoder: Encoder<MapSettings> =
    fun ms ->
      Json.empty()
      |> Encode.property(
        "EngagementRules",
        engagementRulesEncoder ms.EngagementRules
      )
      |> Encode.property("MaxEnemyEntities", Encode.int ms.MaxEnemyEntities)
      |> Encode.property("StartingLayer", Encode.int ms.StartingLayer)
      :> JsonNode

  let mapSettingsDecoder: Decoder<MapSettings> =
    fun json -> decode {
      let! er =
        Optional.Property.get ("EngagementRules", engagementRulesDecoder) json

      and! mee =
        Optional.Property.get ("MaxEnemyEntities", Decode.Required.int) json

      and! sl =
        Optional.Property.get ("StartingLayer", Decode.Required.int) json

      return {
        EngagementRules = er |> Option.defaultValue EngagementRules.PvE
        MaxEnemyEntities = mee |> Option.defaultValue 0
        StartingLayer = sl |> Option.defaultValue 0
      }
    }

  // MapObject encoder/decoder
  let mapObjectEncoder: Encoder<MapObject> =
    fun mo ->
      let obj =
        Json.empty()
        |> Encode.property("Id", Encode.int mo.Id)
        |> Encode.property("Name", Encode.string mo.Name)
        |> Encode.property("Position", vector3Encoder mo.Position)
        |> Encode.property("Shape", mapObjectShapeEncoder mo.Shape)
        |> Encode.property("Data", mapObjectDataEncoder mo.Data)
      // Only include Rotation if present
      let obj =
        match mo.Rotation with
        | ValueSome rot ->
          Encode.property ("Rotation", quaternionEncoder rot) obj
        | ValueNone -> obj

      obj :> JsonNode

  let mapObjectDecoder: Decoder<MapObject> =
    fun json -> decode {
      let! id = Optional.Property.get ("Id", Decode.Required.int) json
      and! name = Optional.Property.get ("Name", Decode.Required.string) json
      and! pos = Optional.Property.get ("Position", vector3Decoder) json
      and! shape = Optional.Property.get ("Shape", Decode.auto) json
      and! data = Optional.Property.get ("Data", Decode.auto) json
      and! rot = Optional.Property.get ("Rotation", quaternionDecoder) json

      return {
        Id = id |> Option.defaultValue 0
        Name = name |> Option.defaultValue ""
        Position = pos |> Option.defaultValue Vector3.Zero
        Shape = shape |> Option.defaultValue(MapObjectShape.Box Vector3.One)
        Data = data |> Option.defaultValue(MapObjectData.Trigger "")
        Rotation = rot |> Option.map ValueSome |> Option.defaultValue ValueNone
      }
    }

  // MapObject array encoder/decoder
  let mapObjectsEncoder: Encoder<MapObject[]> =
    fun mos -> mos |> Seq.map mapObjectEncoder |> Seq.toArray |> Json.sequence

  let mapObjectsDecoder: Decoder<MapObject[]> =
    fun json -> Decode.array (fun i -> mapObjectDecoder) json

  // BlockMapDefinition encoder/decoder (root type)
  let blockMapDefinitionEncoder: Encoder<BlockMapDefinition> =
    fun bmd ->
      let obj =
        Json.empty()
        |> Encode.property("Version", Encode.int bmd.Version)
        |> Encode.property("Key", Encode.string bmd.Key)
        |> Encode.property("Dimensions", gridDimensionsEncoder bmd.Dimensions)
        |> Encode.property("Palette", paletteEncoder bmd.Palette)
        |> Encode.property("Blocks", blocksEncoder bmd.Blocks)
        |> Encode.property("Settings", mapSettingsEncoder bmd.Settings)
        |> Encode.property("Objects", mapObjectsEncoder bmd.Objects)
      // Optional fields only if present
      let obj =
        match bmd.MapKey with
        | ValueSome mk -> Encode.property ("MapKey", Encode.string mk) obj
        | ValueNone -> obj

      let obj =
        match bmd.SpawnCell with
        | ValueSome sc -> Encode.property ("SpawnCell", vector3Encoder sc) obj
        | ValueNone -> obj

      obj :> JsonNode

  let blockMapDefinitionDecoder: Decoder<BlockMapDefinition> =
    fun json -> decode {
      let! version = Optional.Property.get ("Version", Decode.Required.int) json
      and! key = Optional.Property.get ("Key", Decode.Required.string) json

      and! dims =
        Optional.Property.get ("Dimensions", gridDimensionsDecoder) json

      and! palette = Optional.Property.get ("Palette", paletteDecoder) json
      and! blocks = Optional.Property.get ("Blocks", blocksDecoder) json

      and! settings =
        Optional.Property.get ("Settings", mapSettingsDecoder) json

      and! objects = Optional.Property.get ("Objects", mapObjectsDecoder) json

      and! mapKey =
        Optional.Property.get ("MapKey", Decode.Required.string) json

      and! spawnCell = Optional.Property.get ("SpawnCell", vector3Decoder) json

      return {
        Version = version |> Option.defaultValue 1
        Key = key |> Option.defaultValue "NewMap"
        MapKey = mapKey |> Option.map ValueSome |> Option.defaultValue ValueNone
        Dimensions = dims |> Option.defaultValue GridDimensions.Default
        Palette = palette |> Option.defaultValue(Dictionary())
        Blocks = blocks |> Option.defaultValue(Dictionary())
        SpawnCell =
          spawnCell |> Option.map ValueSome |> Option.defaultValue ValueNone
        Settings =
          settings
          |> Option.defaultValue {
            EngagementRules = EngagementRules.PvE
            MaxEnemyEntities = 0
            StartingLayer = 0
          }
        Objects = objects |> Option.defaultValue Array.empty
      }
    }

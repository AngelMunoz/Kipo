namespace Pomo.Core.Domain

open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Spatial

module BlockMap =

  [<Struct>]
  type CollisionType =
    | Box
    | Mesh // Uses model's mesh for collision (slopes, stairs)
    | NoCollision

  [<Struct>]
  type ZoneEffect =
    | Slow
    | Damage
    | Heal
    | Ice
    | Lava
    | Water

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

  [<Struct>]
  type SpawnData = {
    GroupId: string
    SpawnChance: float32
  }

  [<Struct>]
  type MapObjectData =
    | Spawn of SpawnData
    | Teleport of targetMapId: string
    | Trigger of triggerId: string

  type MapObject = {
    Id: int
    Position: WorldPosition
    Rotation: Quaternion
    Data: MapObjectData
  }

  [<Struct>]
  type PlacedBlock = {
    Cell: GridCell3D
    BlockTypeId: int<BlockTypeId>
    Rotation: Quaternion voption
  }

  type BlockType = {
    Id: int<BlockTypeId>
    Name: string
    Model: string
    Category: string
    CollisionType: CollisionType
    ZoneEffect: ZoneEffect voption
  }

  type BlockMapDefinition = {
    Version: int
    Key: string
    Width: int
    Height: int
    Depth: int
    Palette: Dictionary<int<BlockTypeId>, BlockType>
    Blocks: Dictionary<GridCell3D, PlacedBlock>
    SpawnCell: GridCell3D voption
    Settings: MapSettings
    Objects: MapObject list
    ZoneOverlay: Dictionary<GridCell3D, ZoneEffect>
  }

  let CellSize = 32.0f

  let inline createEmpty
    (key: string)
    (width: int)
    (height: int)
    (depth: int)
    : BlockMapDefinition =
    {
      Version = 1
      Key = key
      Width = width
      Height = height
      Depth = depth
      Palette = Dictionary()
      Blocks = Dictionary()
      SpawnCell = ValueNone
      Settings = {
        EngagementRules = PvE
        MaxEnemyEntities = 50
        StartingLayer = 0
      }
      Objects = []
      ZoneOverlay = Dictionary()
    }

  let getZoneEffect
    (cell: GridCell3D)
    (map: BlockMapDefinition)
    : ZoneEffect voption =
    map.ZoneOverlay
    |> Dictionary.tryFindV cell
    |> ValueOption.orElseWith(fun () ->
      map.Blocks
      |> Dictionary.tryFindV cell
      |> ValueOption.bind(fun block ->
        map.Palette
        |> Dictionary.tryFindV block.BlockTypeId
        |> ValueOption.bind(fun blockType -> blockType.ZoneEffect)))

  let inline cellToWorldPosition(cell: GridCell3D) : WorldPosition = {
    X = float32 cell.X * CellSize + CellSize / 2.0f
    Y = float32 cell.Y * CellSize + CellSize / 2.0f
    Z = float32 cell.Z * CellSize + CellSize / 2.0f
  }

  let inline worldPositionToCell(pos: WorldPosition) : GridCell3D = {
    X = int(pos.X / CellSize)
    Y = int(pos.Y / CellSize)
    Z = int(pos.Z / CellSize)
  }

  let inline tryGetBlock
    (map: BlockMapDefinition)
    (cell: GridCell3D)
    : PlacedBlock voption =
    map.Blocks |> Dictionary.tryFindV cell

  let inline isInBounds (map: BlockMapDefinition) (cell: GridCell3D) : bool =
    cell.X >= 0
    && cell.X < map.Width
    && cell.Y >= 0
    && cell.Y < map.Height
    && cell.Z >= 0
    && cell.Z < map.Depth

  let inline getBlockType
    (map: BlockMapDefinition)
    (block: PlacedBlock)
    : BlockType voption =
    map.Palette |> Dictionary.tryFindV block.BlockTypeId

  module Serialization =
    open System.Text.Json
    open System.Text.Json.Nodes
    open JDeck
    open JDeck.Decode
    open JDeck.Encoding

    let gridCell3DDecoder: Decoder<GridCell3D> =
      fun json -> decode {
        let! x = Required.Property.get ("X", Required.int) json
        and! y = Required.Property.get ("Y", Required.int) json
        and! z = Required.Property.get ("Z", Required.int) json
        return { X = x; Y = y; Z = z }
      }

    let gridCell3DEncoder: Encoder<GridCell3D> =
      fun value ->
        Json.empty()
        |> Encode.property("X", Encode.int value.X)
        |> Encode.property("Y", Encode.int value.Y)
        |> Encode.property("Z", Encode.int value.Z)
        :> JsonNode



    let quaternionDecoder: Decoder<Quaternion> =
      fun json -> decode {
        let! x = Required.Property.get ("X", Decode.Required.float32) json
        and! y = Required.Property.get ("Y", Decode.Required.float32) json
        and! z = Required.Property.get ("Z", Decode.Required.float32) json
        and! w = Required.Property.get ("W", Decode.Required.float32) json
        return Quaternion(x, y, z, w)
      }

    let quaternionEncoder: Encoder<Quaternion> =
      fun value ->
        Json.empty()
        |> Encode.property("X", Encode.float32 value.X)
        |> Encode.property("Y", Encode.float32 value.Y)
        |> Encode.property("Z", Encode.float32 value.Z)
        |> Encode.property("W", Encode.float32 value.W)
        :> JsonNode

    let collisionTypeDecoder: Decoder<CollisionType> =
      fun json -> decode {
        let! typeString = Required.Property.get ("Type", Required.string) json

        match typeString with
        | "Box" -> return Box
        | "NoCollision" -> return NoCollision
        | "Mesh" -> return Mesh
        | _ ->
          return!
            Error(
              DecodeError.ofError(json, $"Unknown CollisionType: {typeString}")
            )
      }

    let collisionTypeEncoder: Encoder<CollisionType> =
      fun value ->
        let typeString =
          match value with
          | Box -> "Box"
          | NoCollision -> "NoCollision"
          | Mesh -> "Mesh"

        Json.object [ "Type", Encode.string typeString ]

    let zoneEffectDecoder: Decoder<ZoneEffect> =
      fun json -> decode {
        let! typeString = Required.string json

        match typeString with
        | "Slow" -> return Slow
        | "Damage" -> return Damage
        | "Heal" -> return Heal
        | "Ice" -> return Ice
        | "Lava" -> return Lava
        | "Water" -> return Water
        | _ ->
          return!
            Error(
              DecodeError.ofError(json, $"Unknown ZoneEffect: {typeString}")
            )
      }

    let zoneEffectEncoder: Encoder<ZoneEffect> =
      fun value ->
        let str =
          match value with
          | Slow -> "Slow"
          | Damage -> "Damage"
          | Heal -> "Heal"
          | Ice -> "Ice"
          | Lava -> "Lava"
          | Water -> "Water"

        Encode.string str

    let blockTypeDecoder: Decoder<BlockType> =
      fun json -> decode {
        let! id = Required.Property.get ("Id", Required.int) json
        and! name = Required.Property.get ("Name", Required.string) json
        and! model = Required.Property.get ("Model", Required.string) json

        and! category =
          VOptional.Property.get ("Category", Required.string) json
          |> Result.map(ValueOption.defaultValue "Terrain")

        and! collisionType =
          VOptional.Property.get ("CollisionType", collisionTypeDecoder) json
          |> Result.map(ValueOption.defaultValue Box)

        and! zoneEffect =
          VOptional.Property.get ("ZoneEffect", zoneEffectDecoder) json

        return {
          Id = id * 1<BlockTypeId>
          Name = name
          Model = model
          Category = category
          CollisionType = collisionType
          ZoneEffect = zoneEffect
        }
      }

    let blockTypeEncoder: Encoder<BlockType> =
      fun value ->
        Json.object [
          "Id", Encode.int(value.Id |> UMX.untag)
          "Name", Encode.string value.Name
          "Model", Encode.string value.Model
          "Category", Encode.string value.Category
          "CollisionType", collisionTypeEncoder value.CollisionType
          match value.ZoneEffect with
          | ValueSome effect -> "ZoneEffect", zoneEffectEncoder effect
          | ValueNone -> ()
        ]

    let placedBlockDecoder: Decoder<PlacedBlock> =
      fun json -> decode {
        let! cell = Required.Property.get ("Cell", gridCell3DDecoder) json

        and! blockTypeId =
          Required.Property.get ("BlockTypeId", Required.int) json

        and! rotation =
          VOptional.Property.get ("Rotation", quaternionDecoder) json

        return {
          Cell = cell
          BlockTypeId = %blockTypeId
          Rotation = rotation
        }
      }

    let placedBlockEncoder: Encoder<PlacedBlock> =
      fun value ->
        Json.object [
          "Cell", gridCell3DEncoder value.Cell
          "BlockTypeId", Encode.int(value.BlockTypeId |> UMX.untag)
          match value.Rotation with
          | ValueSome rotation -> "Rotation", quaternionEncoder rotation
          | ValueNone -> ()
        ]

    let engagementRulesDecoder: Decoder<EngagementRules> =
      fun json -> decode {
        let! str = Required.string json

        match str with
        | "Peaceful" -> return Peaceful
        | "PvE" -> return PvE
        | "PvP" -> return PvP
        | "FFA" -> return FFA
        | _ ->
          return!
            Error(DecodeError.ofError(json, $"Unknown EngagementRules: {str}"))
      }

    let engagementRulesEncoder: Encoder<EngagementRules> =
      fun value ->
        let str =
          match value with
          | Peaceful -> "Peaceful"
          | PvE -> "PvE"
          | PvP -> "PvP"
          | FFA -> "FFA"

        Encode.string str

    let mapSettingsDecoder: Decoder<MapSettings> =
      fun json -> decode {
        let! engagement =
          VOptional.Property.get
            ("EngagementRules", engagementRulesDecoder)
            json
          |> Result.map(ValueOption.defaultValue PvE)

        and! maxEnemies =
          VOptional.Property.get ("MaxEnemyEntities", Required.int) json
          |> Result.map(ValueOption.defaultValue 50)

        and! startLayer =
          VOptional.Property.get ("StartingLayer", Required.int) json
          |> Result.map(ValueOption.defaultValue 0)

        return {
          EngagementRules = engagement
          MaxEnemyEntities = maxEnemies
          StartingLayer = startLayer
        }
      }

    let mapSettingsEncoder: Encoder<MapSettings> =
      fun value ->
        Json.object [
          "EngagementRules", engagementRulesEncoder value.EngagementRules
          "MaxEnemyEntities", Encode.int value.MaxEnemyEntities
          "StartingLayer", Encode.int value.StartingLayer
        ]

    let worldPositionDecoder: Decoder<WorldPosition> =
      fun json -> decode {
        let! x = Required.Property.get ("X", Decode.Required.float32) json
        and! y = Required.Property.get ("Y", Decode.Required.float32) json
        and! z = Required.Property.get ("Z", Decode.Required.float32) json
        return { X = x; Y = y; Z = z }
      }

    let worldPositionEncoder: Encoder<WorldPosition> =
      fun value ->
        Json.object [
          "X", Encode.float32 value.X
          "Y", Encode.float32 value.Y
          "Z", Encode.float32 value.Z
        ]

    let mapObjectDataDecoder: Decoder<MapObjectData> =
      fun json -> decode {
        let! typeString = Required.Property.get ("Type", Required.string) json

        match typeString with
        | "Spawn" ->
          let! groupId = Required.Property.get ("GroupId", Required.string) json

          and! chance =
            VOptional.Property.get ("SpawnChance", Decode.Required.float32) json
            |> Result.map(ValueOption.defaultValue 1.0f)

          return
            Spawn {
              GroupId = groupId
              SpawnChance = chance
            }
        | "Teleport" ->
          let! target =
            Required.Property.get ("TargetMapId", Required.string) json

          return Teleport target
        | "Trigger" ->
          let! triggerId =
            Required.Property.get ("TriggerId", Required.string) json

          return Trigger triggerId
        | _ ->
          return!
            Error(
              DecodeError.ofError(
                json,
                $"Unknown MapObjectData type: {typeString}"
              )
            )
      }

    let mapObjectDataEncoder: Encoder<MapObjectData> =
      fun value ->
        match value with
        | Spawn data ->
          Json.object [
            "Type", Encode.string "Spawn"
            "GroupId", Encode.string data.GroupId
            "SpawnChance", Encode.float32 data.SpawnChance
          ]
        | Teleport target ->
          Json.object [
            "Type", Encode.string "Teleport"
            "TargetMapId", Encode.string target
          ]
        | Trigger id ->
          Json.object [
            "Type", Encode.string "Trigger"
            "TriggerId", Encode.string id
          ]

    let mapObjectDecoder: Decoder<MapObject> =
      fun json -> decode {
        let! id = Required.Property.get ("Id", Required.int) json
        and! pos = Required.Property.get ("Position", worldPositionDecoder) json

        and! rot =
          VOptional.Property.get ("Rotation", quaternionDecoder) json
          |> Result.map(ValueOption.defaultValue Quaternion.Identity)

        and! data = Required.Property.get ("Data", mapObjectDataDecoder) json

        return {
          Id = id
          Position = pos
          Rotation = rot
          Data = data
        }
      }

    let mapObjectEncoder: Encoder<MapObject> =
      fun value ->
        Json.object [
          "Id", Encode.int value.Id
          "Position", worldPositionEncoder value.Position
          if value.Rotation <> Quaternion.Identity then
            "Rotation", quaternionEncoder value.Rotation
          "Data", mapObjectDataEncoder value.Data
        ]

    let blockMapDefinitionDecoder: Decoder<BlockMapDefinition> =
      fun json -> decode {
        let! version =
          VOptional.Property.get ("Version", Required.int) json
          |> Result.map(ValueOption.defaultValue 1)

        and! key = Required.Property.get ("Key", Required.string) json
        and! width = Required.Property.get ("Width", Required.int) json
        and! height = Required.Property.get ("Height", Required.int) json
        and! depth = Required.Property.get ("Depth", Required.int) json

        and! palette =
          Required.Property.get
            ("Palette",
             fun j -> decode {
               let inline decoder key elem = blockTypeDecoder elem

               let! dict =
                 Decode.dict decoder j
                 |> Result.map(fun dict ->
                   let newd = Dictionary()

                   for KeyValue(key, value) in dict do
                     newd.Add(int key |> UMX.tag<BlockTypeId>, value)

                   newd)

               return dict
             })
            json


        and! blocks =
          VOptional.Property.get
            ("Blocks",
             fun j -> decode {
               let inline decoder _ (elem: JsonElement) =
                 placedBlockDecoder elem
                 |> Result.map(fun pb -> struct (pb.Cell, pb))

               let! arr = Decode.array decoder j |> Result.map Dictionary.ofSeqV

               return arr
             })
            json

        and! spawnCell =
          VOptional.Property.get ("SpawnCell", gridCell3DDecoder) json

        and! settings =
          VOptional.Property.get ("Settings", mapSettingsDecoder) json
          |> Result.map(
            ValueOption.defaultValue {
              EngagementRules = PvE
              MaxEnemyEntities = 50
              StartingLayer = 0
            }
          )

        and! objects =
          VOptional.Property.get
            ("Objects", fun j -> Decode.list (fun _ e -> mapObjectDecoder e) j)
            json
          |> Result.map(ValueOption.defaultValue [])

        and! zoneOverlay =
          VOptional.Property.get
            ("ZoneOverlay",
             fun j -> decode {
               let inline decoder _ (elem: JsonElement) = decode {
                 let! cell =
                   Required.Property.get ("Cell", gridCell3DDecoder) elem

                 and! effect =
                   Required.Property.get ("Effect", zoneEffectDecoder) elem

                 return struct (cell, effect)
               }

               let! arr = Decode.array decoder j |> Result.map Dictionary.ofSeqV
               return arr
             })
            json

        return {
          Version = version
          Key = key
          Width = width
          Height = height
          Depth = depth
          Palette = palette
          Blocks = defaultValueArg blocks (Dictionary())
          SpawnCell = spawnCell
          Settings = settings
          Objects = objects
          ZoneOverlay = defaultValueArg zoneOverlay (Dictionary())
        }
      }

    let encodeBlockMapDefinition(map: BlockMapDefinition) : JsonNode =
      let paletteEncoder = fun (key, value) -> $"{key}", blockTypeEncoder value
      let blocks = seq { for kv in map.Blocks -> kv.Value }

      let zoneOverlaySeq = seq {
        for kv in map.ZoneOverlay ->
          Json.object [
            "Cell", gridCell3DEncoder kv.Key
            "Effect", zoneEffectEncoder kv.Value
          ]
      }

      Json.object [
        "Version", Encode.int map.Version
        "Key", Encode.string map.Key
        "Width", Encode.int map.Width
        "Height", Encode.int map.Height
        "Depth", Encode.int map.Depth
        "Palette", Encode.map(map.Palette, paletteEncoder)
        "Blocks", Json.sequence(blocks, placedBlockEncoder)
        match map.SpawnCell with
        | ValueSome cell -> "SpawnCell", gridCell3DEncoder cell
        | ValueNone -> ()
        "Settings", mapSettingsEncoder map.Settings
        if not(List.isEmpty map.Objects) then
          "Objects", Json.sequence(map.Objects, mapObjectEncoder)
        if map.ZoneOverlay.Count > 0 then
          "ZoneOverlay",
          JsonArray(
            zoneOverlaySeq |> Seq.map(fun x -> x :> JsonNode) |> Seq.toArray
          )
          :> JsonNode
      ]

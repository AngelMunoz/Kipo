namespace Pomo.Core.Domain

open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.Skill

module BlockMap =

  [<Struct>]
  type CollisionType =
    | Box
    | Mesh // Uses model's mesh for collision (slopes, stairs)
    | NoCollision


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
    Faction: Faction voption
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
    Position: WorldPosition
    Rotation: Quaternion voption
    Shape: MapObjectShape
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
    Effect: Effect voption
  }

  type BlockMapDefinition = {
    Version: int
    Key: string
    MapKey: string voption
    Width: int
    Height: int
    Depth: int
    Palette: Dictionary<int<BlockTypeId>, BlockType>
    Blocks: Dictionary<GridCell3D, PlacedBlock>
    SpawnCell: GridCell3D voption
    Settings: MapSettings
    Objects: MapObject list
  }

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

    let vector3Decoder: Decoder<Vector3> =
      fun json -> decode {
        let! x = Required.Property.get ("X", Decode.Required.float32) json
        and! y = Required.Property.get ("Y", Decode.Required.float32) json
        and! z = Required.Property.get ("Z", Decode.Required.float32) json
        return Vector3(x, y, z)
      }

    let vector3Encoder: Encoder<Vector3> =
      fun value ->
        Json.object [
          "X", Encode.float32 value.X
          "Y", Encode.float32 value.Y
          "Z", Encode.float32 value.Z
        ]

    let mapObjectShapeDecoder: Decoder<MapObjectShape> =
      fun json -> decode {
        let! typeString = Required.Property.get ("Type", Required.string) json

        match typeString with
        | "Box" ->
          let! size = Required.Property.get ("Size", vector3Decoder) json
          return MapObjectShape.Box size
        | "Sphere" ->
          let! radius =
            Required.Property.get ("Radius", Decode.Required.float32) json

          return MapObjectShape.Sphere radius
        | _ ->
          return!
            Error(
              DecodeError.ofError(json, $"Unknown MapObjectShape: {typeString}")
            )
      }

    let mapObjectShapeEncoder: Encoder<MapObjectShape> =
      fun value ->
        match value with
        | MapObjectShape.Box size ->
          Json.object [
            "Type", Encode.string "Box"
            "Size", vector3Encoder size
          ]
        | MapObjectShape.Sphere radius ->
          Json.object [
            "Type", Encode.string "Sphere"
            "Radius", Encode.float32 radius
          ]

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

        and! effect =
          VOptional.Property.get
            ("Effect", Skill.Serialization.Effect.decoder)
            json

        return {
          Id = id * 1<BlockTypeId>
          Name = name
          Model = model
          Category = category
          CollisionType = collisionType
          Effect = effect
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
          match value.Effect with
          | ValueSome effect ->
            "Effect", Skill.Serialization.Effect.encoder effect
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

    let spawnPropertiesDecoder: Decoder<SpawnProperties> =
      fun json -> decode {
        let! isPlayerSpawn =
          VOptional.Property.get ("IsPlayerSpawn", Required.boolean) json
          |> Result.map(ValueOption.defaultValue false)

        and! entityGroup =
          VOptional.Property.get ("EntityGroup", Required.string) json

        and! maxSpawns =
          VOptional.Property.get ("MaxSpawns", Required.int) json
          |> Result.map(ValueOption.defaultValue 1)

        and! faction =
          VOptional.Property.get
            ("Faction", Pomo.Core.Domain.Entity.Serialization.Faction.decoder)
            json

        return {
          IsPlayerSpawn = isPlayerSpawn
          EntityGroup = entityGroup
          MaxSpawns = maxSpawns
          Faction = faction
        }
      }

    let spawnPropertiesEncoder: Encoder<SpawnProperties> =
      fun value ->
        Json.object [
          "IsPlayerSpawn", Encode.boolean value.IsPlayerSpawn
          match value.EntityGroup with
          | ValueSome g -> "EntityGroup", Encode.string g
          | ValueNone -> ()
          "MaxSpawns", Encode.int value.MaxSpawns
          match value.Faction with
          | ValueSome f ->
            "Faction", Pomo.Core.Domain.Entity.Serialization.Faction.encoder f
          | ValueNone -> ()
        ]

    let teleportPropertiesDecoder: Decoder<TeleportProperties> =
      fun json -> decode {
        let! targetMap =
          VOptional.Property.get ("TargetMap", Required.string) json

        and! targetObjectName =
          Required.Property.get ("TargetObjectName", Required.string) json

        return {
          TargetMap = targetMap
          TargetObjectName = targetObjectName
        }
      }

    let teleportPropertiesEncoder: Encoder<TeleportProperties> =
      fun value ->
        Json.object [
          match value.TargetMap with
          | ValueSome m -> "TargetMap", Encode.string m
          | ValueNone -> ()
          "TargetObjectName", Encode.string value.TargetObjectName
        ]

    let mapObjectDataDecoder: Decoder<MapObjectData> =
      fun json -> decode {
        let! typeString = Required.Property.get ("Type", Required.string) json

        match typeString with
        | "Spawn" ->
          let! props = spawnPropertiesDecoder json
          return Spawn props
        | "Teleport" ->
          let! props = teleportPropertiesDecoder json
          return Teleport props
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
            "IsPlayerSpawn", Encode.boolean data.IsPlayerSpawn
            match data.EntityGroup with
            | ValueSome g -> "EntityGroup", Encode.string g
            | ValueNone -> ()
            "MaxSpawns", Encode.int data.MaxSpawns
            match data.Faction with
            | ValueSome f ->
              "Faction", Pomo.Core.Domain.Entity.Serialization.Faction.encoder f
            | ValueNone -> ()
          ]
        | Teleport props ->
          Json.object [
            "Type", Encode.string "Teleport"
            match props.TargetMap with
            | ValueSome m -> "TargetMap", Encode.string m
            | ValueNone -> ()
            "TargetObjectName", Encode.string props.TargetObjectName
          ]
        | Trigger id ->
          Json.object [
            "Type", Encode.string "Trigger"
            "TriggerId", Encode.string id
          ]

    let mapObjectDecoder: Decoder<MapObject> =
      fun json -> decode {
        let! id = Required.Property.get ("Id", Required.int) json
        and! name = Required.Property.get ("Name", Required.string) json
        and! pos = Required.Property.get ("Position", worldPositionDecoder) json
        and! rot = VOptional.Property.get ("Rotation", quaternionDecoder) json
        and! shape = Required.Property.get ("Shape", mapObjectShapeDecoder) json
        and! data = Required.Property.get ("Data", mapObjectDataDecoder) json

        return {
          Id = id
          Name = name
          Position = pos
          Rotation = rot
          Shape = shape
          Data = data
        }
      }

    let mapObjectEncoder: Encoder<MapObject> =
      fun value ->
        Json.object [
          "Id", Encode.int value.Id
          "Name", Encode.string value.Name
          "Position", worldPositionEncoder value.Position
          match value.Rotation with
          | ValueSome rot -> "Rotation", quaternionEncoder rot
          | ValueNone -> ()
          "Shape", mapObjectShapeEncoder value.Shape
          "Data", mapObjectDataEncoder value.Data
        ]

    let blockMapDefinitionDecoder: Decoder<BlockMapDefinition> =
      fun json -> decode {
        let! version =
          VOptional.Property.get ("Version", Required.int) json
          |> Result.map(ValueOption.defaultValue 1)

        and! key = Required.Property.get ("Key", Required.string) json

        and! mapKey = VOptional.Property.get ("MapKey", Required.string) json
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

        return {
          Version = version
          Key = key
          MapKey = mapKey
          Width = width
          Height = height
          Depth = depth
          Palette = palette
          Blocks = defaultValueArg blocks (Dictionary())
          SpawnCell = spawnCell
          Settings = settings
          Objects = objects
        }
      }

    let encodeBlockMapDefinition(map: BlockMapDefinition) : JsonNode =
      let paletteEncoder = fun (key, value) -> $"{key}", blockTypeEncoder value
      let blocks = seq { for kv in map.Blocks -> kv.Value }

      Json.object [
        "Version", Encode.int map.Version
        "Key", Encode.string map.Key
        match map.MapKey with
        | ValueSome k -> "MapKey", Encode.string k
        | ValueNone -> ()
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
      ]

namespace Pomo.Lib.Editor.Subsystems

open System
open Microsoft.Xna.Framework
open System.Collections.Generic
open FSharp.UMX
open Mibo.Elmish
open Pomo.Lib
open Pomo.Lib.Editor
open Pomo.Lib.Services

module BlockMap =
  open Pomo.Lib.BlockMap

  [<Struct>]
  type CollisionType =
    | Box
    | Mesh
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


  type BlockType = {
    Id: int<BlockTypeId>
    ArchetypeId: int<BlockTypeId>
    VariantKey: string voption
    Name: string
    Model: string
    Category: string
    CollisionType: CollisionType
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

  // --- Elmish Module ---

  [<Struct>]
  type BlockMapModel = {
    Definition: BlockMapDefinition
    Cursor: Vector3 voption
    Dirty: bool
  }

  type BlockMapMsg =
    | PlaceBlock of cell: Vector3 * blockId: int<BlockTypeId>
    | RemoveBlock of cell: Vector3
    | SetCursor of Vector3 voption
    | SetMap of BlockMapDefinition



  let init
    (_env: #FileSystemCap & #AssetsCap)
    (mapDef: BlockMapDefinition)
    : BlockMapModel =
    {
      Definition = mapDef
      Cursor = ValueNone
      Dirty = false
    }

  let update
    (_env: #FileSystemCap & #AssetsCap)
    (msg: BlockMapMsg)
    (model: BlockMapModel)
    : struct (BlockMapModel * Cmd<BlockMapMsg>) =
    match msg with
    | PlaceBlock(cell, blockId) ->
      model.Definition.Blocks.Add(
        cell,
        {
          Cell = cell
          BlockTypeId = blockId
          Rotation = ValueNone
        }
      )

      { model with Dirty = true }, Cmd.none
    | RemoveBlock cell ->
      model.Definition.Blocks.Remove cell |> ignore
      { model with Dirty = true }, Cmd.none
    | SetCursor cursor -> { model with Cursor = cursor }, Cmd.none
    | SetMap map ->
      {
        model with
            Definition = map
            Dirty = false
      },
      Cmd.none

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

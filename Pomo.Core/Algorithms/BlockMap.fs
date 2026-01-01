namespace Pomo.Core.Algorithms

open Pomo.Core
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Spatial

module BlockMap =

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
      MapKey = ValueSome key
      Width = width
      Height = height
      Depth = depth
      Palette = System.Collections.Generic.Dictionary()
      Blocks = System.Collections.Generic.Dictionary()
      SpawnCell = ValueNone
      Settings = {
        EngagementRules = EngagementRules.PvE
        MaxEnemyEntities = 50
        StartingLayer = 0
      }
      Objects = []
    }

  let getBlockEffect
    (cell: GridCell3D)
    (map: BlockMapDefinition)
    : Effect voption =
    map.Blocks
    |> Dictionary.tryFindV cell
    |> ValueOption.bind(fun block ->
      map.Palette
      |> Dictionary.tryFindV block.BlockTypeId
      |> ValueOption.bind(fun blockType -> blockType.Effect))

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

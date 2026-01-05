namespace Pomo.Core.Tests

open Microsoft.VisualStudio.TestTools.UnitTesting
open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Spatial
open Pomo.Core.Algorithms

[<TestClass>]
type Spatial3DTests() =

  let cellSize = Pomo.Core.Domain.Core.Constants.BlockMap.CellSize

  let mkBlockType (id: int) (collision: CollisionType) : BlockType = {
    Id = %id
    ArchetypeId = %id
    VariantKey = ValueNone
    Name = $"Block{id}"
    Model = "Tiles/test"
    Category = "Terrain"
    CollisionType = collision
    Effect = ValueNone
  }

  let addBlockType
    (map: BlockMapDefinition)
    (id: int)
    (collision: CollisionType)
    =
    let bt = mkBlockType id collision
    map.Palette.Add(bt.Id, bt)
    bt.Id

  let addBlock
    (map: BlockMapDefinition)
    (blockTypeId: int<BlockTypeId>)
    (cell: GridCell3D)
    =
    let placed: PlacedBlock = {
      Cell = cell
      BlockTypeId = blockTypeId
      Rotation = ValueNone
    }

    map.Blocks.Add(cell, placed)

  [<TestMethod>]
  member _.``tryGetSurfaceHeightWithConfig out of bounds returns none``() =
    let map = Pomo.Core.Algorithms.BlockMap.createEmpty "test" 4 4 4

    let pos: WorldPosition = {
      X = -1.0f * cellSize
      Y = 0.0f
      Z = 1.0f * cellSize
    }

    let result = Spatial3D.tryGetSurfaceHeightWithConfig ValueNone map pos

    Assert.AreEqual(ValueNone, result)

  [<TestMethod>]
  member _.``tryGetSurfaceHeightWithConfig empty column returns zero``() =
    let map = Pomo.Core.Algorithms.BlockMap.createEmpty "test" 4 4 4

    let pos: WorldPosition = {
      X = cellSize * 0.5f
      Y = 0.0f
      Z = cellSize * 0.5f
    }

    let result = Spatial3D.tryGetSurfaceHeightWithConfig ValueNone map pos

    Assert.AreEqual(ValueSome 0.0f, result)

  [<TestMethod>]
  member _.``tryGetSurfaceHeightWithConfig returns top surface``() =
    let map = Pomo.Core.Algorithms.BlockMap.createEmpty "test" 4 6 4
    let blockId = addBlockType map 1 Box

    addBlock map blockId { X = 0; Y = 0; Z = 0 }
    addBlock map blockId { X = 0; Y = 3; Z = 0 }

    let pos: WorldPosition = {
      X = cellSize * 0.5f
      Y = 0.0f
      Z = cellSize * 0.5f
    }

    let result = Spatial3D.tryGetSurfaceHeightWithConfig ValueNone map pos

    Assert.AreEqual(ValueSome(4.0f * cellSize), result)

  [<TestMethod>]
  member _.``canOccupyWithConfig false when blocked``() =
    let map = Pomo.Core.Algorithms.BlockMap.createEmpty "test" 4 4 4
    let blockId = addBlockType map 1 Box

    addBlock map blockId { X = 0; Y = 0; Z = 0 }

    let pos: WorldPosition = {
      X = cellSize * 0.5f
      Y = 0.0f
      Z = cellSize * 0.5f
    }

    let ok = Spatial3D.canOccupyWithConfig ValueNone map pos cellSize

    Assert.IsFalse ok

  [<TestMethod>]
  member _.``canOccupyWithConfig checks vertical clearance``() =
    let map = Pomo.Core.Algorithms.BlockMap.createEmpty "test" 4 4 4
    let blockId = addBlockType map 1 Box

    addBlock map blockId { X = 0; Y = 1; Z = 0 }

    let pos: WorldPosition = {
      X = cellSize * 0.5f
      Y = 0.0f
      Z = cellSize * 0.5f
    }

    let ok = Spatial3D.canOccupyWithConfig ValueNone map pos (cellSize * 2.0f)

    Assert.IsFalse ok

  [<TestMethod>]
  member _.``canStandInCellWithConfig requires support below``() =
    let map = Pomo.Core.Algorithms.BlockMap.createEmpty "test" 4 4 4
    let blockId = addBlockType map 1 Box

    addBlock map blockId { X = 0; Y = 0; Z = 0 }

    let supported =
      Spatial3D.canStandInCellWithConfig
        ValueNone
        map
        { X = 0; Y = 1; Z = 0 }
        cellSize

    let unsupported =
      Spatial3D.canStandInCellWithConfig
        ValueNone
        map
        { X = 1; Y = 1; Z = 0 }
        cellSize

    Assert.IsTrue supported
    Assert.IsFalse unsupported

  [<TestMethod>]
  member _.``canTraverseWithConfig rejects too large step``() =
    let map = Pomo.Core.Algorithms.BlockMap.createEmpty "test" 4 6 4
    let blockId = addBlockType map 1 Box

    addBlock map blockId { X = 0; Y = 0; Z = 0 }

    addBlock map blockId { X = 1; Y = 0; Z = 0 }
    addBlock map blockId { X = 1; Y = 1; Z = 0 }
    addBlock map blockId { X = 1; Y = 2; Z = 0 }

    let startPos: WorldPosition = {
      X = 0.5f * cellSize
      Y = 1.0f * cellSize
      Z = 0.5f * cellSize
    }

    let targetPos: WorldPosition = {
      X = 1.5f * cellSize
      Y = 3.0f * cellSize
      Z = 0.5f * cellSize
    }

    let ok =
      Spatial3D.canTraverseWithConfig
        ValueNone
        map
        startPos
        targetPos
        cellSize
        cellSize

    Assert.IsFalse ok

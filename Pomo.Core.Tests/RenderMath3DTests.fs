namespace Pomo.Core.Tests

open Microsoft.VisualStudio.TestTools.UnitTesting
open Microsoft.Xna.Framework
open Pomo.Core.Graphics
open Pomo.Core.Domain.Core.Constants

[<TestClass>]
type RenderMath3DTests() =

  [<TestMethod>]
  member _.CalcCenterOffsetCentersMap() =
    let ppu = 32.0f
    let width = 10
    let depth = 6

    let offset = RenderMath.BlockMap3D.calcCenterOffset width depth ppu
    let scaleFactor = BlockMap.CellSize / ppu

    Assert.AreEqual(-float32 width * scaleFactor * 0.5f, offset.X, 0.0001f)
    Assert.AreEqual(0.0f, offset.Y, 0.0001f)
    Assert.AreEqual(-float32 depth * scaleFactor * 0.5f, offset.Z, 0.0001f)

  [<TestMethod>]
  member _.CellToRenderReturnsCellCenter() =
    let ppu = 16.0f
    let centerOffset = RenderMath.BlockMap3D.calcCenterOffset 2 2 ppu

    let renderPos = RenderMath.BlockMap3D.cellToRender 0 0 0 ppu centerOffset

    let scaleFactor = BlockMap.CellSize / ppu
    let halfCell = scaleFactor * 0.5f

    Assert.AreEqual(centerOffset.X + halfCell, renderPos.X, 0.0001f)
    Assert.AreEqual(centerOffset.Y + halfCell, renderPos.Y, 0.0001f)
    Assert.AreEqual(centerOffset.Z + halfCell, renderPos.Z, 0.0001f)

  [<TestMethod>]
  member _.GetViewCellBounds3DComputesExpectedIndices() =
    let viewBounds = struct (2.0f, 6.0f, 3.0f, 7.0f)
    let cameraY = 5.0f
    let cellSize = 1.0f
    let visibleHeightRange = 2.0f

    let struct (minX, maxX, minY, maxY, minZ, maxZ) =
      RenderMath.Camera.getViewCellBounds3D
        viewBounds
        cameraY
        cellSize
        visibleHeightRange

    Assert.AreEqual(0, minX)
    Assert.AreEqual(9, maxX)
    Assert.AreEqual(3, minY)
    Assert.AreEqual(8, maxY)
    Assert.AreEqual(1, minZ)
    Assert.AreEqual(10, maxZ)

    Assert.IsTrue(
      RenderMath.Camera.isInCellBounds
        0
        3
        1
        (struct (minX, maxX, minY, maxY, minZ, maxZ))
    )

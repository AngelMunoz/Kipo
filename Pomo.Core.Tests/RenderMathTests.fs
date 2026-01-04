module RenderMathTests

open Microsoft.VisualStudio.TestTools.UnitTesting
open Microsoft.Xna.Framework
open Pomo.Core.Graphics
open Pomo.Core.Domain.Core

[<TestClass>]
type CameraViewBoundsTests() =

  [<TestMethod>]
  member _.``centered camera has symmetric bounds``() =
    let cameraPos = WorldPosition.zero

    let struct (left, right, top, bottom) =
      RenderMath.Camera.getViewBounds cameraPos 800.0f 600.0f 1.0f

    Assert.AreEqual(-400.0f, left)
    Assert.AreEqual(400.0f, right)
    Assert.AreEqual(-300.0f, top)
    Assert.AreEqual(300.0f, bottom)

  [<TestMethod>]
  member _.``zoom shrinks visible area``() =
    let cameraPos = WorldPosition.zero

    let struct (left, right, _, _) =
      RenderMath.Camera.getViewBounds cameraPos 800.0f 600.0f 2.0f

    Assert.AreEqual(-200.0f, left)
    Assert.AreEqual(200.0f, right)

  [<TestMethod>]
  member _.``camera position offsets bounds``() =
    let cameraPos = { X = 100.0f; Y = 0.0f; Z = 50.0f }

    let struct (left, right, top, bottom) =
      RenderMath.Camera.getViewBounds cameraPos 800.0f 600.0f 1.0f

    Assert.AreEqual(-300.0f, left)
    Assert.AreEqual(500.0f, right)
    Assert.AreEqual(-250.0f, top)
    Assert.AreEqual(350.0f, bottom)


[<TestClass>]
type CameraViewCellBoundsTests() =

  [<TestMethod>]
  member _.``getViewCellBounds3D returns valid cell bounds``() =
    let viewBounds = struct (-100.0f, 100.0f, -100.0f, 100.0f)
    let cameraY = 0.0f
    let cellSize = 10.0f
    let visibleHeightRange = 50.0f

    let struct (minX, maxX, minY, maxY, minZ, maxZ) =
      RenderMath.Camera.getViewCellBounds3D
        viewBounds
        cameraY
        cellSize
        visibleHeightRange

    Assert.IsTrue(minX <= maxX)
    Assert.IsTrue(minY <= maxY)
    Assert.IsTrue(minZ <= maxZ)

  [<TestMethod>]
  member _.``isInCellBounds correctly identifies cells within bounds``() =
    let bounds = struct (0, 10, 0, 5, 0, 10)

    Assert.IsTrue(RenderMath.Camera.isInCellBounds 5 2 5 bounds)
    Assert.IsFalse(RenderMath.Camera.isInCellBounds -1 2 5 bounds)
    Assert.IsFalse(RenderMath.Camera.isInCellBounds 5 -1 5 bounds)
    Assert.IsFalse(RenderMath.Camera.isInCellBounds 5 2 -1 bounds)
    Assert.IsFalse(RenderMath.Camera.isInCellBounds 11 2 5 bounds)


[<TestClass>]
type WorldMatrix3DTests() =

  [<TestMethod>]
  member _.``createMesh translation equals render position``() =
    let renderPos = Vector3(10.0f, 20.0f, 30.0f)
    let world = RenderMath.WorldMatrix3D.createMesh renderPos 0.0f 1.0f
    let translation = world.Translation

    Assert.AreEqual(renderPos.X, translation.X, 0.0001f)
    Assert.AreEqual(renderPos.Y, translation.Y, 0.0001f)
    Assert.AreEqual(renderPos.Z, translation.Z, 0.0001f)

  [<TestMethod>]
  member _.``createMesh with scale affects matrix``() =
    let renderPos = Vector3.Zero
    let scale = 2.0f
    let world = RenderMath.WorldMatrix3D.createMesh renderPos 0.0f scale

    Assert.AreEqual(scale, world.M11, 0.0001f)
    Assert.AreEqual(scale, world.M22, 0.0001f)
    Assert.AreEqual(scale, world.M33, 0.0001f)

  [<TestMethod>]
  member _.``createProjectile translation equals render position``() =
    let renderPos = Vector3(5.0f, 10.0f, 15.0f)

    let world =
      RenderMath.WorldMatrix3D.createProjectile renderPos 0.0f 0.0f 1.0f

    let translation = world.Translation

    Assert.AreEqual(renderPos.X, translation.X, 0.0001f)
    Assert.AreEqual(renderPos.Y, translation.Y, 0.0001f)
    Assert.AreEqual(renderPos.Z, translation.Z, 0.0001f)

  [<TestMethod>]
  member _.``createMeshParticle includes pivot offset``() =
    let renderPos = Vector3(10.0f, 20.0f, 30.0f)
    let pivot = Vector3(1.0f, 2.0f, 3.0f)

    let world =
      RenderMath.WorldMatrix3D.createMeshParticle
        renderPos
        Quaternion.Identity
        1.0f
        Vector3.One
        pivot

    let translation = world.Translation

    Assert.AreEqual(renderPos.X + pivot.X, translation.X, 0.1f)
    Assert.AreEqual(renderPos.Y + pivot.Y, translation.Y, 0.1f)
    Assert.AreEqual(renderPos.Z + pivot.Z, translation.Z, 0.1f)


[<TestClass>]
type RigTransformTests() =

  [<TestMethod>]
  member _.``applyNodeTransform with identity animation returns offset translation``
    ()
    =
    let pivot = Vector3.Zero
    let offset = Vector3(5.0f, 10.0f, 15.0f)
    let animation = Matrix.Identity
    let result = RenderMath.Rig.applyNodeTransform pivot offset animation

    Assert.AreEqual(offset.X, result.Translation.X, 0.0001f)
    Assert.AreEqual(offset.Y, result.Translation.Y, 0.0001f)
    Assert.AreEqual(offset.Z, result.Translation.Z, 0.0001f)

  [<TestMethod>]
  member _.``applyNodeTransform with pivot rotates around pivot point``() =
    let pivot = Vector3(0.0f, 1.0f, 0.0f)
    let offset = Vector3.Zero
    let rotation = Matrix.CreateRotationY(MathHelper.PiOver2)
    let result = RenderMath.Rig.applyNodeTransform pivot offset rotation

    Assert.IsTrue(
      abs(result.M11 - 0.0f) < 0.01f || abs(result.M11 - 1.0f) < 0.01f
    )


[<TestClass>]
type BillboardTests() =

  [<TestMethod>]
  member _.``getBillboardVectors returns orthogonal vectors``() =
    let view =
      Matrix.CreateLookAt(Vector3.Backward * 10.0f, Vector3.Zero, Vector3.Up)

    let struct (right, up) = RenderMath.Billboard.getVectors &view
    let dot = Vector3.Dot(right, up)
    Assert.IsTrue(abs(dot) < 0.0001f)


[<TestClass>]
type BlockMap3DTests() =

  [<TestMethod>]
  member _.``calcCenterOffset returns correct offset``() =
    let width = 10
    let depth = 10
    let ppu = 32.0f
    let cellSize = Pomo.Core.Domain.Core.Constants.BlockMap.CellSize

    let offset = RenderMath.BlockMap3D.calcCenterOffset width depth ppu

    let expectedX = -float32 width * (cellSize / ppu) * 0.5f
    let expectedZ = -float32 depth * (cellSize / ppu) * 0.5f

    Assert.AreEqual(expectedX, offset.X, 0.001f)
    Assert.AreEqual(0.0f, offset.Y, 0.001f)
    Assert.AreEqual(expectedZ, offset.Z, 0.001f)

  [<TestMethod>]
  member _.``toRender scales position by PPU``() =
    let pos = { X = 32.0f; Y = 16.0f; Z = 64.0f }
    let ppu = 32.0f
    let centerOffset = Vector3.Zero

    let result = RenderMath.BlockMap3D.toRender pos ppu centerOffset

    Assert.AreEqual(1.0f, result.X, 0.001f)
    Assert.AreEqual(0.5f, result.Y, 0.001f)
    Assert.AreEqual(2.0f, result.Z, 0.001f)

  [<TestMethod>]
  member _.``toRender applies center offset``() =
    let pos = WorldPosition.zero
    let ppu = 32.0f
    let centerOffset = Vector3(10.0f, 20.0f, 30.0f)

    let result = RenderMath.BlockMap3D.toRender pos ppu centerOffset

    Assert.AreEqual(centerOffset.X, result.X, 0.001f)
    Assert.AreEqual(centerOffset.Y, result.Y, 0.001f)
    Assert.AreEqual(centerOffset.Z, result.Z, 0.001f)

  [<TestMethod>]
  member _.``cellToRender positions cell at center``() =
    let cellX, cellY, cellZ = 0, 0, 0
    let ppu = 32.0f
    let centerOffset = Vector3.Zero
    let cellSize = Pomo.Core.Domain.Core.Constants.BlockMap.CellSize

    let result =
      RenderMath.BlockMap3D.cellToRender cellX cellY cellZ ppu centerOffset

    let scaleFactor = cellSize / ppu
    let halfCell = scaleFactor * 0.5f

    Assert.AreEqual(halfCell, result.X, 0.001f)
    Assert.AreEqual(halfCell, result.Y, 0.001f)
    Assert.AreEqual(halfCell, result.Z, 0.001f)

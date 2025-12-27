module RenderMathTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Microsoft.Xna.Framework
open Pomo.Core.Graphics

/// Standard isometric PPU used throughout the game
let isoPpu = Vector2(32.0f, 16.0f)


module LogicToRender =

  [<Fact>]
  let ``origin maps to origin``() =
    let result = RenderMath.LogicRender.toRender Vector2.Zero 0.0f isoPpu
    Assert.Equal(Vector3.Zero, result)

  [<Fact>]
  let ``X is scaled by PPU.X``() =
    let logicPos = Vector2(64.0f, 0.0f)
    let result = RenderMath.LogicRender.toRender logicPos 0.0f isoPpu
    Assert.Equal(2.0f, result.X) // 64 / 32 = 2

  [<Fact>]
  let ``Z is scaled by PPU.Y``() =
    let logicPos = Vector2(0.0f, 32.0f)
    let result = RenderMath.LogicRender.toRender logicPos 0.0f isoPpu
    Assert.Equal(2.0f, result.Z) // 32 / 16 = 2

  [<Fact>]
  let ``Y includes altitude and depth bias``() =
    let logicPos = Vector2(0.0f, 16.0f) // zBase = 1.0
    let altitude = 5.0f
    let result = RenderMath.LogicRender.toRender logicPos altitude isoPpu
    Assert.Equal(6.0f, result.Y) // altitude(5) + zBase(1) = 6

  [<Fact>]
  let ``Z is reduced by altitude for depth sorting``() =
    let logicPos = Vector2(0.0f, 16.0f) // zBase = 1.0
    let altitude = 5.0f
    let result = RenderMath.LogicRender.toRender logicPos altitude isoPpu
    Assert.Equal(-4.0f, result.Z) // zBase(1) - altitude(5) = -4

module GetSquishFactor =

  [<Fact>]
  let ``isometric PPU gives 2x squish``() =
    let squish = RenderMath.WorldMatrix.getSquishFactor isoPpu
    Assert.Equal(2.0f, squish) // 32 / 16 = 2

  [<Fact>]
  let ``square PPU gives 1x squish``() =
    let squish = RenderMath.WorldMatrix.getSquishFactor(Vector2(32.0f, 32.0f))
    Assert.Equal(1.0f, squish)

module ScreenToLogic =

  [<Fact>]
  let ``screen center maps to camera position``() =
    let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
    let screenCenter = Vector2(400.0f, 300.0f)
    let cameraPos = Vector2(100.0f, 200.0f)

    let result =
      RenderMath.ScreenLogic.toLogic screenCenter viewport 1.0f cameraPos

    Assert.Equal(cameraPos.X, result.X, 0.001f)
    Assert.Equal(cameraPos.Y, result.Y, 0.001f)

  [<Fact>]
  let ``offset from center is scaled by zoom``() =
    let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
    let screenPos = Vector2(500.0f, 300.0f) // 100 pixels right of center
    let cameraPos = Vector2.Zero
    let zoom = 2.0f

    let result =
      RenderMath.ScreenLogic.toLogic screenPos viewport zoom cameraPos

    Assert.Equal(50.0f, result.X, 0.001f) // 100 / 2 = 50

  [<Fact>]
  let ``round-trip logic to screen to logic``() =
    let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
    let cameraPos = Vector2(1000.0f, 500.0f)
    let zoom = 2.5f
    let logicPos = Vector2(1100.0f, 400.0f)

    let screenPos =
      RenderMath.ScreenLogic.toScreen logicPos viewport zoom cameraPos

    let actualLogicPos =
      RenderMath.ScreenLogic.toLogic screenPos viewport zoom cameraPos

    Assert.Equal(logicPos.X, actualLogicPos.X, 0.001f)
    Assert.Equal(logicPos.Y, actualLogicPos.Y, 0.001f)


module Properties =

  /// Helper to clamp values to reasonable ranges for PPU (avoid division by zero)
  let inline clampPpu(x: int) = max 1 (abs x % 128 + 1)

  [<Property>]
  let ``LogicToRender X component is always logicX / ppu.X``
    (lx: int)
    (ly: int)
    (px: int)
    (py: int)
    =
    let logicPos = Vector2(float32 lx, float32 ly)
    let ppu = Vector2(float32(clampPpu px), float32(clampPpu py))
    let result = RenderMath.LogicRender.toRender logicPos 0.0f ppu
    abs(result.X - logicPos.X / ppu.X) < 0.0001f

  [<Property>]
  let ``LogicToRender Z at zero altitude equals logicY / ppu.Y``
    (lx: int)
    (ly: int)
    (px: int)
    (py: int)
    =
    let logicPos = Vector2(float32 lx, float32 ly)
    let ppu = Vector2(float32(clampPpu px), float32(clampPpu py))
    let result = RenderMath.LogicRender.toRender logicPos 0.0f ppu
    abs(result.Z - logicPos.Y / ppu.Y) < 0.0001f

  [<Property>]
  let ``altitude directly contributes to Y``
    (lx: int)
    (ly: int)
    (px: int)
    (py: int)
    =
    let logicPos = Vector2(float32 lx, float32 ly)
    let ppu = Vector2(float32(clampPpu px), float32(clampPpu py))
    let altA = 0.0f
    let altB = 10.0f
    let resultA = RenderMath.LogicRender.toRender logicPos altA ppu
    let resultB = RenderMath.LogicRender.toRender logicPos altB ppu
    abs(resultB.Y - resultA.Y - 10.0f) < 0.0001f

  [<Property>]
  let ``altitude inversely contributes to Z``
    (lx: int)
    (ly: int)
    (px: int)
    (py: int)
    =
    let logicPos = Vector2(float32 lx, float32 ly)
    let ppu = Vector2(float32(clampPpu px), float32(clampPpu py))
    let altA = 0.0f
    let altB = 10.0f
    let resultA = RenderMath.LogicRender.toRender logicPos altA ppu
    let resultB = RenderMath.LogicRender.toRender logicPos altB ppu
    // Higher altitude = lower Z (sorts behind)
    abs(resultA.Z - resultB.Z - 10.0f) < 0.0001f

  [<Property>]
  let ``GetSquishFactor is always ppu.X / ppu.Y`` (px: int) (py: int) =
    let ppu = Vector2(float32(clampPpu px), float32(clampPpu py))
    let expected = ppu.X / ppu.Y
    let actual = RenderMath.WorldMatrix.getSquishFactor ppu
    abs(actual - expected) < 0.0001f

  [<Property>]
  let ``ParticleSpace at ground equals LogicRender at zero altitude``
    (x: int)
    (z: int)
    =
    let ppu = Vector2(32.0f, 16.0f)
    let particle = Vector3(float32 x, 0.0f, float32 z)
    let logic = Vector2(float32 x, float32 z)
    let fromParticle = RenderMath.ParticleSpace.toRender particle ppu
    let fromLogic = RenderMath.LogicRender.toRender logic 0.0f ppu

    abs(fromParticle.X - fromLogic.X) < 0.0001f
    && abs(fromParticle.Y - fromLogic.Y) < 0.0001f
    && abs(fromParticle.Z - fromLogic.Z) < 0.0001f

  [<Property>]
  let ``tileToRender X and Z match toRender X and Z`` (lx: int) (ly: int) =
    let ppu = Vector2(32.0f, 16.0f)
    let logicPos = Vector2(float32 lx, float32 ly)
    let depthY = 5.0f
    let tileResult = RenderMath.LogicRender.tileToRender logicPos depthY ppu
    let renderResult = RenderMath.LogicRender.toRender logicPos 0.0f ppu
    // X and Z should match (only Y differs)
    abs(tileResult.X - renderResult.X) < 0.0001f
    && abs(tileResult.Z - renderResult.Z) < 0.0001f
    && abs(tileResult.Y - depthY) < 0.0001f

  [<Property>]
  let ``createMesh translation equals render position``
    (x: int)
    (y: int)
    (z: int)
    =
    let renderPos = Vector3(float32 x, float32 y, float32 z)
    let world = RenderMath.WorldMatrix.createMesh renderPos 0.0f 1.0f 2.0f
    let translation = world.Translation

    abs(translation.X - renderPos.X) < 0.0001f
    && abs(translation.Y - renderPos.Y) < 0.0001f
    && abs(translation.Z - renderPos.Z) < 0.0001f

  [<Property>]
  let ``createProjectile translation equals render position``
    (x: int)
    (y: int)
    (z: int)
    =
    let renderPos = Vector3(float32 x, float32 y, float32 z)

    let world =
      RenderMath.WorldMatrix.createProjectile renderPos 0.0f 0.0f 1.0f 2.0f

    let translation = world.Translation

    abs(translation.X - renderPos.X) < 0.0001f
    && abs(translation.Y - renderPos.Y) < 0.0001f
    && abs(translation.Z - renderPos.Z) < 0.0001f

  [<Property>]
  let ``createMeshParticle translation includes pivot and position``
    (x: int)
    (y: int)
    (z: int)
    =
    let renderPos = Vector3(float32 x, float32 y, float32 z)
    let pivot = Vector3(1.0f, 2.0f, 3.0f)

    let world =
      RenderMath.WorldMatrix.createMeshParticle
        renderPos
        Quaternion.Identity
        1.0f
        Vector3.One
        pivot
        2.0f

    let translation = world.Translation
    // Translation should be renderPos + pivot (before other transforms)
    abs(translation.X - (renderPos.X + pivot.X)) < 0.1f
    && abs(translation.Y - (renderPos.Y + pivot.Y)) < 0.1f
    && abs(translation.Z - (renderPos.Z + pivot.Z)) < 0.1f

  [<Property>]
  let ``applyNodeTransform with identity animation returns offset translation``
    (ox: int)
    (oy: int)
    (oz: int)
    =
    let pivot = Vector3.Zero
    let offset = Vector3(float32 ox, float32 oy, float32 oz)
    let animation = Matrix.Identity
    let result = RenderMath.Rig.applyNodeTransform pivot offset animation

    abs(result.Translation.X - offset.X) < 0.0001f
    && abs(result.Translation.Y - offset.Y) < 0.0001f
    && abs(result.Translation.Z - offset.Z) < 0.0001f

  [<Property>]
  let ``getBillboardVectors returns orthogonal vectors``() =
    let ppu = Vector2(32.0f, 16.0f)
    let view = RenderMath.Camera.getViewMatrix Vector2.Zero ppu
    let struct (right, up) = RenderMath.Billboard.getVectors &view
    // Right and Up should be orthogonal (dot product = 0)
    let dot = Vector3.Dot(right, up)
    abs(dot) < 0.0001f

  [<Property>]
  let ``getViewMatrix produces invertible matrix`` (x: int) (y: int) =
    let ppu = Vector2(32.0f, 16.0f)
    let pos = Vector2(float32 x, float32 y)
    let view = RenderMath.Camera.getViewMatrix pos ppu
    let det = view.Determinant()
    // Invertible matrix has non-zero determinant
    abs(det) > 0.0001f

  [<Property>]
  let ``getProjectionMatrix produces well-formed matrix``(zoom: int) =
    let zoom = float32(abs zoom % 10 + 1)
    let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
    let ppu = Vector2(32.0f, 16.0f)
    let proj = RenderMath.Camera.getProjectionMatrix viewport zoom ppu
    // Orthographic matrices are not invertible, but should be well-formed
    not(System.Single.IsNaN proj.M11) && not(System.Single.IsInfinity proj.M11)

  [<Property>]
  let ``get2DViewMatrix centers camera position on screen center``
    (cx: int)
    (cy: int)
    =
    let zoom = 1.0f
    let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
    let cameraPos = Vector2(float32 cx, float32 cy)
    let matrix = RenderMath.Camera.get2DViewMatrix cameraPos zoom viewport
    // Camera position should transform to screen center (400, 300)
    let transformed = Vector2.Transform(cameraPos, matrix)

    abs(transformed.X - 400.0f) < 0.0001f
    && abs(transformed.Y - 300.0f) < 0.0001f


module GetViewBounds =

  [<Fact>]
  let ``centered camera has symmetric bounds``() =
    let cameraPos = Vector2(0.0f, 0.0f)

    let struct (left, right, top, bottom) =
      RenderMath.Camera.getViewBounds cameraPos 800.0f 600.0f 1.0f

    Assert.Equal(-400.0f, left)
    Assert.Equal(400.0f, right)
    Assert.Equal(-300.0f, top)
    Assert.Equal(300.0f, bottom)

  [<Fact>]
  let ``zoom shrinks visible area``() =
    let cameraPos = Vector2(0.0f, 0.0f)

    let struct (left, right, top, bottom) =
      RenderMath.Camera.getViewBounds cameraPos 800.0f 600.0f 2.0f
    // 800 / (2 * 2) = 200 half-width
    Assert.Equal(-200.0f, left)
    Assert.Equal(200.0f, right)

  [<Fact>]
  let ``camera position offsets bounds``() =
    let cameraPos = Vector2(100.0f, 50.0f)

    let struct (left, right, top, bottom) =
      RenderMath.Camera.getViewBounds cameraPos 800.0f 600.0f 1.0f

    Assert.Equal(-300.0f, left) // 100 - 400
    Assert.Equal(500.0f, right) // 100 + 400
    Assert.Equal(-250.0f, top) // 50 - 300
    Assert.Equal(350.0f, bottom) // 50 + 300


module TileGridToLogic =

  open Pomo.Core.Domain.Map

  [<Fact>]
  let ``orthogonal layout is simple grid``() =
    let struct (px, py) =
      RenderMath.TileGrid.toLogic
        Orthogonal
        ValueNone
        ValueNone
        10
        2
        3
        32.0f
        32.0f

    Assert.Equal(64.0f, px) // 2 * 32
    Assert.Equal(96.0f, py) // 3 * 32

  [<Fact>]
  let ``isometric layout applies diamond projection``() =
    let mapWidth = 10
    let tileW, tileH = 64.0f, 32.0f

    let struct (px, py) =
      RenderMath.TileGrid.toLogic
        Isometric
        ValueNone
        ValueNone
        mapWidth
        0
        0
        tileW
        tileH
    // Origin at center: mapWidth * tileW / 2 = 320
    Assert.Equal(320.0f, px)
    Assert.Equal(0.0f, py)

  [<Fact>]
  let ``isometric x+1 moves right and down``() =
    let mapWidth = 10
    let tileW, tileH = 64.0f, 32.0f

    let struct (px0, py0) =
      RenderMath.TileGrid.toLogic
        Isometric
        ValueNone
        ValueNone
        mapWidth
        0
        0
        tileW
        tileH

    let struct (px1, py1) =
      RenderMath.TileGrid.toLogic
        Isometric
        ValueNone
        ValueNone
        mapWidth
        1
        0
        tileW
        tileH
    // x+1: px increases by tileW/2, py increases by tileH/2
    Assert.Equal(px0 + 32.0f, px1)
    Assert.Equal(py0 + 16.0f, py1)

  [<Fact>]
  let ``staggered X axis odd index offsets Y``() =
    let struct (px0, py0) =
      RenderMath.TileGrid.toLogic
        Staggered
        (ValueSome X)
        (ValueSome Odd)
        10
        0
        0
        64.0f
        32.0f

    let struct (px1, py1) =
      RenderMath.TileGrid.toLogic
        Staggered
        (ValueSome X)
        (ValueSome Odd)
        10
        1
        0
        64.0f
        32.0f
    // x=1 is odd, should have Y offset of tileH/2
    Assert.Equal(0.0f, py0)
    Assert.Equal(16.0f, py1) // tileH / 2

  [<Fact>]
  let ``staggered Y axis odd index offsets X``() =
    let struct (px0, py0) =
      RenderMath.TileGrid.toLogic
        Staggered
        (ValueSome Y)
        (ValueSome Odd)
        10
        0
        0
        64.0f
        32.0f

    let struct (px1, py1) =
      RenderMath.TileGrid.toLogic
        Staggered
        (ValueSome Y)
        (ValueSome Odd)
        10
        0
        1
        64.0f
        32.0f
    // y=1 is odd, should have X offset of tileW/2
    Assert.Equal(0.0f, px0)
    Assert.Equal(32.0f, px1) // tileW / 2

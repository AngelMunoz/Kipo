module RenderMathTests

open Microsoft.VisualStudio.TestTools.UnitTesting
open Microsoft.Xna.Framework
open Pomo.Core.Graphics
open Pomo.Core.Domain.Map
open FsCheck
open FsCheck.FSharp

/// Standard isometric PPU used throughout the game
let isoPpu = Vector2(32.0f, 16.0f)

/// Helper to clamp values to reasonable ranges for PPU (avoid division by zero)
let inline clampPpu(x: int) = max 1 (abs x % 128 + 1)

/// Arbitrary for int values
let intArb = Arb.fromGen(Gen.choose(-10000, 10000))

/// Arbitrary for 4 int values (for PPU tests)
let fourInts =
  Arb.zip(Arb.zip(intArb, intArb), Arb.zip(intArb, intArb))
  |> Arb.convert (fun ((a, b), (c, d)) -> (a, b, c, d)) (fun (a, b, c, d) ->
    ((a, b), (c, d)))

/// Arbitrary for 3 int values
let threeInts =
  Arb.zip(intArb, Arb.zip(intArb, intArb))
  |> Arb.convert (fun (a, (b, c)) -> (a, b, c)) (fun (a, b, c) -> (a, (b, c)))

/// Arbitrary for 2 int values
let twoInts = Arb.zip(intArb, intArb)


[<TestClass>]
type LogicToRenderTests() =

  [<TestMethod>]
  member _.``origin maps to origin``() =
    let result = RenderMath.LogicRender.toRender Vector2.Zero 0.0f isoPpu
    Assert.AreEqual(Vector3.Zero, result)

  [<TestMethod>]
  member _.``X is scaled by PPU.X``() =
    let logicPos = Vector2(64.0f, 0.0f)
    let result = RenderMath.LogicRender.toRender logicPos 0.0f isoPpu
    Assert.AreEqual(2.0f, result.X) // 64 / 32 = 2

  [<TestMethod>]
  member _.``Z is scaled by PPU.Y``() =
    let logicPos = Vector2(0.0f, 32.0f)
    let result = RenderMath.LogicRender.toRender logicPos 0.0f isoPpu
    Assert.AreEqual(2.0f, result.Z) // 32 / 16 = 2

  [<TestMethod>]
  member _.``Y includes altitude and depth bias``() =
    let logicPos = Vector2(0.0f, 16.0f) // zBase = 1.0
    let altitude = 5.0f
    let result = RenderMath.LogicRender.toRender logicPos altitude isoPpu
    Assert.AreEqual(6.0f, result.Y) // altitude(5) + zBase(1) = 6

  [<TestMethod>]
  member _.``Z is reduced by altitude for depth sorting``() =
    let logicPos = Vector2(0.0f, 16.0f) // zBase = 1.0
    let altitude = 5.0f
    let result = RenderMath.LogicRender.toRender logicPos altitude isoPpu
    Assert.AreEqual(-4.0f, result.Z) // zBase(1) - altitude(5) = -4


[<TestClass>]
type GetSquishFactorTests() =

  [<TestMethod>]
  member _.``isometric PPU gives 2x squish``() =
    let squish = RenderMath.WorldMatrix.getSquishFactor isoPpu
    Assert.AreEqual(2.0f, squish) // 32 / 16 = 2

  [<TestMethod>]
  member _.``square PPU gives 1x squish``() =
    let squish = RenderMath.WorldMatrix.getSquishFactor(Vector2(32.0f, 32.0f))
    Assert.AreEqual(1.0f, squish)


[<TestClass>]
type ScreenToLogicTests() =

  [<TestMethod>]
  member _.``screen center maps to camera position``() =
    let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
    let screenCenter = Vector2(400.0f, 300.0f)
    let cameraPos = Vector2(100.0f, 200.0f)

    let result =
      RenderMath.ScreenLogic.toLogic screenCenter viewport 1.0f cameraPos

    Assert.AreEqual(cameraPos.X, result.X, 0.001f)
    Assert.AreEqual(cameraPos.Y, result.Y, 0.001f)

  [<TestMethod>]
  member _.``offset from center is scaled by zoom``() =
    let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
    let screenPos = Vector2(500.0f, 300.0f) // 100 pixels right of center
    let cameraPos = Vector2.Zero
    let zoom = 2.0f

    let result =
      RenderMath.ScreenLogic.toLogic screenPos viewport zoom cameraPos

    Assert.AreEqual(50.0f, result.X, 0.001f) // 100 / 2 = 50

  [<TestMethod>]
  member _.``round-trip logic to screen to logic``() =
    let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
    let cameraPos = Vector2(1000.0f, 500.0f)
    let zoom = 2.5f
    let logicPos = Vector2(1100.0f, 400.0f)

    let screenPos =
      RenderMath.ScreenLogic.toScreen logicPos viewport zoom cameraPos

    let actualLogicPos =
      RenderMath.ScreenLogic.toLogic screenPos viewport zoom cameraPos

    Assert.AreEqual(logicPos.X, actualLogicPos.X, 0.001f)
    Assert.AreEqual(logicPos.Y, actualLogicPos.Y, 0.001f)


[<TestClass>]
type PropertyTests() =

  [<TestMethod>]
  member _.``LogicToRender X component is always logicX div ppu.X``() =
    Prop.forAll fourInts (fun (lx, ly, px, py) ->
      let logicPos = Vector2(float32 lx, float32 ly)
      let ppu = Vector2(float32(clampPpu px), float32(clampPpu py))
      let result = RenderMath.LogicRender.toRender logicPos 0.0f ppu
      abs(result.X - logicPos.X / ppu.X) < 0.0001f)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``LogicToRender Z at zero altitude equals logicY div ppu.Y``() =
    Prop.forAll fourInts (fun (lx, ly, px, py) ->
      let logicPos = Vector2(float32 lx, float32 ly)
      let ppu = Vector2(float32(clampPpu px), float32(clampPpu py))
      let result = RenderMath.LogicRender.toRender logicPos 0.0f ppu
      abs(result.Z - logicPos.Y / ppu.Y) < 0.0001f)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``altitude directly contributes to Y``() =
    Prop.forAll fourInts (fun (lx, ly, px, py) ->
      let logicPos = Vector2(float32 lx, float32 ly)
      let ppu = Vector2(float32(clampPpu px), float32(clampPpu py))
      let altA = 0.0f
      let altB = 10.0f
      let resultA = RenderMath.LogicRender.toRender logicPos altA ppu
      let resultB = RenderMath.LogicRender.toRender logicPos altB ppu
      abs(resultB.Y - resultA.Y - 10.0f) < 0.0001f)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``altitude inversely contributes to Z``() =
    Prop.forAll fourInts (fun (lx, ly, px, py) ->
      let logicPos = Vector2(float32 lx, float32 ly)
      let ppu = Vector2(float32(clampPpu px), float32(clampPpu py))
      let altA = 0.0f
      let altB = 10.0f
      let resultA = RenderMath.LogicRender.toRender logicPos altA ppu
      let resultB = RenderMath.LogicRender.toRender logicPos altB ppu
      abs(resultA.Z - resultB.Z - 10.0f) < 0.0001f)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``GetSquishFactor is always ppu.X div ppu.Y``() =
    Prop.forAll twoInts (fun (px, py) ->
      let ppu = Vector2(float32(clampPpu px), float32(clampPpu py))
      let expected = ppu.X / ppu.Y
      let actual = RenderMath.WorldMatrix.getSquishFactor ppu
      abs(actual - expected) < 0.0001f)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``ParticleSpace at ground equals LogicRender at zero altitude``() =
    Prop.forAll twoInts (fun (x, z) ->
      let ppu = Vector2(32.0f, 16.0f)
      let particle = Vector3(float32 x, 0.0f, float32 z)
      let logic = Vector2(float32 x, float32 z)
      let fromParticle = RenderMath.ParticleSpace.toRender particle ppu
      let fromLogic = RenderMath.LogicRender.toRender logic 0.0f ppu

      abs(fromParticle.X - fromLogic.X) < 0.0001f
      && abs(fromParticle.Y - fromLogic.Y) < 0.0001f
      && abs(fromParticle.Z - fromLogic.Z) < 0.0001f)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``tileToRender X and Z match toRender X and Z``() =
    Prop.forAll twoInts (fun (lx, ly) ->
      let ppu = Vector2(32.0f, 16.0f)
      let logicPos = Vector2(float32 lx, float32 ly)
      let depthY = 5.0f
      let tileResult = RenderMath.LogicRender.tileToRender logicPos depthY ppu
      let renderResult = RenderMath.LogicRender.toRender logicPos 0.0f ppu

      abs(tileResult.X - renderResult.X) < 0.0001f
      && abs(tileResult.Z - renderResult.Z) < 0.0001f
      && abs(tileResult.Y - depthY) < 0.0001f)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``createMesh translation equals render position``() =
    Prop.forAll threeInts (fun (x, y, z) ->
      let renderPos = Vector3(float32 x, float32 y, float32 z)
      let world = RenderMath.WorldMatrix.createMesh renderPos 0.0f 1.0f 2.0f
      let translation = world.Translation

      abs(translation.X - renderPos.X) < 0.0001f
      && abs(translation.Y - renderPos.Y) < 0.0001f
      && abs(translation.Z - renderPos.Z) < 0.0001f)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``createProjectile translation equals render position``() =
    Prop.forAll threeInts (fun (x, y, z) ->
      let renderPos = Vector3(float32 x, float32 y, float32 z)

      let world =
        RenderMath.WorldMatrix.createProjectile renderPos 0.0f 0.0f 1.0f 2.0f

      let translation = world.Translation

      abs(translation.X - renderPos.X) < 0.0001f
      && abs(translation.Y - renderPos.Y) < 0.0001f
      && abs(translation.Z - renderPos.Z) < 0.0001f)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``createMeshParticle translation includes pivot and position``() =
    Prop.forAll threeInts (fun (x, y, z) ->
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

      abs(translation.X - (renderPos.X + pivot.X)) < 0.1f
      && abs(translation.Y - (renderPos.Y + pivot.Y)) < 0.1f
      && abs(translation.Z - (renderPos.Z + pivot.Z)) < 0.1f)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``applyNodeTransform with identity animation returns offset translation``
    ()
    =
    Prop.forAll threeInts (fun (ox, oy, oz) ->
      let pivot = Vector3.Zero
      let offset = Vector3(float32 ox, float32 oy, float32 oz)
      let animation = Matrix.Identity
      let result = RenderMath.Rig.applyNodeTransform pivot offset animation

      abs(result.Translation.X - offset.X) < 0.0001f
      && abs(result.Translation.Y - offset.Y) < 0.0001f
      && abs(result.Translation.Z - offset.Z) < 0.0001f)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``getBillboardVectors returns orthogonal vectors``() =
    let ppu = Vector2(32.0f, 16.0f)
    let view = RenderMath.Camera.getViewMatrix Vector2.Zero ppu
    let struct (right, up) = RenderMath.Billboard.getVectors &view
    let dot = Vector3.Dot(right, up)
    Assert.IsTrue(abs(dot) < 0.0001f)

  [<TestMethod>]
  member _.``getViewMatrix produces invertible matrix``() =
    Prop.forAll twoInts (fun (x, y) ->
      let ppu = Vector2(32.0f, 16.0f)
      let pos = Vector2(float32 x, float32 y)
      let view = RenderMath.Camera.getViewMatrix pos ppu
      let det = view.Determinant()
      abs(det) > 0.0001f)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``getProjectionMatrix produces well-formed matrix``() =
    Prop.forAll intArb (fun zoom ->
      let zoom = float32(abs zoom % 10 + 1)
      let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
      let ppu = Vector2(32.0f, 16.0f)
      let proj = RenderMath.Camera.getProjectionMatrix viewport zoom ppu

      not(System.Single.IsNaN proj.M11)
      && not(System.Single.IsInfinity proj.M11))
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``get2DViewMatrix centers camera position on screen center``() =
    Prop.forAll twoInts (fun (cx, cy) ->
      let zoom = 1.0f
      let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
      let cameraPos = Vector2(float32 cx, float32 cy)
      let matrix = RenderMath.Camera.get2DViewMatrix cameraPos zoom viewport
      let transformed = Vector2.Transform(cameraPos, matrix)

      abs(transformed.X - 400.0f) < 0.0001f
      && abs(transformed.Y - 300.0f) < 0.0001f)
    |> Check.QuickThrowOnFailure


[<TestClass>]
type GetViewBoundsTests() =

  [<TestMethod>]
  member _.``centered camera has symmetric bounds``() =
    let cameraPos = Vector2(0.0f, 0.0f)

    let struct (left, right, top, bottom) =
      RenderMath.Camera.getViewBounds cameraPos 800.0f 600.0f 1.0f

    Assert.AreEqual(-400.0f, left)
    Assert.AreEqual(400.0f, right)
    Assert.AreEqual(-300.0f, top)
    Assert.AreEqual(300.0f, bottom)

  [<TestMethod>]
  member _.``zoom shrinks visible area``() =
    let cameraPos = Vector2(0.0f, 0.0f)

    let struct (left, right, _, _) =
      RenderMath.Camera.getViewBounds cameraPos 800.0f 600.0f 2.0f

    Assert.AreEqual(-200.0f, left)
    Assert.AreEqual(200.0f, right)

  [<TestMethod>]
  member _.``camera position offsets bounds``() =
    let cameraPos = Vector2(100.0f, 50.0f)

    let struct (left, right, top, bottom) =
      RenderMath.Camera.getViewBounds cameraPos 800.0f 600.0f 1.0f

    Assert.AreEqual(-300.0f, left)
    Assert.AreEqual(500.0f, right)
    Assert.AreEqual(-250.0f, top)
    Assert.AreEqual(350.0f, bottom)


[<TestClass>]
type TileGridToLogicTests() =

  [<TestMethod>]
  member _.``orthogonal layout is simple grid``() =
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

    Assert.AreEqual(64.0f, px)
    Assert.AreEqual(96.0f, py)

  [<TestMethod>]
  member _.``isometric layout applies diamond projection``() =
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

    Assert.AreEqual(320.0f, px)
    Assert.AreEqual(0.0f, py)

  [<TestMethod>]
  member _.``isometric x+1 moves right and down``() =
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

    Assert.AreEqual(px0 + 32.0f, px1)
    Assert.AreEqual(py0 + 16.0f, py1)

  [<TestMethod>]
  member _.``staggered X axis odd index offsets Y``() =
    let struct (_, py0) =
      RenderMath.TileGrid.toLogic
        Staggered
        (ValueSome X)
        (ValueSome Odd)
        10
        0
        0
        64.0f
        32.0f

    let struct (_, py1) =
      RenderMath.TileGrid.toLogic
        Staggered
        (ValueSome X)
        (ValueSome Odd)
        10
        1
        0
        64.0f
        32.0f

    Assert.AreEqual(0.0f, py0)
    Assert.AreEqual(16.0f, py1)

  [<TestMethod>]
  member _.``staggered Y axis odd index offsets X``() =
    let struct (px0, _) =
      RenderMath.TileGrid.toLogic
        Staggered
        (ValueSome Y)
        (ValueSome Odd)
        10
        0
        0
        64.0f
        32.0f

    let struct (px1, _) =
      RenderMath.TileGrid.toLogic
        Staggered
        (ValueSome Y)
        (ValueSome Odd)
        10
        0
        1
        64.0f
        32.0f

    Assert.AreEqual(0.0f, px0)
    Assert.AreEqual(32.0f, px1)

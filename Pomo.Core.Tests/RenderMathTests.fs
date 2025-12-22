module RenderMathTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Microsoft.Xna.Framework
open Pomo.Core.Graphics

/// Standard isometric PPU used throughout the game
let isoPpu = Vector2(32.0f, 16.0f)

// ============================================================================
// Unit Tests
// ============================================================================

module LogicToRender =

  [<Fact>]
  let ``origin maps to origin``() =
    let result = RenderMath.LogicToRender Vector2.Zero 0.0f isoPpu
    Assert.Equal(Vector3.Zero, result)

  [<Fact>]
  let ``X is scaled by PPU.X``() =
    let logicPos = Vector2(64.0f, 0.0f)
    let result = RenderMath.LogicToRender logicPos 0.0f isoPpu
    Assert.Equal(2.0f, result.X) // 64 / 32 = 2

  [<Fact>]
  let ``Z is scaled by PPU.Y``() =
    let logicPos = Vector2(0.0f, 32.0f)
    let result = RenderMath.LogicToRender logicPos 0.0f isoPpu
    Assert.Equal(2.0f, result.Z) // 32 / 16 = 2

  [<Fact>]
  let ``Y includes altitude and depth bias``() =
    let logicPos = Vector2(0.0f, 16.0f) // Z = 1.0
    let altitude = 5.0f
    let result = RenderMath.LogicToRender logicPos altitude isoPpu
    Assert.Equal(6.0f, result.Y) // altitude(5) + Z(1) = 6

module GetSquishFactor =

  [<Fact>]
  let ``isometric PPU gives 2x squish``() =
    let squish = RenderMath.GetSquishFactor isoPpu
    Assert.Equal(2.0f, squish) // 32 / 16 = 2

  [<Fact>]
  let ``square PPU gives 1x squish``() =
    let squish = RenderMath.GetSquishFactor(Vector2(32.0f, 32.0f))
    Assert.Equal(1.0f, squish)

module ScreenToLogic =

  [<Fact>]
  let ``screen center maps to camera position``() =
    let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
    let screenCenter = Vector2(400.0f, 300.0f)
    let cameraPos = Vector2(100.0f, 200.0f)
    let result = RenderMath.ScreenToLogic screenCenter viewport 1.0f cameraPos
    Assert.Equal(cameraPos.X, result.X, 0.001f)
    Assert.Equal(cameraPos.Y, result.Y, 0.001f)

  [<Fact>]
  let ``offset from center is scaled by zoom``() =
    let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
    let screenPos = Vector2(500.0f, 300.0f) // 100 pixels right of center
    let cameraPos = Vector2.Zero
    let zoom = 2.0f
    let result = RenderMath.ScreenToLogic screenPos viewport zoom cameraPos
    Assert.Equal(50.0f, result.X, 0.001f) // 100 / 2 = 50

  [<Fact>]
  let ``round-trip logic to screen to logic``() =
    let viewport = Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600)
    let cameraPos = Vector2(1000.0f, 500.0f)
    let zoom = 2.5f
    let logicPos = Vector2(1100.0f, 400.0f)

    let screenPos = RenderMath.LogicToScreen logicPos viewport zoom cameraPos

    let actualLogicPos =
      RenderMath.ScreenToLogic screenPos viewport zoom cameraPos

    Assert.Equal(logicPos.X, actualLogicPos.X, 0.001f)
    Assert.Equal(logicPos.Y, actualLogicPos.Y, 0.001f)

// ============================================================================
// Property-Based Tests
// ============================================================================

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
    let result = RenderMath.LogicToRender logicPos 0.0f ppu
    abs(result.X - logicPos.X / ppu.X) < 0.0001f

  [<Property>]
  let ``LogicToRender Z component is always logicY / ppu.Y``
    (lx: int)
    (ly: int)
    (px: int)
    (py: int)
    =
    let logicPos = Vector2(float32 lx, float32 ly)
    let ppu = Vector2(float32(clampPpu px), float32(clampPpu py))
    let result = RenderMath.LogicToRender logicPos 0.0f ppu
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
    let resultA = RenderMath.LogicToRender logicPos altA ppu
    let resultB = RenderMath.LogicToRender logicPos altB ppu
    abs(resultB.Y - resultA.Y - 10.0f) < 0.0001f

  [<Property>]
  let ``GetSquishFactor is always ppu.X / ppu.Y`` (px: int) (py: int) =
    let ppu = Vector2(float32(clampPpu px), float32(clampPpu py))
    let expected = ppu.X / ppu.Y
    let actual = RenderMath.GetSquishFactor ppu
    abs(actual - expected) < 0.0001f

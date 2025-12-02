namespace Pomo.Core.Graphics

open System
open Microsoft.Xna.Framework

module RenderMath =

  let PixelsPerUnitX = 64.0f
  let PixelsPerUnitY = 32.0f

  /// Converts a Logic/Screen position (pixels) to Unified Render Space (3D Units).
  /// X = LogicX / 64
  /// Z = LogicY / 32 (Maps Screen Y to World Z)
  /// Y = LogicY / 32 (Depth Bias based on Screen Y)
  let LogicToRender(logicPos: Vector2) : Vector3 =
    let x = logicPos.X / PixelsPerUnitX
    let z = logicPos.Y / PixelsPerUnitY
    let y = z // Depth Bias = Z
    Vector3(x, y, z)

  /// Converts a Logic/Screen position (pixels) to Unified Render Space (3D Units) with explicit depth override.
  let LogicToRenderWithDepth (logicPos: Vector2) (depthY: float32) : Vector3 =
    let x = logicPos.X / PixelsPerUnitX
    let z = logicPos.Y / PixelsPerUnitY
    Vector3(x, depthY, z)

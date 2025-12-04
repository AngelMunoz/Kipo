namespace Pomo.Core.Graphics

open System
open Microsoft.Xna.Framework

module RenderMath =

  /// Converts a Logic/Screen position (pixels) to Unified Render Space (3D Units).
  /// X = LogicX / PPU.X
  /// Z = LogicY / PPU.Y (Maps Screen Y to World Z)
  /// Y = LogicY / PPU.Y (Depth Bias based on Screen Y)
  let LogicToRender (logicPos: Vector2) (pixelsPerUnit: Vector2) : Vector3 =
    let x = logicPos.X / pixelsPerUnit.X
    let z = logicPos.Y / pixelsPerUnit.Y
    let y = z // Depth Bias = Z
    Vector3(x, y, z)

  /// Converts a Logic/Screen position (pixels) to Unified Render Space (3D Units) with explicit depth override.
  let LogicToRenderWithDepth
    (logicPos: Vector2)
    (depthY: float32)
    (pixelsPerUnit: Vector2)
    : Vector3 =
    let x = logicPos.X / pixelsPerUnit.X
    let z = logicPos.Y / pixelsPerUnit.Y
    Vector3(x, depthY, z)

  /// Calculates the 2D transform matrix for SpriteBatch.
  /// Used for Background layers and UI.
  let GetSpriteBatchTransform
    (cameraPos: Vector2)
    (zoom: float32)
    (viewportWidth: int)
    (viewportHeight: int)
    : Matrix =
    Matrix.CreateTranslation(-cameraPos.X, -cameraPos.Y, 0.0f)
    * Matrix.CreateScale(zoom, zoom, 1.0f)
    * Matrix.CreateTranslation(
      float32 viewportWidth / 2.0f,
      float32 viewportHeight / 2.0f,
      0.0f
    )

  // Pre-calculated matrices for 3D entity rendering
  let private isoRot =
    Matrix.CreateLookAt(
      Vector3.Zero,
      Vector3.Normalize(Vector3(-1.0f, -1.0f, -1.0f)),
      Vector3.Up
    )

  let private topDownRot =
    Matrix.CreateLookAt(Vector3.Zero, Vector3.Down, Vector3.Forward)

  let private correction = isoRot * Matrix.Invert topDownRot

  /// Calculates the World Matrix for a 3D entity.
  /// Applies Isometric Rotation correction and Squish compensation.
  /// squishFactor is typically PixelsPerUnit.X / PixelsPerUnit.Y (e.g., 64/32 = 2.0)
  let GetEntityWorldMatrix
    (renderPos: Vector3)
    (rotationY: float32)
    (rotationOffset: float32)
    (squishFactor: float32)
    (modelScale: float32)
    : Matrix =
    let squishCompensation = Matrix.CreateScale(1.0f, 1.0f, squishFactor)
    let scale = Matrix.CreateScale(modelScale)

    Matrix.CreateRotationY(rotationY + rotationOffset)
    * correction
    * squishCompensation
    * scale
    * Matrix.CreateTranslation(renderPos)

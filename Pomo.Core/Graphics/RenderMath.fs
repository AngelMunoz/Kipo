namespace Pomo.Core.Graphics

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

module RenderMath =

  /// Converts a Logic position (pixels) and altitude to Unified Render Space (3D Units).
  /// X = LogicX / PPU.X
  /// Z = LogicY / PPU.Y (Maps Screen Y to World Z)
  /// Y = Altitude + Z (Depth Bias = Z)
  let LogicToRender
    (logicPos: Vector2)
    (altitude: float32)
    (pixelsPerUnit: Vector2)
    : Vector3 =
    let x = logicPos.X / pixelsPerUnit.X
    let z = logicPos.Y / pixelsPerUnit.Y
    let y = altitude + z
    Vector3(x, y, z)

  /// Calculates the Squish Factor used for isometric correction.
  /// defined as PPU.X / PPU.Y
  let GetSquishFactor(pixelsPerUnit: Vector2) : float32 =
    pixelsPerUnit.X / pixelsPerUnit.Y

  /// Calculates the View Matrix for the camera.
  /// Logic Position is the center of the camera in pixels.
  let GetViewMatrix (logicPos: Vector2) (pixelsPerUnit: Vector2) : Matrix =
    let target =
      Vector3(logicPos.X / pixelsPerUnit.X, 0.0f, logicPos.Y / pixelsPerUnit.Y)
    // Look straight down from above
    let cameraPos = target + Vector3.Up * 100.0f
    // Up vector is Forward (0, 0, -1) so that Z maps to screen Y (down)
    Matrix.CreateLookAt(cameraPos, target, Vector3.Forward)

  /// Calculates the Orthographic Projection Matrix.
  let GetProjectionMatrix
    (viewport: Viewport)
    (zoom: float32)
    (pixelsPerUnit: Vector2)
    : Matrix =
    // Orthographic Projection respecting zoom and unit scale
    // We want 1 unit in 3D to correspond to pixelsPerUnit * Zoom pixels on screen
    let viewWidth = float32 viewport.Width / (zoom * pixelsPerUnit.X)
    let viewHeight = float32 viewport.Height / (zoom * pixelsPerUnit.Y)
    Matrix.CreateOrthographic(viewWidth, viewHeight, 0.1f, 5000.0f)

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

  /// Calculates the World Matrix for a 3D entity (Mesh).
  let CreateMeshWorldMatrix
    (renderPos: Vector3)
    (facing: float32)
    (scale: float32)
    (squishFactor: float32)
    : Matrix =
    let squishCompensation = Matrix.CreateScale(1.0f, 1.0f, squishFactor)
    let scaleM = Matrix.CreateScale(scale)

    Matrix.CreateRotationY(facing)
    * correction
    * squishCompensation
    * scaleM
    * Matrix.CreateTranslation(renderPos)

  /// Extract the Right and Up vectors from the View Matrix for Billboarding.
  let GetBillboardVectors(view: Matrix) =
    let inverseView = Matrix.Invert(view)
    (inverseView.Right, inverseView.Up)

  /// Converts Screen coordinates to Logic coordinates.
  /// Used for Picking / Mouse interaction.
  let ScreenToLogic
    (screenPos: Vector2)
    (viewport: Viewport)
    (zoom: float32)
    (cameraPosition: Vector2)
    : Vector2 =
    let screenCenter =
      Vector2(
        float32 viewport.X + float32 viewport.Width / 2.0f,
        float32 viewport.Y + float32 viewport.Height / 2.0f
      )

    let deltaPixels = screenPos - screenCenter
    let logicDelta = deltaPixels / zoom
    cameraPosition + logicDelta

  module Legacy =
    /// Legacy support: Converts a Logic/Screen position (pixels) to Unified Render Space (3D Units) with explicit depth override.
    let LogicToRenderWithDepth
      (logicPos: Vector2)
      (depthY: float32)
      (pixelsPerUnit: Vector2)
      : Vector3 =
      let x = logicPos.X / pixelsPerUnit.X
      let z = logicPos.Y / pixelsPerUnit.Y
      Vector3(x, depthY, z)

    /// Legacy support: Calculates the 2D transform matrix for SpriteBatch.
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

    /// Legacy support: Calculates the World Matrix for a 3D entity.
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

    /// Legacy support: Calculates the World Matrix for a 3D entity with an additional X-axis tilt and local spin.
    let GetTiltedEntityWorldMatrix
      (renderPos: Vector3)
      (facing: float32)
      (tilt: float32)
      (spin: float32)
      (rotationOffset: float32)
      (squishFactor: float32)
      (modelScale: float32)
      : Matrix =
      let squishCompensation = Matrix.CreateScale(1.0f, 1.0f, squishFactor)
      let scale = Matrix.CreateScale(modelScale)

      Matrix.CreateRotationY(spin) // 1. Spin around local axis
      * Matrix.CreateRotationX(tilt) // 2. Tilt to align Y with Z (Forward)
      * Matrix.CreateRotationY(facing + rotationOffset) // 3. Face movement direction
      * correction
      * squishCompensation
      * scale
      * Matrix.CreateTranslation(renderPos)

    let LogicToRender (logicPos: Vector2) (pixelsPerUnit: Vector2) : Vector3 =
      LogicToRender logicPos 0.0f pixelsPerUnit

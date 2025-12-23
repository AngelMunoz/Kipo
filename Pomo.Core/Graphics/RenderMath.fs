namespace Pomo.Core.Graphics

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

module RenderMath =

  /// Converts a Logic position (pixels) and altitude to Unified Render Space (3D Units).
  /// X = LogicX / PPU.X
  /// Z = (LogicY / PPU.Y) - altitude (elevated objects sort behind ground objects)
  /// Y = Altitude + Z_base (visual height includes altitude and depth bias)
  let inline LogicToRender
    (logicPos: Vector2)
    (altitude: float32)
    (pixelsPerUnit: Vector2)
    : Vector3 =
    let x = logicPos.X / pixelsPerUnit.X
    let zBase = logicPos.Y / pixelsPerUnit.Y
    let y = altitude + zBase
    let z = zBase - altitude
    Vector3(x, y, z)

  /// Converts a 3D particle world position to Unified Render Space.
  /// Particles simulate in 3D where: X/Z = horizontal plane (logic space), Y = altitude.
  /// The altitude must be scaled by the isometric correction factor (Y / PPU.Y * 2.0)
  /// to match the visual proportions of the 2:1 isometric projection.
  let inline ParticleToRender
    (particlePos: Vector3)
    (pixelsPerUnit: Vector2)
    : Vector3 =
    let logicPos = Vector2(particlePos.X, particlePos.Z)
    let altitude = (particlePos.Y / pixelsPerUnit.Y) * 2.0f
    LogicToRender logicPos altitude pixelsPerUnit

  /// Converts a Tile position (pixels) with explicit depth to Unified Render Space (3D Units).
  /// Used for terrain tiles where depthY is pre-calculated from tile bottom edge.
  /// X = LogicX / PPU.X, Z = LogicY / PPU.Y, Y = depthY (no addition)
  let inline TileToRender
    (logicPos: Vector2)
    (depthY: float32)
    (pixelsPerUnit: Vector2)
    : Vector3 =
    let x = logicPos.X / pixelsPerUnit.X
    let z = logicPos.Y / pixelsPerUnit.Y
    Vector3(x, depthY, z)

  /// Calculates the Squish Factor used for isometric correction.
  /// defined as PPU.X / PPU.Y
  let inline GetSquishFactor(pixelsPerUnit: Vector2) : float32 =
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

  /// Calculates the 2D transform matrix for SpriteBatch rendering.
  /// Used for background terrain and UI.
  let Get2DViewMatrix
    (cameraPos: Vector2)
    (zoom: float32)
    (viewport: Viewport)
    : Matrix =
    Matrix.CreateTranslation(-cameraPos.X, -cameraPos.Y, 0.0f)
    * Matrix.CreateScale(zoom, zoom, 1.0f)
    * Matrix.CreateTranslation(
      float32 viewport.Width / 2.0f,
      float32 viewport.Height / 2.0f,
      0.0f
    )

  // Pre-calculated matrices for 3D entity rendering (public for reuse)

  /// Look-at matrix for isometric view direction
  let isoRot =
    Matrix.CreateLookAt(
      Vector3.Zero,
      Vector3.Normalize(Vector3(-1.0f, -1.0f, -1.0f)),
      Vector3.Up
    )

  /// Look-at matrix for top-down view
  let topDownRot =
    Matrix.CreateLookAt(Vector3.Zero, Vector3.Down, Vector3.Forward)

  /// Transforms models from top-down orientation to isometric view.
  /// This rotates meshes modeled standing upright to display correctly
  /// in the 2:1 isometric projection.
  let IsometricCorrectionMatrix = isoRot * Matrix.Invert topDownRot

  /// Calculates the World Matrix for a 3D entity (Mesh).
  /// Includes PiOver4 offset for isometric camera alignment.
  let CreateMeshWorldMatrix
    (renderPos: Vector3)
    (facing: float32)
    (scale: float32)
    (squishFactor: float32)
    : Matrix =
    let squishCompensation = Matrix.CreateScale(1.0f, 1.0f, squishFactor)
    let scaleM = Matrix.CreateScale(scale)

    Matrix.CreateRotationY(facing + MathHelper.PiOver4)
    * IsometricCorrectionMatrix
    * squishCompensation
    * scaleM
    * Matrix.CreateTranslation(renderPos)

  /// Calculates the World Matrix for a projectile with tilt (for descending/ascending).
  /// Tilt rotates around X axis before applying facing.
  /// Includes PiOver4 offset for isometric camera alignment.
  let CreateProjectileWorldMatrix
    (renderPos: Vector3)
    (facing: float32)
    (tilt: float32)
    (scale: float32)
    (squishFactor: float32)
    : Matrix =
    let squishCompensation = Matrix.CreateScale(1.0f, 1.0f, squishFactor)
    let scaleM = Matrix.CreateScale(scale)

    Matrix.CreateRotationX(tilt)
    * Matrix.CreateRotationY(facing + MathHelper.PiOver4)
    * IsometricCorrectionMatrix
    * squishCompensation
    * scaleM
    * Matrix.CreateTranslation(renderPos)

  /// Applies rig node transform with pivot-based rotation.
  /// Used for skeletal animation where rotation should happen around a joint.
  let inline ApplyRigNodeTransform
    (pivot: Vector3)
    (offset: Vector3)
    (animation: Matrix)
    : Matrix =
    let pivotT = Matrix.CreateTranslation pivot
    let inversePivotT = Matrix.CreateTranslation -pivot
    let offsetT = Matrix.CreateTranslation offset
    inversePivotT * animation * pivotT * offsetT

  /// Calculates World Matrix for a mesh particle with non-uniform scaling.
  /// Used for tumbling debris, projectile trails, etc.
  let CreateMeshParticleWorldMatrix
    (renderPos: Vector3)
    (rotation: Quaternion)
    (baseScale: float32)
    (scaleAxis: Vector3)
    (pivot: Vector3)
    (squishFactor: float32)
    : Matrix =
    let scaleMatrix =
      Matrix.CreateScale(
        1.0f + (baseScale - 1.0f) * scaleAxis.X,
        1.0f + (baseScale - 1.0f) * scaleAxis.Y,
        1.0f + (baseScale - 1.0f) * scaleAxis.Z
      )

    let squishCompensation = Matrix.CreateScale(1.0f, 1.0f, squishFactor)

    Matrix.CreateFromQuaternion(rotation)
    * scaleMatrix
    * IsometricCorrectionMatrix
    * squishCompensation
    * Matrix.CreateTranslation(pivot)
    * Matrix.CreateTranslation(renderPos)

  /// Extracts billboard orientation vectors from view matrix
  let inline GetBillboardVectors(view: Matrix) : struct (Vector3 * Vector3) =
    let inverseView = Matrix.Invert(view)
    struct (inverseView.Right, inverseView.Up)

  /// Computes camera view bounds in logic space for culling.
  /// Returns struct(left, right, top, bottom).
  let inline GetViewBounds
    (cameraPos: Vector2)
    (viewportWidth: float32)
    (viewportHeight: float32)
    (zoom: float32)
    : struct (float32 * float32 * float32 * float32) =
    let halfW = viewportWidth / (2.0f * zoom)
    let halfH = viewportHeight / (2.0f * zoom)

    struct (cameraPos.X - halfW,
            cameraPos.X + halfW,
            cameraPos.Y - halfH,
            cameraPos.Y + halfH)

  /// Converts tile grid coordinates to logic position based on map orientation.
  /// Handles orthogonal, isometric, and staggered map layouts.
  let TileGridToLogic
    (orientation: Pomo.Core.Domain.Map.Orientation)
    (staggerAxis: Pomo.Core.Domain.Map.StaggerAxis voption)
    (staggerIndex: Pomo.Core.Domain.Map.StaggerIndex voption)
    (mapWidth: int)
    (x: int)
    (y: int)
    (tileW: float32)
    (tileH: float32)
    : struct (float32 * float32) =
    match orientation, staggerAxis, staggerIndex with
    | Pomo.Core.Domain.Map.Staggered,
      ValueSome Pomo.Core.Domain.Map.X,
      ValueSome index ->
      let xStep = tileW / 2.0f
      let yStep = tileH
      let px = float32 x * xStep

      let isStaggeredCol =
        match index with
        | Pomo.Core.Domain.Map.Odd -> x % 2 = 1
        | Pomo.Core.Domain.Map.Even -> x % 2 = 0

      let yOffset = if isStaggeredCol then tileH / 2.0f else 0.0f
      struct (px, float32 y * yStep + yOffset)

    | Pomo.Core.Domain.Map.Staggered,
      ValueSome Pomo.Core.Domain.Map.Y,
      ValueSome index ->
      let xStep = tileW
      let yStep = tileH / 2.0f
      let py = float32 y * yStep

      let isStaggeredRow =
        match index with
        | Pomo.Core.Domain.Map.Odd -> y % 2 = 1
        | Pomo.Core.Domain.Map.Even -> y % 2 = 0

      let xOffset = if isStaggeredRow then tileW / 2.0f else 0.0f
      struct (float32 x * xStep + xOffset, py)

    | Pomo.Core.Domain.Map.Isometric, _, _ ->
      let originX = float32 mapWidth * tileW / 2.0f
      let px = originX + (float32 x - float32 y) * tileW / 2.0f
      let py = (float32 x + float32 y) * tileH / 2.0f
      struct (px, py)

    | _ -> struct (float32 x * tileW, float32 y * tileH)

  /// Converts Screen coordinates to Logic coordinates.
  /// Used for Picking / Mouse interaction.
  let inline ScreenToLogic
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

  /// Converts Logic coordinates to Screen coordinates.
  /// Inverse of ScreenToLogic.
  let inline LogicToScreen
    (logicPos: Vector2)
    (viewport: Viewport)
    (zoom: float32)
    (cameraPosition: Vector2)
    : Vector2 =
    let screenCenter =
      Vector2(
        float32 viewport.X + float32 viewport.Width / 2.0f,
        float32 viewport.Y + float32 viewport.Height / 2.0f
      )

    let logicDelta = logicPos - cameraPosition
    let deltaPixels = logicDelta * zoom
    screenCenter + deltaPixels

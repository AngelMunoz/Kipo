namespace Pomo.Core.Editor

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain
open Pomo.Core.Domain.Core

/// Editor camera state supporting isometric and free-fly modes.
/// Uses mutable properties for high-frequency updates.
type EditorCameraState() =
  static let defaultIsometricYaw = MathHelper.PiOver4
  static let defaultIsometricPitch = -MathHelper.ToRadians(30.0f) // Standard Isometric Slope

  member val Position = Vector3.Zero with get, set
  member val Yaw = defaultIsometricYaw with get, set
  member val Pitch = defaultIsometricPitch with get, set
  member val Zoom = 2.5f with get, set
  member val Mode = Isometric with get, set

  member this.ResetToIsometric() =
    this.Yaw <- defaultIsometricYaw
    this.Pitch <- defaultIsometricPitch
    this.Mode <- Isometric
    this.Position <- Vector3.Zero
    this.Zoom <- 2.0f

module EditorCamera =

  let panXZ (cam: EditorCameraState) (deltaX: float32) (deltaZ: float32) =
    // Pan relative to camera orientation? Or World Axes?
    // User expects arrow keys to move "grid relative".
    // Left/Right -> X axis. Up/Down -> Z axis.
    // So World Axes is better for "Map Editor".
    cam.Position <- cam.Position + Vector3(deltaX, 0f, deltaZ)

  let moveFreeFly
    (cam: EditorCameraState)
    (deltaX: float32)
    (deltaY: float32)
    (deltaZ: float32)
    =
    // Move relative to camera view
    let rotation = Matrix.CreateFromYawPitchRoll(cam.Yaw, cam.Pitch, 0f)
    let forward = Vector3.Transform(Vector3.Forward, rotation)
    let right = Vector3.Transform(Vector3.Right, rotation)
    let up = Vector3.Transform(Vector3.Up, rotation)

    cam.Position <-
      cam.Position + forward * deltaZ + right * deltaX + up * deltaY

  let rotate
    (cam: EditorCameraState)
    (deltaYaw: float32)
    (deltaPitch: float32)
    =
    cam.Yaw <- cam.Yaw + deltaYaw

    cam.Pitch <-
      MathHelper.Clamp(
        cam.Pitch + deltaPitch,
        -MathHelper.PiOver2 + 0.1f,
        MathHelper.PiOver2 - 0.1f
      )

  let zoom (cam: EditorCameraState) (delta: float32) =
    cam.Zoom <- MathHelper.Clamp(cam.Zoom + delta, 0.5f, 10.0f)

  let getViewMatrix(cam: EditorCameraState) : Matrix =
    match cam.Mode with
    | Isometric
    | FreeFly ->
      // Both use standard 3D camera logic now
      let rotation = Matrix.CreateFromYawPitchRoll(cam.Yaw, cam.Pitch, 0f)
      // Camera is positioned at 'Position' but looking at...
      // Wait. We want 'Position' to be the TARGET (Focus Point).
      // So we back up from Position.
      let offset = Vector3.Transform(Vector3.Backward * 100.0f, rotation)
      let cameraPos = cam.Position + offset
      Matrix.CreateLookAt(cameraPos, cam.Position, Vector3.Up)

  let getProjectionMatrix
    (cam: EditorCameraState)
    (viewport: Viewport)
    (pixelsPerUnit: Vector2)
    : Matrix =
    let viewWidth = float32 viewport.Width / (cam.Zoom * pixelsPerUnit.X)
    let viewHeight = float32 viewport.Height / (cam.Zoom * pixelsPerUnit.Y)
    Matrix.CreateOrthographic(viewWidth, viewHeight, 0.1f, 5000.0f)

  let getPickRay
    (cam: EditorCameraState)
    (screenPos: Vector2)
    (viewport: Viewport)
    (pixelsPerUnit: Vector2)
    =

    let view = getViewMatrix cam
    let proj = getProjectionMatrix cam viewport pixelsPerUnit

    let nearSource = Vector3(screenPos.X, screenPos.Y, 0f)
    let farSource = Vector3(screenPos.X, screenPos.Y, 1f)

    let nearPoint = viewport.Unproject(nearSource, proj, view, Matrix.Identity)
    let farPoint = viewport.Unproject(farSource, proj, view, Matrix.Identity)

    let direction = Vector3.Normalize(farPoint - nearPoint)
    Ray(nearPoint, direction)

  let screenToWorld
    (cam: EditorCameraState)
    (screenPos: Vector2)
    (viewport: Viewport)
    (pixelsPerUnit: Vector2)
    (layer: int)
    : WorldPosition =

    let ray = getPickRay cam screenPos viewport pixelsPerUnit

    // Intersect with plane at height = layer * CellSize
    // Note: Render Space Height vs Logic Height.
    // Render Y = Altitude.
    // If we use PPU.X for scaling width, we use PPU.Y for squish?
    // In True 3D, we likely assumed Uniform Scale 1.0 (after PPU division).
    // So Render Y = Logic Y / PPU.Y? Or PPU.X?
    // Isometric usually assumes uniform cube -> isotropic scale?
    // RenderMath uses PPU.X/PPU.Y to 'squish'.
    // If I want uniform 3D blocks, I should divide ALL dimensions by SAME factor?
    // `pixelsPerUnit.X` is usually 64. `pixelsPerUnit.Y` is 32.
    // This implies 2:1 squish is baked into PPU.
    // IF I want True 3D with 2:1 PPU, I will get stretched blocks if I don't squish.
    // User wants "3D".
    // If I use True 3D Camera, blocks should be CUBES.
    // So Render Dimensions should be uniform.
    // `size = CellSize / PPU.X`.
    // So I should use `PPU.X` for EVERYTHING in True 3D mode.
    // `Render Z = logicZ / PPU.X`.
    // `Render Y = logicY / PPU.X`.

    // For now, let's assume Plane Height relative to Render logic.
    // I will enforce Uniform Scale in `EditorRender` refactor.
    // Height = layer * CellSize / pixelsPerUnit.X.

    let planeY = (float32 layer * 32.0f) / pixelsPerUnit.X
    let plane = Plane(Vector3.Up, -planeY)

    let dist = ray.Intersects(plane)

    if dist.HasValue then
      let hitPos = ray.Position + ray.Direction * dist.Value
      // Convert Render Hit -> Logic
      // Logic = Hit * PPU.X (Uniform Scaling for True 3D)
      {
        X = hitPos.X * pixelsPerUnit.X
        Y = float32 layer * 32.0f
        Z = hitPos.Z * pixelsPerUnit.X
      }
    else
      { X = 0f; Y = 0f; Z = 0f }

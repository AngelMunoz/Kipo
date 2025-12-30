namespace Pomo.Core.Editor

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain
open Pomo.Core.Domain.Core

/// Editor camera state supporting isometric and free-fly modes.
/// Uses mutable properties for high-frequency updates.
type EditorCameraState() =
  static let defaultIsometricYaw = MathHelper.PiOver4
  static let defaultIsometricPitch = -MathHelper.Pi / 3.0f // ~60 degrees

  member val Position = Vector3.Zero with get, set
  member val Yaw = defaultIsometricYaw with get, set
  member val Pitch = defaultIsometricPitch with get, set
  member val Zoom = 2.5f with get, set
  member val Mode = Isometric with get, set

  member this.ResetToIsometric() =
    this.Yaw <- defaultIsometricYaw
    this.Pitch <- defaultIsometricPitch
    this.Mode <- Isometric

module EditorCamera =

  let panXZ (cam: EditorCameraState) (deltaX: float32) (deltaZ: float32) =
    cam.Position <- cam.Position + Vector3(deltaX, 0f, deltaZ)

  let moveFreeFly
    (cam: EditorCameraState)
    (deltaX: float32)
    (deltaY: float32)
    (deltaZ: float32)
    =
    cam.Position <- cam.Position + Vector3(deltaX, deltaY, deltaZ)

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
    | Isometric ->
      let target = cam.Position
      let cameraPos = target + Vector3.Up * 100.0f
      Matrix.CreateLookAt(cameraPos, target, Vector3.Forward)
    | FreeFly ->
      let rotation =
        Matrix.CreateRotationX(cam.Pitch) * Matrix.CreateRotationY(cam.Yaw)

      let forward = Vector3.Transform(Vector3.Forward, rotation)
      let target = cam.Position + forward
      Matrix.CreateLookAt(cam.Position + Vector3.Up * 100f, target, Vector3.Up)

  let getProjectionMatrix
    (cam: EditorCameraState)
    (viewport: Viewport)
    (pixelsPerUnit: Vector2)
    : Matrix =
    let viewWidth = float32 viewport.Width / (cam.Zoom * pixelsPerUnit.X)
    let viewHeight = float32 viewport.Height / (cam.Zoom * pixelsPerUnit.Y)
    Matrix.CreateOrthographic(viewWidth, viewHeight, 0.1f, 5000.0f)

  let screenToWorld
    (cam: EditorCameraState)
    (screenPos: Vector2)
    (viewport: Viewport)
    (pixelsPerUnit: Vector2)
    (layer: int)
    : WorldPosition =
    let screenCenter =
      Vector2(float32 viewport.Width / 2f, float32 viewport.Height / 2f)

    let deltaPixels = screenPos - screenCenter
    // Convert screen pixel delta to render unit delta
    // View Width in units = ViewWidthPixels / (Zoom * PPU.X)
    // So 1 Pixel = 1 / (Zoom * PPU) units
    let deltaRender = deltaPixels / (cam.Zoom * pixelsPerUnit)

    // Camera is looking at target (Render Space).
    // In Iso mode, we Pan XZ plane.
    // Cam Position tracks the center of view on the plane.
    let renderPos = cam.Position + Vector3(deltaRender.X, 0f, deltaRender.Y)

    // Convert Render Space -> Logic Space
    {
      X = renderPos.X * pixelsPerUnit.X
      Y = float32 layer * BlockMap.CellSize // Keep layer fixed to input altitude
      Z = renderPos.Z * pixelsPerUnit.Y
    }

namespace Pomo.Core.Graphics

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.Core

/// Centralized 3D camera operations for orthographic projection
module Camera =

  /// Camera state (immutable struct)
  [<Struct>]
  type CameraParams = {
    Position: Vector3
    Yaw: float32
    Pitch: float32
    Zoom: float32
  }

  module Defaults =
    let isometricYaw = MathHelper.PiOver4
    let isometricPitch = -MathHelper.ToRadians(30.0f)

    let defaultParams = {
      Position = Vector3.Zero
      Yaw = isometricYaw
      Pitch = isometricPitch
      Zoom = 1.5f
    }

  /// Pure transformation functions
  module Transform =

    let inline panXZ
      (p: CameraParams)
      (dx: float32)
      (dz: float32)
      : CameraParams =
      {
        p with
            Position = p.Position + Vector3(dx, 0f, dz)
      }

    /// Pans the camera on the XZ plane relative to its current Yaw.
    /// dx: Right/Left on screen, dz: Forward/Backward (Up/Down) on screen.
    let panRelative (p: CameraParams) (dx: float32) (dz: float32) : CameraParams =
      let rotation = Matrix.CreateRotationY(p.Yaw)
      let right = Vector3.Transform(Vector3.Right, rotation)
      let forward = Vector3.Transform(Vector3.Forward, rotation)

      let moveDir = right * dx + forward * dz
      {
        p with
            Position = p.Position + moveDir
      }

    let moveFreeFly (p: CameraParams) (delta: Vector3) : CameraParams =
      let rotation = Matrix.CreateFromYawPitchRoll(p.Yaw, p.Pitch, 0f)
      let forward = Vector3.Transform(Vector3.Forward, rotation)
      let right = Vector3.Transform(Vector3.Right, rotation)
      let up = Vector3.Transform(Vector3.Up, rotation)

      let newPos =
        p.Position + forward * delta.Z + right * delta.X + up * delta.Y

      { p with Position = newPos }

    let inline rotate
      (p: CameraParams)
      (deltaYaw: float32)
      (deltaPitch: float32)
      : CameraParams =
      let newPitch =
        MathHelper.Clamp(
          p.Pitch + deltaPitch,
          -MathHelper.PiOver2 + 0.1f,
          MathHelper.PiOver2 - 0.1f
        )

      {
        p with
            Yaw = p.Yaw + deltaYaw
            Pitch = newPitch
      }

    let inline zoom (p: CameraParams) (delta: float32) : CameraParams = {
      p with
          Zoom = MathHelper.Clamp(p.Zoom + delta, 0.5f, 10.0f)
    }

  /// Matrix and ray computation (pure functions)
  module Compute =

    let getViewMatrix(p: CameraParams) : Matrix =
      let rotation = Matrix.CreateFromYawPitchRoll(p.Yaw, p.Pitch, 0f)
      let offset = Vector3.Transform(Vector3.Backward * 100.0f, rotation)
      let cameraPos = p.Position + offset
      Matrix.CreateLookAt(cameraPos, p.Position, Vector3.Up)

    let getProjectionMatrix
      (p: CameraParams)
      (viewport: Viewport)
      (ppu: float32)
      : Matrix =
      let viewWidth = float32 viewport.Width / (p.Zoom * ppu)
      let viewHeight = float32 viewport.Height / (p.Zoom * ppu)
      Matrix.CreateOrthographic(viewWidth, viewHeight, 0.1f, 5000.0f)

    let getPickRay
      (p: CameraParams)
      (screenPos: Vector2)
      (viewport: Viewport)
      (ppu: float32)
      : Ray =
      let view = getViewMatrix p
      let proj = getProjectionMatrix p viewport ppu
      let nearSource = Vector3(screenPos.X, screenPos.Y, 0f)
      let farSource = Vector3(screenPos.X, screenPos.Y, 1f)

      let nearPoint =
        viewport.Unproject(nearSource, proj, view, Matrix.Identity)

      let farPoint = viewport.Unproject(farSource, proj, view, Matrix.Identity)
      let direction = Vector3.Normalize(farPoint - nearPoint)
      Ray(nearPoint, direction)

    let screenToWorld
      (p: CameraParams)
      (screenPos: Vector2)
      (viewport: Viewport)
      (ppu: float32)
      (planeY: float32)
      : WorldPosition =
      let ray = getPickRay p screenPos viewport ppu
      let plane = Plane(Vector3.Up, -planeY / ppu)
      let dist = ray.Intersects(plane)

      if dist.HasValue then
        let hitPos = ray.Position + ray.Direction * dist.Value

        {
          X = hitPos.X * ppu
          Y = planeY
          Z = hitPos.Z * ppu
        }
      else
        WorldPosition.zero

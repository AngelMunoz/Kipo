namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core

module Camera =

  [<Struct>]
  type Camera = {
    Position: WorldPosition
    Zoom: float32
    Viewport: Viewport
    View: Matrix
    Projection: Matrix
  }

  type CameraService =
    abstract member GetCamera: Guid<EntityId> -> Camera voption
    abstract member GetAllCameras: unit -> struct (Guid<EntityId> * Camera)[]

    abstract member ScreenToWorld:
      Vector2 * Guid<EntityId> -> WorldPosition voption

    abstract member CreatePickRay: Vector2 * Guid<EntityId> -> Ray voption

/// Full 3D camera with immutable state. Module functions only, no classes.
module Camera3D =

  let private defaultIsometricYaw = MathHelper.PiOver4
  let private defaultIsometricPitch = -MathHelper.ToRadians(30.0f)

  [<Struct>]
  type State = {
    Position: Vector3
    Yaw: float32
    Pitch: float32
    Zoom: float32
  }

  let defaultState = {
    Position = Vector3.Zero
    Yaw = defaultIsometricYaw
    Pitch = defaultIsometricPitch
    Zoom = 2.5f
  }

  let inline panXZ (state: State) (deltaX: float32) (deltaZ: float32) : State = {
    state with
        Position = state.Position + Vector3(deltaX, 0f, deltaZ)
  }

  let moveFreeFly (state: State) (delta: Vector3) : State =
    let rotation = Matrix.CreateFromYawPitchRoll(state.Yaw, state.Pitch, 0f)
    let forward = Vector3.Transform(Vector3.Forward, rotation)
    let right = Vector3.Transform(Vector3.Right, rotation)
    let up = Vector3.Transform(Vector3.Up, rotation)

    let newPos =
      state.Position + forward * delta.Z + right * delta.X + up * delta.Y

    { state with Position = newPos }

  let inline rotate
    (state: State)
    (deltaYaw: float32)
    (deltaPitch: float32)
    : State =
    let newPitch =
      MathHelper.Clamp(
        state.Pitch + deltaPitch,
        -MathHelper.PiOver2 + 0.1f,
        MathHelper.PiOver2 - 0.1f
      )

    {
      state with
          Yaw = state.Yaw + deltaYaw
          Pitch = newPitch
    }

  let inline zoom (state: State) (delta: float32) : State = {
    state with
        Zoom = MathHelper.Clamp(state.Zoom + delta, 0.5f, 10.0f)
  }

  let getViewMatrix(state: State) : Matrix =
    let rotation = Matrix.CreateFromYawPitchRoll(state.Yaw, state.Pitch, 0f)
    let offset = Vector3.Transform(Vector3.Backward * 100.0f, rotation)
    let cameraPos = state.Position + offset
    Matrix.CreateLookAt(cameraPos, state.Position, Vector3.Up)

  let getProjectionMatrix
    (state: State)
    (viewport: Viewport)
    (pixelsPerUnit: float32)
    : Matrix =
    let viewWidth = float32 viewport.Width / (state.Zoom * pixelsPerUnit)
    let viewHeight = float32 viewport.Height / (state.Zoom * pixelsPerUnit)
    Matrix.CreateOrthographic(viewWidth, viewHeight, 0.1f, 5000.0f)

  let getPickRay
    (state: State)
    (screenPos: Vector2)
    (viewport: Viewport)
    (pixelsPerUnit: float32)
    : Ray =
    let view = getViewMatrix state
    let proj = getProjectionMatrix state viewport pixelsPerUnit
    let nearSource = Vector3(screenPos.X, screenPos.Y, 0f)
    let farSource = Vector3(screenPos.X, screenPos.Y, 1f)
    let nearPoint = viewport.Unproject(nearSource, proj, view, Matrix.Identity)
    let farPoint = viewport.Unproject(farSource, proj, view, Matrix.Identity)
    let direction = Vector3.Normalize(farPoint - nearPoint)
    Ray(nearPoint, direction)

  let screenToWorld
    (state: State)
    (screenPos: Vector2)
    (viewport: Viewport)
    (pixelsPerUnit: float32)
    (planeY: float32)
    : WorldPosition =
    let ray = getPickRay state screenPos viewport pixelsPerUnit
    let plane = Plane(Vector3.Up, -planeY / pixelsPerUnit)
    let dist = ray.Intersects(plane)

    if dist.HasValue then
      let hitPos = ray.Position + ray.Direction * dist.Value

      {
        X = hitPos.X * pixelsPerUnit
        Y = planeY
        Z = hitPos.Z * pixelsPerUnit
      }
    else
      WorldPosition.zero

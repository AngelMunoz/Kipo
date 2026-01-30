namespace Pomo.Lib.Editor.Subsystems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Mibo.Elmish
open Mibo.Rendering.Graphics3D
open Pomo.Lib.Services

module Camera =
  [<Struct>]
  type CameraMode =
    | Isometric
    | FreeFly

  [<Struct>]
  type CameraModel = {
    Camera: Camera
    Mode: CameraMode
    Pan: Vector2
    Zoom: float32
    CurrentLayer: int
    IsDragging: bool
    LastMousePos: Point
  }

  type CameraMsg =
    | SetMode of CameraMode
    | Pan of delta: Vector2
    | Zoom of delta: float32
    | SetLayer of layer: int
    | SetIsDragging of isDragging: bool
    | SetLastMousePos of pos: Point
    | Orbit of yaw: float32 * pitch: float32
    | ResetCamera

  let isometricDefaults: Camera =
    let yaw = MathHelper.ToRadians -45f
    let pitch = MathHelper.ToRadians -35.264f
    let distance = 50f
    let target = Vector3.Zero

    Camera.perspectiveDefaults |> Camera.orbit target yaw pitch distance

  let freeFlyDefaults: Camera =
    Camera.perspectiveDefaults
    |> Camera.at(Vector3(20f, 20f, 20f))
    |> Camera.lookingAt Vector3.Zero

  let init(_env: #FileSystemCap & #AssetsCap) : CameraModel = {
    Camera = isometricDefaults
    Mode = Isometric
    Pan = Vector2.Zero
    Zoom = 1f
    CurrentLayer = 0
    IsDragging = false
    LastMousePos = Point.Zero
  }

  let private applyZoom(model: CameraModel) =
    let distance = Vector3.Distance(model.Camera.Position, model.Camera.Target)
    let newDistance = Math.Max(5f, Math.Min(200f, distance / model.Zoom))

    let newCamera =
      if model.Zoom <> 1f then
        let dir = Vector3.Normalize(model.Camera.Position - model.Camera.Target)
        let newPos = model.Camera.Target + dir * newDistance
        model.Camera |> Camera.at newPos
      else
        model.Camera

    {
      model with
          Camera = newCamera
          Zoom = 1f
    }

  let private applyIsometricPan(model: CameraModel) =
    let right =
      Vector3.Normalize(Vector3.Cross(model.Camera.Forward, model.Camera.Up))

    let up = model.Camera.Up

    let offset = right * model.Pan.X + up * model.Pan.Y

    let newTarget = model.Camera.Target + offset
    let newPos = model.Camera.Position + offset

    {
      model with
          Camera =
            model.Camera |> Camera.at newPos |> Camera.lookingAt newTarget
          Pan = Vector2.Zero
    }

  let update
    (_env: #FileSystemCap & #AssetsCap)
    (msg: CameraMsg)
    (model: CameraModel)
    : struct (CameraModel * Cmd<CameraMsg>) =
    match msg with
    | SetMode mode ->
      let newCamera =
        match mode with
        | Isometric -> isometricDefaults
        | FreeFly -> freeFlyDefaults

      {
        model with
            Mode = mode
            Camera = newCamera
      },
      Cmd.none
    | Pan delta ->
      { model with Pan = model.Pan + delta } |> applyIsometricPan, Cmd.none
    | Zoom delta ->
      { model with Zoom = model.Zoom + delta } |> applyZoom, Cmd.none
    | SetLayer layer -> { model with CurrentLayer = layer }, Cmd.none
    | SetIsDragging isDragging ->
      { model with IsDragging = isDragging }, Cmd.none
    | SetLastMousePos pos -> { model with LastMousePos = pos }, Cmd.none

    | Orbit(yaw, pitch) ->
      match model.Mode with
      | FreeFly ->
        let clampedPitch =
          MathHelper.Clamp(
            pitch,
            -MathHelper.PiOver2 + 0.1f,
            MathHelper.PiOver2 - 0.1f
          )

        let distance =
          Vector3.Distance(model.Camera.Position, model.Camera.Target)

        let newCamera =
          model.Camera
          |> Camera.orbit model.Camera.Target yaw clampedPitch distance

        { model with Camera = newCamera }, Cmd.none
      | Isometric -> model, Cmd.none

    | ResetCamera ->
      let newCamera =
        match model.Mode with
        | Isometric -> isometricDefaults
        | FreeFly -> freeFlyDefaults

      { model with Camera = newCamera }, Cmd.none

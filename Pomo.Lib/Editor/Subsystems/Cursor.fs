namespace Pomo.Lib.Editor.Subsystems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Rendering.Graphics3D
open Pomo.Lib.Services

module Cursor =
  [<Struct>]
  type CursorModel = { Cell: Vector3 voption }

  [<Struct>]
  type UpdatePosition = {
    MousePos: Vector2
    ViewportWidth: int
    ViewportHeight: int
    Camera: Camera
    LayerY: float32
  }

  [<Struct>]
  type Msg =
    | UpdatePosition of updatePosition: UpdatePosition
    | ClearPosition

  let init _ : CursorModel = { Cell = ValueNone }

  let private rayPlaneIntersection
    (ray: Ray, planeY: float32)
    : float32 voption =
    if Math.Abs ray.Direction.Y < 0.0001f then
      ValueNone
    else
      let t = (planeY - ray.Position.Y) / ray.Direction.Y
      if t >= 0f then ValueSome t else ValueNone

  let private worldToGridCell(worldPos: Vector3) : Vector3 =
    Vector3(
      MathF.Floor worldPos.X,
      MathF.Floor worldPos.Y,
      MathF.Floor worldPos.Z
    )

  let update _ (msg: Msg) _ : struct (CursorModel * Cmd<Msg>) =
    match msg with
    | UpdatePosition {
                       MousePos = mousePos
                       ViewportWidth = viewportWidth
                       ViewportHeight = viewportHeight
                       Camera = camera
                       LayerY = layerY
                     } ->
      let viewport = Viewport(0, 0, viewportWidth, viewportHeight)
      let ray = Camera.screenPointToRay camera mousePos viewport

      match rayPlaneIntersection(ray, layerY) with
      | ValueNone -> { Cell = ValueNone }, Cmd.none
      | ValueSome distance ->
        let worldPos = ray.Position + ray.Direction * distance
        let cell = worldToGridCell worldPos
        { Cell = ValueSome cell }, Cmd.none

    | ClearPosition -> { Cell = ValueNone }, Cmd.none

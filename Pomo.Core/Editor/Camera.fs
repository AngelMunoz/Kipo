namespace Pomo.Core.Editor

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.Core
open Pomo.Core.Graphics

/// Mutable camera state for high-frequency editor updates.
/// Delegates all math to Graphics.Camera module.
type MutableCamera() =
  member val Params = Camera.Defaults.defaultParams with get, set
  member val Mode = Isometric with get, set

  member this.ResetToIsometric() =
    this.Params <- Camera.Defaults.defaultParams
    this.Mode <- Isometric

/// Editor camera operations using centralized Camera module
module EditorCamera =

  let inline panXZ (cam: MutableCamera) (deltaX: float32) (deltaZ: float32) =
    cam.Params <- Camera.Transform.panXZ cam.Params deltaX deltaZ

  let inline panRelative (cam: MutableCamera) (deltaX: float32) (deltaZ: float32) =
    cam.Params <- Camera.Transform.panRelative cam.Params deltaX deltaZ

  let moveFreeFly
    (cam: MutableCamera)
    (deltaX: float32)
    (deltaY: float32)
    (deltaZ: float32)
    =
    cam.Params <-
      Camera.Transform.moveFreeFly cam.Params (Vector3(deltaX, deltaY, deltaZ))

  let inline rotate
    (cam: MutableCamera)
    (deltaYaw: float32)
    (deltaPitch: float32)
    =
    cam.Params <- Camera.Transform.rotate cam.Params deltaYaw deltaPitch

  let inline zoom (cam: MutableCamera) (delta: float32) =
    cam.Params <- Camera.Transform.zoom cam.Params delta

  let inline getViewMatrix(cam: MutableCamera) : Matrix =
    Camera.Compute.getViewMatrix cam.Params

  let inline getProjectionMatrix
    (cam: MutableCamera)
    (viewport: Viewport)
    (ppu: float32)
    : Matrix =
    Camera.Compute.getProjectionMatrix cam.Params viewport ppu

  let inline getPickRay
    (cam: MutableCamera)
    (screenPos: Vector2)
    (viewport: Viewport)
    (ppu: float32)
    : Ray =
    Camera.Compute.getPickRay cam.Params screenPos viewport ppu

  let inline screenToWorld
    (cam: MutableCamera)
    (screenPos: Vector2)
    (viewport: Viewport)
    (ppu: float32)
    (planeY: float32)
    : WorldPosition =
    Camera.Compute.screenToWorld cam.Params screenPos viewport ppu planeY

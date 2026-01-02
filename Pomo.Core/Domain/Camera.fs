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

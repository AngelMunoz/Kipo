namespace Pomo.Lib.Services

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Graphics
open Mibo.Rendering.Graphics3D

/// Service for tracking cursor position in the editor
[<Interface>]
type EditorCursorService =
  /// Get grid cell at cursor position for given camera and layer
  abstract GetCursorCell: camera: Camera -> layerY: float32 -> Vector3 voption

/// Capability interface for accessing EditorCursorService
[<Interface>]
type EditorCursorCap =
  abstract EditorCursor: EditorCursorService

module EditorCursor =

  let live(graphicsDevice: GraphicsDevice) : EditorCursorService =
    { new EditorCursorService with
        member _.GetCursorCell camera layerY =
          let mouseState = Mouse.GetState()
          let viewport = graphicsDevice.Viewport
          let mousePos = Vector2(float32 mouseState.X, float32 mouseState.Y)
          let ray = Camera.screenPointToRay camera mousePos viewport

          // Ray-plane intersection with Y plane at layerY
          if Math.Abs ray.Direction.Y < 0.0001f then
            ValueNone
          else
            let t = (layerY - ray.Position.Y) / ray.Direction.Y

            if t >= 0f then
              let worldPos = ray.Position + ray.Direction * t
              ValueSome worldPos
            else
              ValueNone
    }

  /// Helper function to get cursor cell from environment
  let getCursorCell (env: #EditorCursorCap) camera layerY =
    env.EditorCursor.GetCursorCell camera layerY

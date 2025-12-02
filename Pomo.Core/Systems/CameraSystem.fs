namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Camera
open Pomo.Core.Projections


module CameraSystem =

  let create
    (game: Game, projections: ProjectionService, localPlayers: Guid<EntityId>[])
    =
    // Constants
    let pixelsPerUnitX = 64.0f
    let pixelsPerUnitY = 32.0f
    let defaultZoom = 2.0f

    { new CameraService with
        member this.GetCamera(playerId: Guid<EntityId>) =
          if Array.contains playerId localPlayers then
            let graphicsDevice = game.GraphicsDevice
            let width = graphicsDevice.PresentationParameters.BackBufferWidth
            let height = graphicsDevice.PresentationParameters.BackBufferHeight

            let playerCount = localPlayers.Length

            let playerIndex =
              Array.findIndex (fun id -> id = playerId) localPlayers

            let viewport =
              match playerCount with
              | 1 ->
                Microsoft.Xna.Framework.Graphics.Viewport(0, 0, width, height)
              | 2 ->
                let w = width / 2

                if playerIndex = 0 then
                  Microsoft.Xna.Framework.Graphics.Viewport(0, 0, w, height)
                else
                  Microsoft.Xna.Framework.Graphics.Viewport(w, 0, w, height)
              | 3
              | 4 ->
                let w = width / 2
                let h = height / 2

                match playerIndex with
                | 0 -> Microsoft.Xna.Framework.Graphics.Viewport(0, 0, w, h)
                | 1 -> Microsoft.Xna.Framework.Graphics.Viewport(w, 0, w, h)
                | 2 -> Microsoft.Xna.Framework.Graphics.Viewport(0, h, w, h)
                | 3 -> Microsoft.Xna.Framework.Graphics.Viewport(w, h, w, h)
                | _ ->
                  Microsoft.Xna.Framework.Graphics.Viewport(0, 0, width, height)
              | _ ->
                Microsoft.Xna.Framework.Graphics.Viewport(0, 0, width, height)

            let entityScenarios = projections.EntityScenarios |> AMap.force

            let position =
              match entityScenarios |> HashMap.tryFindV playerId with
              | ValueSome scenarioId ->
                projections.ComputeMovementSnapshot(scenarioId).Positions
                |> HashMap.tryFind playerId
                |> Option.defaultValue Vector2.Zero
              | ValueNone -> Vector2.Zero

            // 3D Camera Logic (axis-aligned top-down view)
            let target =
              Vector3(
                position.X / pixelsPerUnitX,
                0.0f,
                position.Y / pixelsPerUnitY
              )

            // Look straight down from above
            let cameraPos = target + Vector3.Up * 100.0f
            // Up vector is Forward (0, 0, -1) so that Z maps to screen Y (down)
            let view = Matrix.CreateLookAt(cameraPos, target, Vector3.Forward)

            // Orthographic Projection respecting zoom and unit scale
            // We want 1 unit in 3D to correspond to pixelsPerUnit * Zoom pixels on screen
            let viewWidth =
              float32 viewport.Width / (defaultZoom * pixelsPerUnitX)

            let viewHeight =
              float32 viewport.Height / (defaultZoom * pixelsPerUnitY)

            let projection =
              Matrix.CreateOrthographic(viewWidth, viewHeight, 0.1f, 5000.0f)

            ValueSome {
              Position = position
              Zoom = defaultZoom
              Viewport = viewport
              View = view
              Projection = projection
            }
          else
            ValueNone

        member this.GetAllCameras() =
          localPlayers
          |> Array.choose(fun id ->
            this.GetCamera id
            |> ValueOption.map(fun cam -> struct (id, cam))
            |> ValueOption.toOption)

        member this.ScreenToWorld
          (screenPos: Vector2, playerId: Guid<EntityId>)
          =
          match this.GetCamera playerId with
          | ValueSome camera ->
            // Check if screenPos is within the viewport
            let viewport = camera.Viewport

            if
              screenPos.X >= float32 viewport.X
              && screenPos.X <= float32(viewport.X + viewport.Width)
              && screenPos.Y >= float32 viewport.Y
              && screenPos.Y <= float32(viewport.Y + viewport.Height)
            then

              // Transform
              let transform =
                Matrix.CreateTranslation(
                  -camera.Position.X,
                  -camera.Position.Y,
                  0.0f
                )
                * Matrix.CreateScale(camera.Zoom)
                * Matrix.CreateTranslation(
                  float32 viewport.Width / 2.0f,
                  float32 viewport.Height / 2.0f,
                  0.0f
                )

              // Invert transform to go from Screen -> World
              let inverse = Matrix.Invert(transform)

              let viewportPos =
                Vector2(
                  screenPos.X - float32 viewport.X,
                  screenPos.Y - float32 viewport.Y
                )

              let worldPos = Vector2.Transform(viewportPos, inverse)
              ValueSome worldPos
            else
              ValueNone
          | ValueNone -> ValueNone
    }

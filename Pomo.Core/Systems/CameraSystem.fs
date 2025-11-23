namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Projections


module CameraSystem =

  let create
    (game: Game, projections: ProjectionService, localPlayers: Guid<EntityId>[])
    =
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

            let position =
              projections.UpdatedPositions
              |> AMap.tryFind playerId
              |> AVal.force
              |> Option.defaultValue Vector2.Zero

            ValueSome {
              Position = position
              Zoom = 2.0f
              Viewport = viewport
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

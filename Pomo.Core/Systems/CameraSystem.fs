namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Camera
open Pomo.Core.Domain.World
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Projections
open Pomo.Core.Graphics

module CameraSystem =

  let create
    (
      game: Game,
      projections: ProjectionService,
      world: World,
      localPlayers: Guid<EntityId>[]
    ) =
    let defaultZoom = 2.5f

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
            let scenarios = world.Scenarios |> AMap.force

            let position, pixelsPerUnit =
              match entityScenarios |> HashMap.tryFindV playerId with
              | ValueSome scenarioId ->
                let pos =
                  projections.ComputeMovementSnapshot(scenarioId).Positions
                  |> Dictionary.tryFindV playerId
                  |> ValueOption.defaultValue WorldPosition.zero

                let ppu =
                  match scenarios |> HashMap.tryFindV scenarioId with
                  | ValueSome _ -> Constants.BlockMap3DPixelsPerUnit
                  | ValueNone -> Constants.DefaultPixelsPerUnit

                pos, ppu
              | ValueNone -> WorldPosition.zero, Constants.DefaultPixelsPerUnit

            let view = RenderMath.Camera.getViewMatrix position pixelsPerUnit

            let projection =
              RenderMath.Camera.getProjectionMatrix
                viewport
                defaultZoom
                pixelsPerUnit

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
            if camera.Viewport.Bounds.Contains screenPos then
              let worldPos =
                RenderMath.ScreenLogic.toLogic
                  screenPos
                  camera.Viewport
                  camera.Zoom
                  camera.Position

              ValueSome worldPos
            else
              ValueNone
          | ValueNone -> ValueNone

        member this.CreatePickRay
          (screenPos: Vector2, playerId: Guid<EntityId>)
          =
          match this.GetCamera playerId with
          | ValueSome camera ->
            if camera.Viewport.Bounds.Contains screenPos then
              ValueSome(
                Picking.createPickRay
                  screenPos
                  camera.Viewport
                  camera.View
                  camera.Projection
              )
            else
              ValueNone
          | ValueNone -> ValueNone
    }

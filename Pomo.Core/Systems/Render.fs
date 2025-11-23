namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Systems.Targeting


module Render =
  open Pomo.Core
  open Pomo.Core.Domain.Projectile

  type DrawCommand =
    | DrawPlayer of rect: Rectangle
    | DrawEnemy of rect: Rectangle
    | DrawProjectile of rect: Rectangle
    | DrawTargetingIndicator of rect: Rectangle

  let private generateEntityCommands
    (positions: amap<Guid<EntityId>, Vector2>)
    (projectiles: amap<Guid<EntityId>, LiveProjectile>)
    (playerId: Guid<EntityId>)
    (cameraService: Core.CameraService)
    =
    positions
    |> AMap.chooseA(fun entityId pos -> adaptive {
      let! isProjectile = AMap.keys projectiles |> ASet.contains entityId

      if isProjectile then
        return None
      else
        let size = Core.Constants.Entity.Size

        // Check if entity is within any camera's viewport (for split-screen)
        let entityRect =
          Rectangle(
            int(pos.X - size.X / 2.0f),
            int(pos.Y - size.Y / 2.0f),
            int size.X,
            int size.Y
          )

        // Add padding to prevent entities from popping in/out at viewport boundaries
        let cullingPadding = 64.0f // Add 64 pixels of padding around viewport

        // Get all camera viewports and check if entity is within any of them
        let isVisible =
          cameraService.GetAllCameras()
          |> Array.exists(fun struct (_, camera) ->
            let paddedViewportRect =
              Rectangle(
                int(
                  camera.Position.X
                  - float32 camera.Viewport.Width / (2.0f * camera.Zoom)
                  - cullingPadding
                ),
                int(
                  camera.Position.Y
                  - float32 camera.Viewport.Height / (2.0f * camera.Zoom)
                  - cullingPadding
                ),
                int(float32 camera.Viewport.Width / camera.Zoom + 2.0f * cullingPadding),
                int(float32 camera.Viewport.Height / camera.Zoom + 2.0f * cullingPadding)
              )

            paddedViewportRect.Intersects entityRect)

        if isVisible then
          if entityId = playerId then
            return Some(DrawPlayer entityRect)
          else
            return Some(DrawEnemy entityRect)
        else
          return None
    })
    |> AMap.fold (fun acc _ cmd -> IndexList.add cmd acc) IndexList.empty

  let private generateProjectileCommands
    (projectiles: amap<Guid<EntityId>, LiveProjectile>)
    (positions: amap<Guid<EntityId>, Vector2>)
    =
    projectiles
    |> AMap.chooseA(fun projectileId _ -> adaptive {
      let! posOpt = AMap.tryFind projectileId positions

      match posOpt with
      | Some pos ->
        let projectileSize = Core.Constants.Projectile.Size

        let rect =
          Rectangle(
            int(pos.X - projectileSize.X / 2.0f),
            int(pos.Y - projectileSize.Y / 2.0f),
            int projectileSize.X,
            int projectileSize.Y
          )

        return Some(DrawProjectile rect)
      | None -> return None
    })
    |> AMap.fold (fun acc _ cmd -> IndexList.add cmd acc) IndexList.empty

  let private generateTargetingIndicatorCommands
    (world: World.World)
    (targetingService: TargetingService)
    (cameraService: Core.CameraService)
    (playerId: Guid<EntityId>)
    =
    adaptive {
      let! targetingMode = targetingService.TargetingMode

      let! mouseState =
        world.RawInputStates
        |> AMap.tryFind playerId
        |> AVal.map(Option.map _.Mouse)

      match targetingMode with
      | ValueSome _ ->
        match mouseState with
        | None -> return IndexList.empty
        | Some mouseState ->
          let rawPos =
            Vector2(
              float32 mouseState.Position.X,
              float32 mouseState.Position.Y
            )

          let indicatorPosition =
            match cameraService.ScreenToWorld(rawPos, playerId) with
            | ValueSome worldPos -> worldPos
            | ValueNone -> rawPos

          let indicatorSize = Core.Constants.UI.TargetingIndicatorSize

          let rect =
            Rectangle(
              int(indicatorPosition.X - indicatorSize.X / 2.0f),
              int(indicatorPosition.Y - indicatorSize.Y / 2.0f),
              int indicatorSize.X,
              int indicatorSize.Y
            )

          return IndexList.single(DrawTargetingIndicator rect)
      | ValueNone -> return IndexList.empty
    }

  let generateDrawCommands
    (world: World.World)
    (targetingService: TargetingService)
    (projections: Projections.ProjectionService)
    (cameraService: Core.CameraService)
    (playerId: Guid<EntityId>)
    =
    adaptive {
      let! entityCmds =
        generateEntityCommands
          projections.UpdatedPositions
          world.LiveProjectiles
          playerId
          cameraService

      and! projectileCmds =
        generateProjectileCommands
          world.LiveProjectiles
          projections.UpdatedPositions

      and! targetingCmds =
        generateTargetingIndicatorCommands
          world
          targetingService
          cameraService
          playerId

      return IndexList.concat [ entityCmds; projectileCmds; targetingCmds ]
    }



  type RenderService =
    abstract Draw: Core.Camera -> unit

  let create
    (
      game: Game,
      world: World.World,
      targetingService: TargetingService,
      projections: Projections.ProjectionService,
      cameraService: Core.CameraService,
      playerId: Guid<EntityId>
    ) =
    let spriteBatch = new SpriteBatch(game.GraphicsDevice)
    let texture = new Texture2D(game.GraphicsDevice, 1, 1)
    texture.SetData [| Color.White |]

    let drawCommands =
      generateDrawCommands
        world
        targetingService
        projections
        cameraService
        playerId

    { new RenderService with
        member _.Draw(camera: Core.Camera) =
          let commandsToExecute = drawCommands |> AVal.force

          let transform =
            Matrix.CreateTranslation(
              -camera.Position.X,
              -camera.Position.Y,
              0.0f
            )
            * Matrix.CreateScale(camera.Zoom)
            * Matrix.CreateTranslation(
              float32 camera.Viewport.Width / 2.0f,
              float32 camera.Viewport.Height / 2.0f,
              0.0f
            )

          game.GraphicsDevice.Viewport <- camera.Viewport

          spriteBatch.Begin(transformMatrix = transform)

          for command in commandsToExecute do
            match command with
            | DrawPlayer rect -> spriteBatch.Draw(texture, rect, Color.White)
            | DrawEnemy rect -> spriteBatch.Draw(texture, rect, Color.Green)
            | DrawProjectile rect -> spriteBatch.Draw(texture, rect, Color.Red)
            | DrawTargetingIndicator rect ->
              spriteBatch.Draw(texture, rect, Color.Blue * 0.5f)

          spriteBatch.End()
    }

namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Camera
open Pomo.Core.Systems.Targeting


module Render =
  open Pomo.Core
  open Pomo.Core.Domain.Projectile
  open Microsoft.Xna.Framework.Input
  open Pomo.Core.Environment

  type DrawCommand =
    | DrawPlayer of rect: Rectangle
    | DrawEnemy of rect: Rectangle
    | DrawProjectile of rect: Rectangle
    | DrawTargetingIndicator of rect: Rectangle

  let private generateEntityCommands
    (snapshot: Projections.MovementSnapshot)
    (projectiles: HashMap<Guid<EntityId>, LiveProjectile>)
    (liveEntities: HashSet<Guid<EntityId>>)
    (playerId: Guid<EntityId>)
    (cameraService: CameraService)
    =
    let positions = snapshot.Positions
    let projectileKeys = projectiles |> HashMap.keys

    positions
    |> HashMap.toArrayV
    |> Array.choose(fun struct (entityId, pos) ->
      if projectileKeys.Contains entityId then
        None
      elif not(liveEntities.Contains entityId) then
        None
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
                int(
                  float32 camera.Viewport.Width / camera.Zoom
                  + 2.0f * cullingPadding
                ),
                int(
                  float32 camera.Viewport.Height / camera.Zoom
                  + 2.0f * cullingPadding
                )
              )

            paddedViewportRect.Intersects entityRect)

        if isVisible then
          if entityId = playerId then
            Some(DrawPlayer entityRect)
          else
            Some(DrawEnemy entityRect)
        else
          None)

  let private generateProjectileCommands
    (projectiles: HashMap<Guid<EntityId>, LiveProjectile>)
    (snapshot: Projections.MovementSnapshot)
    =
    let positions = snapshot.Positions

    projectiles
    |> HashMap.keys
    |> HashSet.chooseV(fun projectileId ->
      match positions |> HashMap.tryFindV projectileId with
      | ValueSome pos ->
        let projectileSize = Core.Constants.Projectile.Size

        let rect =
          Rectangle(
            int(pos.X - projectileSize.X / 2.0f),
            int(pos.Y - projectileSize.Y / 2.0f),
            int projectileSize.X,
            int projectileSize.Y
          )

        ValueSome(DrawProjectile rect)
      | ValueNone -> ValueNone)

  let private generateTargetingIndicatorCommands
    (targetingMode: Skill.Targeting voption)
    (mouseState: MouseState voption)
    (cameraService: CameraService)
    (playerId: Guid<EntityId>)
    =
    match targetingMode with
    | ValueSome _ ->
      match mouseState with
      | ValueNone -> IndexList.empty
      | ValueSome mouseState ->
        let rawPos =
          Vector2(float32 mouseState.Position.X, float32 mouseState.Position.Y)

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

        IndexList.single(DrawTargetingIndicator rect)
    | ValueNone -> IndexList.empty

  [<Struct>]
  type DrawCommandContext = {
    LiveProjectiles: HashMap<Guid<EntityId>, LiveProjectile>
    LiveEntities: HashSet<Guid<EntityId>>
    CameraService: CameraService
    PlayerId: Guid<EntityId>
    Snapshot: Projections.MovementSnapshot
    TargetingMode: Skill.Targeting voption
    MouseState: MouseState voption
  }

  let generateDrawCommands(context: DrawCommandContext) =
    let entityCmds =
      generateEntityCommands
        context.Snapshot
        context.LiveProjectiles
        context.LiveEntities
        context.PlayerId
        context.CameraService

    let projectileCmds =
      generateProjectileCommands context.LiveProjectiles context.Snapshot

    let targetingCmds =
      generateTargetingIndicatorCommands
        context.TargetingMode
        context.MouseState
        context.CameraService
        context.PlayerId

    seq {
      yield! entityCmds
      yield! projectileCmds
      yield! targetingCmds
    }

  type RenderService =
    abstract Draw: Camera -> unit

  open Pomo.Core.Environment.Patterns

  let create(game: Game, env: PomoEnvironment, playerId: Guid<EntityId>) =
    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices

    let world = core.World
    let targetingService = gameplay.TargetingService
    let projections = gameplay.Projections
    let cameraService = gameplay.CameraService
    let spriteBatch = new SpriteBatch(game.GraphicsDevice)
    let texture = new Texture2D(game.GraphicsDevice, 1, 1)
    texture.SetData [| Color.White |]

    { new RenderService with
        member _.Draw(camera: Camera) =
          let snapshot = projections.ComputeMovementSnapshot()

          let mouseState =
            world.RawInputStates |> AMap.map' _.Mouse |> AMap.force

          let targetingMode = targetingService.TargetingMode |> AVal.force
          let mouseState = HashMap.tryFindV playerId mouseState
          let liveProjectiles = world.LiveProjectiles |> AMap.force
          let liveEntities = projections.LiveEntities |> ASet.force

          let drawCommands =
            generateDrawCommands {
              LiveProjectiles = liveProjectiles
              LiveEntities = liveEntities
              CameraService = cameraService
              PlayerId = playerId
              Snapshot = snapshot
              TargetingMode = targetingMode
              MouseState = mouseState
            }

          let transform =
            Matrix.CreateTranslation(
              -camera.Position.X,
              -camera.Position.Y,
              0.0f
            )
            * Matrix.CreateScale(camera.Zoom, camera.Zoom, 1.0f)
            * Matrix.CreateTranslation(
              float32 camera.Viewport.Width / 2.0f,
              float32 camera.Viewport.Height / 2.0f,
              0.0f
            )

          game.GraphicsDevice.Viewport <- camera.Viewport

          spriteBatch.Begin(transformMatrix = transform)

          for command in drawCommands do
            match command with
            | DrawPlayer rect -> spriteBatch.Draw(texture, rect, Color.White)
            | DrawEnemy rect -> spriteBatch.Draw(texture, rect, Color.Green)
            | DrawProjectile rect -> spriteBatch.Draw(texture, rect, Color.Red)
            | DrawTargetingIndicator rect ->
              spriteBatch.Draw(texture, rect, Color.Blue * 0.5f)

          spriteBatch.End()
    }

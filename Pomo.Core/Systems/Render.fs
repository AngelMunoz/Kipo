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
    =
    positions
    |> AMap.chooseA(fun entityId pos -> adaptive {
      let! isProjectile = AMap.keys projectiles |> ASet.contains entityId

      if isProjectile then
        return None
      else
        let size = Core.Constants.Entity.Size

        let rect =
          Rectangle(
            int(pos.X - size.X / 2.0f),
            int(pos.Y - size.Y / 2.0f),
            int size.X,
            int size.Y
          )

        if entityId = playerId then
          return Some(DrawPlayer rect)
        else
          return Some(DrawEnemy rect)
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
          let indicatorPosition =
            Vector2(
              float32 mouseState.Position.X,
              float32 mouseState.Position.Y
            )

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
    (playerId: Guid<EntityId>)
    =
    adaptive {
      let! entityCmds =
        generateEntityCommands
          projections.UpdatedPositions
          world.LiveProjectiles
          playerId

      and! projectileCmds =
        generateProjectileCommands
          world.LiveProjectiles
          projections.UpdatedPositions

      and! targetingCmds =
        generateTargetingIndicatorCommands world targetingService playerId

      return IndexList.concat [ entityCmds; projectileCmds; targetingCmds ]
    }

  type RenderSystem(game: Game, playerId: Guid<EntityId>) =
    inherit DrawableGameComponent(game)

    let world: World.World = game.Services.GetService<World.World>()
    let targetingService = game.Services.GetService<TargetingService>()
    let projections = game.Services.GetService<Projections.ProjectionService>()

    let spriteBatch = lazy (new SpriteBatch(game.GraphicsDevice))

    let mutable texture = Unchecked.defaultof<_>

    let drawCommands =
      generateDrawCommands world targetingService projections playerId

    override _.Initialize() : unit =
      base.Initialize()
      texture <- new Texture2D(game.GraphicsDevice, 1, 1)
      texture.SetData [| Color.White |]

    override _.Draw(gameTime) =
      let commandsToExecute = drawCommands |> AVal.force
      let sb = spriteBatch.Value

      sb.Begin()

      for command in commandsToExecute do
        match command with
        | DrawPlayer rect -> sb.Draw(texture, rect, Color.White)
        | DrawEnemy rect -> sb.Draw(texture, rect, Color.Green)
        | DrawProjectile rect -> sb.Draw(texture, rect, Color.Red)
        | DrawTargetingIndicator rect ->
          sb.Draw(texture, rect, Color.Blue * 0.5f)

      sb.End()

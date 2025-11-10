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

  type DrawCommand =
    | DrawPlayer of rect: Rectangle
    | DrawEnemy of rect: Rectangle
    | DrawProjectile of rect: Rectangle
    | DrawTargetingIndicator of rect: Rectangle

  let private generateEntityCommands
    (world: World.World)
    (playerId: Guid<EntityId>)
    =
    world.Positions
    |> AMap.chooseA(fun entityId pos -> adaptive {
      let! isProjectile =
        AMap.keys world.LiveProjectiles |> ASet.contains entityId

      if isProjectile then
        return None
      else
        let size = Vector2(32.0f, 32.0f)
        let rect = Rectangle(int pos.X, int pos.Y, int size.X, int size.Y)

        if entityId = playerId then
          return Some(DrawPlayer rect)
        else
          return Some(DrawEnemy rect)
    })
    |> AMap.fold (fun acc _ cmd -> IndexList.add cmd acc) IndexList.empty

  let private generateProjectileCommands(world: World.World) =
    world.LiveProjectiles
    |> AMap.chooseA(fun projectileId _ -> adaptive {
      let! posOpt = AMap.tryFind projectileId world.Positions

      match posOpt with
      | Some pos ->
        let projectileSize = Vector2(8.0f, 8.0f)

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

          let indicatorSize = Vector2(64.0f, 64.0f)

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
    (playerId: Guid<EntityId>)
    =
    adaptive {
      let! entityCmds = generateEntityCommands world playerId
      and! projectileCmds = generateProjectileCommands world

      and! targetingCmds =
        generateTargetingIndicatorCommands world targetingService playerId

      return IndexList.concat [ entityCmds; projectileCmds; targetingCmds ]
    }

  type RenderSystem(game: Game, playerId: Guid<EntityId>) =
    inherit DrawableGameComponent(game)

    let world: World.World = game.Services.GetService<World.World>()
    let targetingService = game.Services.GetService<TargetingService>()
    let spriteBatch = lazy (new SpriteBatch(game.GraphicsDevice))

    let mutable texture = Unchecked.defaultof<_>

    let drawCommands = generateDrawCommands world targetingService playerId

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

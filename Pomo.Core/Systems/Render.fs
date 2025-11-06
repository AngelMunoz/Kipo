namespace Pomo.Core.Domains

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domains.Targeting
open Pomo.Core.Domains.RawInput


module Render =

  type RenderSystem(game: Game, playerId: Guid<EntityId>) =
    inherit DrawableGameComponent(game)

    let world: World.World = game.Services.GetService<World.World>()
    let targetingService = game.Services.GetService<TargetingService>()
    let spriteBatch = lazy (new SpriteBatch(game.GraphicsDevice))
    let playerColor = Color.White
    let playerSize = Vector2(32.0f, 32.0f)

    let mutable texture = Unchecked.defaultof<_>

    override _.Initialize() : unit =
      base.Initialize()
      texture <- new Texture2D(game.GraphicsDevice, 1, 1)
      texture.SetData [| Color.White |]

    override _.Draw(gameTime) =
      let sb = spriteBatch.Value
      sb.Begin()
      // Draw all entities (for now, just draw all positions as rectangles)
      let positions = world.Positions |> AMap.force

      for _, pos in positions do
        // Draw a white rectangle (simulate player)
        sb.Draw(
          texture,
          Rectangle(int pos.X, int pos.Y, int playerSize.X, int playerSize.Y),
          playerColor
        )

      // --- Targeting Indicator Logic ---
      let targetingMode = targetingService.TargetingMode |> AVal.force

      let mouseState =
        world.RawInputStates
        |> AMap.tryFind playerId
        |> AVal.map(Option.map _.Mouse)
        |> AVal.force


      if targetingMode.IsValueSome then
        match mouseState with
        | None -> ()
        | Some mouseState ->

          let indicatorPosition =
            Vector2(
              float32 mouseState.Position.X,
              float32 mouseState.Position.Y
            )

          let indicatorSize = Vector2(64.0f, 64.0f) // Example size for indicator
          let indicatorColor = Color.Blue * 0.5f // Semi-transparent blue

          sb.Draw(
            texture,
            Rectangle(
              int(indicatorPosition.X - indicatorSize.X / 2.0f),
              int(indicatorPosition.Y - indicatorSize.Y / 2.0f),
              int indicatorSize.X,
              int indicatorSize.Y
            ),
            indicatorColor
          )
      // --- End Targeting Indicator Logic ---

      sb.End()

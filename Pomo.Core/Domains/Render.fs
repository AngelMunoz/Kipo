namespace Pomo.Core.Domains

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.Data.Adaptive
open Pomo.Core.Domain


module Render =

  type RenderSystem(game: Game) =
    inherit DrawableGameComponent(game)

    let world: World.World = game.Services.GetService<World.World>()
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

      sb.End()

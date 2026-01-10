open Pomo.Core.MiboApp
open Mibo.Elmish

let game =
  Program.create()
  |> Mibo.Elmish.Program.withConfig(fun (game, gdm) ->
    game.IsMouseVisible <- true
    game.Content.RootDirectory <- "Content"
    gdm.PreferredBackBufferWidth <- 1280
    gdm.PreferredBackBufferHeight <- 720)

(new ElmishGame<_, _>(game)).Run()

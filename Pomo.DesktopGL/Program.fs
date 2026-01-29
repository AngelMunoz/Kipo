open Pomo.Core
open Pomo.Lib
open Mibo.Elmish


let game =
  new ElmishGame<_, _>(
    Program.create()
    |> Program.withConfig(fun (game, _) ->
      game.Window.AllowAltF4 <- true
      game.Window.AllowUserResizing <- true
      game.IsMouseVisible <- true)
  )

game.Run()

open Pomo.Core
open System.Windows.Forms
// Configure the application to be DPI-aware for better display scaling.
Application.SetHighDpiMode HighDpiMode.SystemAware |> ignore<bool>

// Create an instance of the game and start the game loop.
let game = new PomoGame()
game.Run()

namespace Pomo.Core.Systems

open FSharp.Data.Adaptive

[<Struct>]
type GuiAction =
  | StartNewGame
  | OpenSettings
  | ExitGame
  | BackToMainMenu

module UIService =

  let create() =
    let isMouseOverUI = cval false

    { new Pomo.Core.Environment.IUIService with
        member _.IsMouseOverUI = isMouseOverUI

        member _.SetMouseOverUI value =
          transact(fun () -> isMouseOverUI.Value <- value)
    }

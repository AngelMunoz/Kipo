namespace Pomo.Core.Systems

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Pomo.Core.Domain.Cursor

module CursorSystem =

  let create(game: Game) : CursorService =
    let mutable currentCursor = Arrow

    { new CursorService with
        member _.SetCursor(cursorType) =
          if cursorType <> currentCursor then
            currentCursor <- cursorType

            let cursor =
              match cursorType with
              | Arrow -> MouseCursor.Arrow
              | Hand -> MouseCursor.Hand
              | Attack -> MouseCursor.Crosshair
              | Move -> MouseCursor.Arrow
              | Targeting -> MouseCursor.SizeAll


            Mouse.SetCursor cursor

        member _.GetCurrentCursor() = currentCursor
    }

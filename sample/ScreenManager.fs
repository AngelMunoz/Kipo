namespace Pomo.Core

open Microsoft.Xna.Framework

// Simple screen manager to swap DrawableGameComponents
type ScreenManager(game: Game) =
  let mutable current: DrawableGameComponent option = None
  member _.Current = current
  member _.Change(next: DrawableGameComponent) =
    match current with
    | Some c -> game.Components.Remove(c) |> ignore
    | None -> ()
    current <- Some next
    game.Components.Add(next)
  interface IGameComponent with
    member _.Initialize() = ()

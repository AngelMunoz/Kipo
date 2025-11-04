namespace Pomo.Core.Domains

open System.Diagnostics
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.Action

module QuickSlot =

  type QuickSlotSystem(game: Game, playerId: Guid<EntityId>) as this =
    inherit GameSystem(game)

    let actionStates =
      this.World.GameActionStates
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue HashMap.empty)

    let pressedActions =
      actionStates
      |> AVal.map(fun states ->
        states
        |> HashMap.chooseV(fun action state ->
          if state = Pressed then ValueSome action else ValueNone)
        |> HashMap.toValueArray)

    override this.Update gameTime =
      let actions = pressedActions |> AVal.force

      for action in actions do
        match action with
        | UseSlot1 -> Debug.WriteLine("Used Quick Slot 1")
        | UseSlot2 -> Debug.WriteLine("Used Quick Slot 2")
        | UseSlot3 -> Debug.WriteLine("Used Quick Slot 3")
        | UseSlot4 -> Debug.WriteLine("Used Quick Slot 4")
        | UseSlot5 -> Debug.WriteLine("Used Quick Slot 5")
        | UseSlot6 -> Debug.WriteLine("Used Quick Slot 6")
        | UseSlot7 -> Debug.WriteLine("Used Quick Slot 7")
        | UseSlot8 -> Debug.WriteLine("Used Quick Slot 8")
        | _ -> ()

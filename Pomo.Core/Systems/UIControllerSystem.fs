namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Core.EventBus
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.UI
open Pomo.Core.Environment

module UIController =

  /// Handle a single game action, toggling appropriate panels
  let inline handleAction (hudService: IHUDService) (action: GameAction) =
    match action with
    // Panel toggles
    | ToggleCharacterSheet ->
      hudService.TogglePanelVisible HUDPanelId.CharacterSheet
    | ToggleInventory -> hudService.TogglePanelVisible HUDPanelId.EquipmentPanel
    | ToggleAbilities // TODO: Abilities panel not yet implemented
    | ToggleJournal // TODO: Journal panel not yet implemented
    // Movement - handled elsewhere
    | MoveUp
    | MoveDown
    | MoveLeft
    | MoveRight
    // Slots - handled elsewhere
    | UseSlot1
    | UseSlot2
    | UseSlot3
    | UseSlot4
    | UseSlot5
    | UseSlot6
    | UseSlot7
    | UseSlot8
    // Action sets - handled elsewhere
    | SetActionSet1
    | SetActionSet2
    | SetActionSet3
    | SetActionSet4
    | SetActionSet5
    | SetActionSet6
    | SetActionSet7
    | SetActionSet8
    // General actions - handled elsewhere
    | PrimaryAction
    | SecondaryAction
    | Cancel
    // Debug actions - handled elsewhere
    | DebugAction1
    | DebugAction2
    | DebugAction3
    | DebugAction4
    | DebugAction5 -> ()

  /// Handle input event for a specific player
  let handleInput
    (hudService: IHUDService)
    (playerId: Guid<EntityId>)
    (inputEvent: Guid<EntityId> * HashMap<GameAction, InputActionState>)
    =
    let entityId, states = inputEvent

    if entityId = playerId then
      for action, state in states do
        if state = Pressed then
          handleAction hudService action

  /// Create a UI controller system using factory function with object expression
  let create (game: Game) (env: PomoEnvironment) (playerId: Guid<EntityId>) =
    let (Core core) = env.CoreServices
    let mutable sub: IDisposable = null

    { new GameComponent(game) with
        override _.Initialize() =
          base.Initialize()

          sub <-
            core.EventBus.Observable
            |> Observable.choose(fun e ->
              match e with
              | GameEvent.State(Input(GameActionStatesChanged(id, states))) ->
                Some(id, states)
              | _ -> None)
            |> Observable.subscribe(handleInput core.HUDService playerId)

        override _.Dispose(disposing: bool) =
          if disposing && not(isNull sub) then
            sub.Dispose()

          base.Dispose disposing
    }

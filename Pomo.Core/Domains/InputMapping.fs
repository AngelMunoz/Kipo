namespace Pomo.Core.Domains

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.EventBus
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.RawInput

module InputMapping =

  let private isKeyJustPressed
    (prev: KeyboardState)
    (curr: KeyboardState)
    (key: Keys)
    =
    curr.IsKeyDown(key) && prev.IsKeyUp(key)

  let private isKeyJustReleased
    (prev: KeyboardState)
    (curr: KeyboardState)
    (key: Keys)
    =
    curr.IsKeyUp(key) && prev.IsKeyDown(key)

  let private isMouseButtonDown (curr: MouseState) (btn: MouseButton) =
    match btn with
    | MouseButton.Left -> curr.LeftButton = ButtonState.Pressed
    | MouseButton.Right -> curr.RightButton = ButtonState.Pressed
    | MouseButton.Middle -> curr.MiddleButton = ButtonState.Pressed

  let private isMouseButtonJustPressed
    (prev: MouseState)
    (curr: MouseState)
    (btn: MouseButton)
    =
    let isDown = isMouseButtonDown curr btn
    let wasDown = isMouseButtonDown prev btn
    isDown && not wasDown

  let private isMouseButtonJustReleased
    (prev: MouseState)
    (curr: MouseState)
    (btn: MouseButton)
    =
    let isDown = isMouseButtonDown curr btn
    let wasDown = isMouseButtonDown prev btn
    not isDown && wasDown

  type InputMappingSystem(game: Game, entityId: Guid<EntityId>) as this =
    inherit GameSystem(game)

    let rawInputState = this.World.RawInputStates |> AMap.tryFind entityId
    let inputMap = this.World.InputMaps |> AMap.tryFind entityId
    let prevActionStates = this.World.GameActionStates |> AMap.tryFind entityId

    let actionStates = adaptive {
      let! rawState = rawInputState
      let! map = inputMap
      let! prevActions = prevActionStates

      match rawState, map with
      | Some raw, Some map ->
        let currentActions =
          prevActions
          |> Option.defaultValue HashMap.empty
          |> HashMap.chooseV(fun _ state ->
            match state with
            | Pressed -> ValueSome Held
            | Held -> ValueSome Held
            | _ -> ValueNone)

        let newActions =
          map
          |> HashMap.fold
            (fun (acc: HashMap<_, _>) rawInput gameAction ->
              let isDown, isJustPressed, isJustReleased =
                match rawInput with
                | Key k ->
                  raw.Keyboard.IsKeyDown(k),
                  isKeyJustPressed raw.PrevKeyboard raw.Keyboard k,
                  isKeyJustReleased raw.PrevKeyboard raw.Keyboard k
                | MouseButton mb ->
                  isMouseButtonDown raw.Mouse mb,
                  isMouseButtonJustPressed raw.PrevMouse raw.Mouse mb,
                  isMouseButtonJustReleased raw.PrevMouse raw.Mouse mb
                | _ -> false, false, false

              if isJustPressed then
                acc.Add(gameAction, Pressed)
              elif isJustReleased then
                acc.Add(gameAction, Released)
              elif isDown then
                if not(acc.ContainsKey(gameAction)) then
                  acc.Add(gameAction, Held)
                else
                  acc
              else
                acc)
            currentActions

        return Some newActions
      | _ -> return None
    }

    override val Kind = InputMapping with get

    override this.Update gameTime =
      match actionStates |> AVal.force with
      | Some states ->
        this.EventBus.Publish(GameActionStatesChanged struct (entityId, states))
      | None -> ()


  let createDefaultInputMap() =
    HashMap.ofSeqV [
      MouseButton MouseButton.Left, PrimaryAction
      MouseButton MouseButton.Right, SecondaryAction
      Key Keys.Q, UseSlot1
      Key Keys.W, UseSlot2
      Key Keys.E, UseSlot3
      Key Keys.R, UseSlot4
      Key Keys.A, UseSlot5
      Key Keys.S, UseSlot6
      Key Keys.D, UseSlot7
      Key Keys.Z, ToggleJournal
      Key Keys.X, ToggleInventory
      Key Keys.C, ToggleAbilities
      Key Keys.V, ToggleCharacterSheet
      Key Keys.D1, SetActionSet1
      Key Keys.D2, SetActionSet2
      Key Keys.D3, SetActionSet3
      Key Keys.D4, SetActionSet4
      Key Keys.D5, SetActionSet5
      Key Keys.D6, SetActionSet6
      Key Keys.D7, SetActionSet7
      Key Keys.D8, SetActionSet8
      Key Keys.Escape, Cancel
      Key Keys.F1, DebugAction1
      Key Keys.F2, DebugAction2
      Key Keys.F3, DebugAction3
      Key Keys.F4, DebugAction4
      Key Keys.F5, DebugAction5
      Key Keys.Up, MoveUp
      Key Keys.Down, MoveDown
      Key Keys.Left, MoveLeft
      Key Keys.Right, MoveRight
    ]

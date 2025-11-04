namespace Pomo.Core.Domain

open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Input.Touch

module Action =

  [<Struct>]
  type MouseButton =
    | Left
    | Right
    | Middle

  [<Struct>]
  type Side =
    | Left
    | Right

  [<Struct>]
  type RawInput =
    | Key of keys: Keys
    | MouseButton of mouseButton: MouseButton
    | GamePadButton of buttons: Buttons
    | GamePadTrigger of playerIndex: PlayerIndex * side: Side
    | GamePadThumbStick of playerIndex: PlayerIndex * side: Side
    | Touch
    | LongPress of duration: float32

  [<Struct>]
  type GameAction =
    // Movement
    | MoveUp
    | MoveDown
    | MoveLeft
    | MoveRight
    // Quick Slots
    | UseSlot1
    | UseSlot2
    | UseSlot3
    | UseSlot4
    | UseSlot5
    | UseSlot6
    | UseSlot7
    | UseSlot8
    // Action Sets
    | SetActionSet1
    | SetActionSet2
    | SetActionSet3
    | SetActionSet4
    | SetActionSet5
    | SetActionSet6
    | SetActionSet7
    | SetActionSet8
    // General Actions
    | PrimaryAction
    | SecondaryAction
    | ToggleInventory
    | ToggleCharacterSheet
    | ToggleAbilities
    | ToggleJournal
    | Cancel
    // Debug Actions
    | DebugAction1
    | DebugAction2
    | DebugAction3
    | DebugAction4
    | DebugAction5

  [<Struct>]
  type InputActionState =
    | Pressed
    | Held
    | Released

  type InputMap = HashMap<RawInput, GameAction>

module RawInput =

  type RawInputState = {
    Keyboard: KeyboardState
    Mouse: MouseState
    GamePad: GamePadState
    Touch: TouchCollection
    PrevKeyboard: KeyboardState
    PrevMouse: MouseState
    PrevGamePad: GamePadState
    PrevTouch: TouchCollection
  }

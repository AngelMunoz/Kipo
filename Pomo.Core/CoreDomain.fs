namespace Pomo.Core.Domain

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Input.Touch
open System.Collections.Concurrent

module Units =
  [<Measure>]
  type EntityId

module Action =
  open Microsoft.Xna.Framework.Input
  open FSharp.UMX

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

module Entity =
  open Units

  [<Struct>]
  type EntitySnapshot = {
    Id: Guid<EntityId>
    Position: Vector2
    Velocity: Vector2
  }

module RawInput =
  open Microsoft.Xna.Framework.Input.Touch

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

module World =
  open Units
  open RawInput
  open Action

  // The internal, MUTABLE source of truth.
  // This contains the "cell" types that can be modified.
  type MutableWorld = {
    DeltaTime: cval<TimeSpan>
    Positions: cmap<Guid<EntityId>, Vector2>
    Velocities: cmap<Guid<EntityId>, Vector2>
    RawInputStates: cmap<Guid<EntityId>, RawInputState>
    InputMaps: cmap<Guid<EntityId>, InputMap>
    GameActionStates:
      cmap<Guid<EntityId>, HashMap<GameAction, InputActionState>>
  }

  // This is the READ-ONLY VIEW of the world.
  // It contains the "adaptive" types.
  type World = {
    DeltaTime: TimeSpan aval
    Positions: amap<Guid<EntityId>, Vector2>
    Velocities: amap<Guid<EntityId>, Vector2>
    RawInputStates: amap<Guid<EntityId>, RawInputState>
    InputMaps: amap<Guid<EntityId>, InputMap>
    GameActionStates:
      amap<Guid<EntityId>, HashMap<GameAction, InputActionState>>
  }

  let create() =
    let mutableWorld: MutableWorld = {
      DeltaTime = cval TimeSpan.Zero
      Positions = cmap()
      Velocities = cmap()
      RawInputStates = cmap()
      InputMaps = cmap()
      GameActionStates = cmap()
    }

    let worldView = {
      DeltaTime = mutableWorld.DeltaTime
      Positions = mutableWorld.Positions
      Velocities = mutableWorld.Velocities
      RawInputStates = mutableWorld.RawInputStates
      InputMaps = mutableWorld.InputMaps
      GameActionStates = mutableWorld.GameActionStates
    }

    struct (mutableWorld, worldView)

  [<Struct>]
  type WorldEvent =
    | EntityCreated of created: Entity.EntitySnapshot
    | EntityRemoved of removed: Guid<EntityId>
    | PositionChanged of posChanged: struct (Guid<EntityId> * Vector2)
    | VelocityChanged of velChanged: struct (Guid<EntityId> * Vector2)
    | RawInputStateChanged of
      rawIChanged: struct (Guid<EntityId> * RawInputState)
    | InputMapChanged of iMapChanged: struct (Guid<EntityId> * InputMap)
    | GameActionStatesChanged of
      gAChanged: struct (Guid<EntityId> * HashMap<GameAction, InputActionState>)

module EventBus =
  open World
  open System.Diagnostics

  type EventBus() =
    let eventQueue = ConcurrentQueue<WorldEvent>()

    member _.Publish(event) =
      Debug.WriteLine($"Event Published: {event}")
      eventQueue.Enqueue(event)

    member _.Publish(events: WorldEvent seq) =
      for event in events do
        Debug.WriteLine($"Event Published: {event}")
        eventQueue.Enqueue(event)

    member _.TryDequeue(event: byref<WorldEvent>) =
      let dequeued = eventQueue.TryDequeue(&event)

      if dequeued then
        Debug.WriteLine($"Event Dequeued: {event}")

      dequeued

module Systems =
  open World
  open EventBus

  [<Struct>]
  type SystemKind =
    | Game
    | Movement
    | RawInput
    | InputMapping

  type GameSystem(game: Game) =
    inherit GameComponent(game)

    abstract Kind: SystemKind
    default _.Kind = Game

    // It gets the READ-ONLY World view. The type system now prevents writing.
    member val World = game.Services.GetService<World>() with get
    member val EventBus = game.Services.GetService<EventBus>() with get

namespace Pomo.Core.Domain

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open System.Collections.Concurrent
open Pomo.Core.Domain.Units


module World =
  open RawInput
  open Action

  type MutableWorld = {
    DeltaTime: cval<TimeSpan>
    RawInputStates: cmap<Guid<EntityId>, RawInputState>
    InputMaps: cmap<Guid<EntityId>, InputMap>
    GameActionStates:
      cmap<Guid<EntityId>, HashMap<GameAction, InputActionState>>
    // entity components
    Positions: cmap<Guid<EntityId>, Vector2>
    Velocities: cmap<Guid<EntityId>, Vector2>
    Resources: cmap<Guid<EntityId>, Entity.Resource>
    Factions: cmap<Guid<EntityId>, Entity.Faction HashSet>
  }

  type World =
    abstract DeltaTime: TimeSpan aval
    abstract RawInputStates: amap<Guid<EntityId>, RawInputState>
    abstract InputMaps: amap<Guid<EntityId>, InputMap>

    abstract GameActionStates:
      amap<Guid<EntityId>, HashMap<GameAction, InputActionState>>
    // entity components
    abstract Positions: amap<Guid<EntityId>, Vector2>
    abstract Velocities: amap<Guid<EntityId>, Vector2>
    abstract Resources: amap<Guid<EntityId>, Entity.Resource>
    abstract Factions: amap<Guid<EntityId>, Entity.Faction HashSet>


  let create() =
    let mutableWorld: MutableWorld = {
      DeltaTime = cval TimeSpan.Zero
      Positions = cmap()
      Velocities = cmap()
      RawInputStates = cmap()
      InputMaps = cmap()
      GameActionStates = cmap()
      Resources = cmap()
      Factions = cmap()
    }

    let worldView =
      { new World with
          member _.DeltaTime = mutableWorld.DeltaTime
          member _.Positions = mutableWorld.Positions
          member _.Velocities = mutableWorld.Velocities
          member _.RawInputStates = mutableWorld.RawInputStates
          member _.InputMaps = mutableWorld.InputMaps
          member _.GameActionStates = mutableWorld.GameActionStates
          member _.Resources = mutableWorld.Resources
          member _.Factions = mutableWorld.Factions
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

    member _.Publish(event) = eventQueue.Enqueue(event)

    member _.Publish(events: WorldEvent seq) =
      for event in events do
        eventQueue.Enqueue(event)

    member _.TryDequeue(event: byref<WorldEvent>) =
      eventQueue.TryDequeue(&event)

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

    member val World = game.Services.GetService<World>() with get
    member val EventBus = game.Services.GetService<EventBus>() with get

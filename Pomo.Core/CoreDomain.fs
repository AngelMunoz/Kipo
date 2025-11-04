namespace Pomo.Core.Domain

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open System.Collections.Concurrent

module Units =
  [<Measure>]
  type EntityId

module Entity =
  open Units

  [<Struct>]
  type EntitySnapshot = {
    Id: Guid<EntityId>
    Position: Vector2
    Velocity: Vector2
  }

module World =
  open Units

  // The internal, MUTABLE source of truth.
  // This contains the "cell" types that can be modified.
  type MutableWorld = {
    DeltaTime: cval<TimeSpan>
    Positions: cmap<Guid<EntityId>, Vector2>
    Velocities: cmap<Guid<EntityId>, Vector2>
  }

  // This is the READ-ONLY VIEW of the world.
  // It contains the "adaptive" types.
  type World = {
    DeltaTime: TimeSpan aval
    Positions: amap<Guid<EntityId>, Vector2>
    Velocities: amap<Guid<EntityId>, Vector2>
  }

  let create() =
    let mutableWorld: MutableWorld = {
      DeltaTime = cval TimeSpan.Zero
      Positions = cmap()
      Velocities = cmap()
    }

    let worldView = {
      DeltaTime = mutableWorld.DeltaTime
      Positions = mutableWorld.Positions
      Velocities = mutableWorld.Velocities
    }

    struct (mutableWorld, worldView)

  [<Struct>]
  type WorldEvent =
    | EntityCreated of created: Entity.EntitySnapshot
    | EntityRemoved of removed: Guid<EntityId>
    | PositionChanged of changed: struct (Guid<EntityId> * Vector2)
    | VelocityChanged of changed: struct (Guid<EntityId> * Vector2)

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

  type GameSystem(game: Game) =
    inherit GameComponent(game)

    abstract Kind: SystemKind
    default _.Kind = Game

    // It gets the READ-ONLY World view. The type system now prevents writing.
    member val World = game.Services.GetService<World>() with get
    member val EventBus = game.Services.GetService<EventBus>() with get

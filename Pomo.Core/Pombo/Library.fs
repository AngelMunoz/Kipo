namespace Pomo.Core.Pombo

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework


module Units =
  [<Measure>]
  type EntityId

module Entity =
  open Units

  [<Struct>]
  type EntitySnapshot = {
    Id: Guid<EntityId>
    Position: Vector2
  }

module World =
  open Units

  // The internal, MUTABLE source of truth.
  // This contains the "cell" types that can be modified.
  type MutableWorld = {
    Time: cval<TimeSpan>
    Positions: cmap<Guid<EntityId>, Vector2>
    Velocities: cmap<Guid<EntityId>, Vector2>
  }

  // This is the READ-ONLY VIEW of the world.
  // It contains the "adaptive" types.
  type World = {
    Time: TimeSpan aval
    Positions: amap<Guid<EntityId>, Vector2>
    Velocities: amap<Guid<EntityId>, Vector2>
  }

  let create() =
    let mutableWorld: MutableWorld = {
      Time = cval TimeSpan.Zero
      Positions = cmap()
      Velocities = cmap()
    }

    let worldView = {
      Time = mutableWorld.Time
      Positions = mutableWorld.Positions
      Velocities = mutableWorld.Velocities
    }

    struct (mutableWorld, worldView)


  [<Struct>]
  type WorldEvent =
    | EntityCreated of created: Entity.EntitySnapshot
    | EntityRemoved of removed: Guid<EntityId>
    | PositionChanged of changed: struct (Guid<EntityId> * Vector2)

  module Projections =
    let Movements(world: World) =
      (world.Velocities, world.Positions)
      ||> AMap.choose2V(fun id velocity position ->
        match velocity, position with
        | ValueSome vel, ValueSome pos ->
          let newPos = pos + vel
          ValueSome pos
        | _ -> ValueNone)


module Events =
  open System.Collections.Concurrent
  open Units
  open World

  type EventBus() =
    let eventQueue = ConcurrentQueue<WorldEvent>()

    member _.Publish(event) = eventQueue.Enqueue(event)

    member _.Publish(events: WorldEvent seq) =
      for event in events do
        eventQueue.Enqueue(event)

    member _.TryDequeue(event: byref<WorldEvent>) =
      eventQueue.TryDequeue(&event)

module Systems =
  open Microsoft.Xna.Framework

  open Units
  open World
  open Events

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
    member val EventBus = game.Services.GetService<Events.EventBus>() with get


  type MovementSystem(game: Game) as this =
    inherit GameSystem(game)
    let movements = Projections.Movements this.World
    let delta = cval TimeSpan.Zero

    override val Kind = Movement with get

    override this.Update gameTime =
      let movements = movements |> AMap.force

      for id, movement in movements do
        let newPosition =
          movement * float32 gameTime.ElapsedGameTime.TotalSeconds

        this.EventBus.Publish(PositionChanged struct (id, newPosition))

  // The dedicated STATE WRITER system.
  // It receives the MutableWorld via constructor injection, ensuring no other system can access it.
  type StateUpdateSystem(game: Game, mutableWorld: World.MutableWorld) =
    inherit GameComponent(game)

    let eventBus = game.Services.GetService<EventBus>()

    override this.Update(gameTime) =
      // This is the one and only place where state is written,
      // wrapped in a single transaction for efficiency.
      transact(fun () ->
        let mutable event = Unchecked.defaultof<WorldEvent>

        while eventBus.TryDequeue(&event) do
          match event with
          | PositionChanged(struct (entity, position)) ->
            // This is possible because we have the cmap from the MutableWorld.
            mutableWorld.Positions.[entity] <- position
          | EntityRemoved removed ->
            mutableWorld.Positions.Remove(removed) |> ignore
            mutableWorld.Velocities.Remove(removed) |> ignore
          | _ -> ())

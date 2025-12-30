namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Events
open Pomo.Core.Systems.Systems
open Pomo.Core.Domain.Core
open Pomo.Core.Algorithms
open Pomo.Core.Systems

module UnitMovement =

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type UnitMovementSystem
    (game: Game, env: PomoEnvironment, playerId: Guid<EntityId>) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let stateWrite = core.StateWrite

    // Mutable state to track last known velocities to avoid spamming events
    let lastVelocities =
      System.Collections.Generic.Dictionary<Guid<EntityId>, Vector2>()

    let mutable sub: IDisposable = null

    let collisionEvents =
      System.Collections.Concurrent.ConcurrentQueue<
        SystemCommunications.CollisionEvents
       >()

    // Local state for path following
    let mutable currentPaths =
      System.Collections.Generic.Dictionary<Guid<EntityId>, Vector2 list>()

    let movementStates =
      core.World.MovementStates |> AMap.filter(fun id _ -> id <> playerId)



    let derivedStats =
      gameplay.Projections.DerivedStats
      |> AMap.filter(fun id _ -> id <> playerId)

    override val Kind = Movement with get

    override this.Initialize() =
      base.Initialize()

      sub <-
        core.EventBus.Observable
        |> Observable.choose(fun e ->
          match e with
          | GameEvent.Collision(collision) -> Some collision
          | _ -> None)
        |> Observable.subscribe(fun e -> collisionEvents.Enqueue(e))


    override this.Dispose(disposing) =
      if disposing then
        sub.Dispose()

      base.Dispose(disposing)

    override this.Update(gameTime) =
      let scenarios = core.World.Scenarios |> AMap.force
      let movementStates = movementStates |> AMap.force
      let derivedStats = derivedStats |> AMap.force
      let entityScenarios = core.World.EntityScenario |> AMap.force

      let frameCollisions =
        System.Collections.Generic.Dictionary<Guid<EntityId>, Vector2>()

      let mutable collisionEvent =
        Unchecked.defaultof<SystemCommunications.CollisionEvents>

      while collisionEvents.TryDequeue(&collisionEvent) do
        match collisionEvent with
        | SystemCommunications.CollisionEvents.MapObjectCollision(eId, _, mtv) when
          eId <> playerId
          ->
          match entityScenarios.TryFindV eId with
          | ValueSome scenarioId ->
            let snapshot =
              gameplay.Projections.ComputeMovementSnapshot(scenarioId)

            match snapshot.Positions |> Dictionary.tryFindV eId with
            | ValueSome currentPos ->

              match frameCollisions.TryGetValue eId with
              | true, existing -> frameCollisions[eId] <- existing + mtv
              | false, _ -> frameCollisions[eId] <- mtv
            | ValueNone -> ()
          | ValueNone -> ()
        | _ -> ()

      // 2. Process Movement
      for (scenarioId, _) in scenarios do
        let snapshot = gameplay.Projections.ComputeMovementSnapshot(scenarioId)

        for KeyValue(entityId, currentPos) in snapshot.Positions do
          if entityId <> playerId then
            match movementStates.TryFindV entityId with
            | ValueSome state ->
              let speed =
                match derivedStats.TryFindV entityId with
                | ValueSome stats -> float32 stats.MS
                | ValueNone -> 100.0f

              let mtv =
                match frameCollisions.TryGetValue entityId with
                | true, m -> m
                | false, _ -> Vector2.Zero

              let currentPos2d = WorldPosition.toVector2 currentPos

              match state with
              | MovingAlongPath path ->
                currentPaths[entityId] <- path

                match
                  MovementLogic.handleMovingAlongPath currentPos2d path speed mtv
                with
                | MovementLogic.Arrived ->
                  MovementLogic.notifyArrived entityId stateWrite core.EventBus
                  lastVelocities[entityId] <- Vector2.Zero
                  currentPaths.Remove(entityId) |> ignore
                | MovementLogic.WaypointReached remaining ->
                  MovementLogic.notifyWaypointReached
                    entityId
                    remaining
                    stateWrite
                    core.EventBus
                | MovementLogic.Moving finalVel ->
                  let lastVel =
                    match lastVelocities.TryGetValue entityId with
                    | true, v -> v
                    | false, _ -> Vector2.Zero

                  lastVelocities[entityId] <-
                    MovementLogic.notifyVelocityChange
                      entityId
                      finalVel
                      lastVel
                      stateWrite

              | MovingTo target ->
                match
                  MovementLogic.handleMovingTo currentPos2d target speed mtv
                with
                | MovementLogic.Arrived ->
                  MovementLogic.notifyArrived entityId stateWrite core.EventBus
                  lastVelocities[entityId] <- Vector2.Zero
                  currentPaths.Remove(entityId) |> ignore
                | MovementLogic.Moving finalVel ->
                  let lastVel =
                    match lastVelocities.TryGetValue entityId with
                    | true, v -> v
                    | false, _ -> Vector2.Zero

                  lastVelocities[entityId] <-
                    MovementLogic.notifyVelocityChange
                      entityId
                      finalVel
                      lastVel
                      stateWrite
                | _ -> ()

              | Idle ->
                if
                  lastVelocities.ContainsKey entityId
                  && lastVelocities[entityId] <> Vector2.Zero
                then
                  lastVelocities[entityId] <-
                    MovementLogic.notifyVelocityChange
                      entityId
                      Vector2.Zero
                      lastVelocities[entityId]
                      stateWrite

                currentPaths.Remove(entityId) |> ignore
            | ValueNone -> ()

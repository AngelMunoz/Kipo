namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Events
open Pomo.Core.Systems.Systems
open Pomo.Core.Domain.Core // For Constants.AI.WaypointReachedThreshold
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
        core.EventBus.GetObservableFor<SystemCommunications.CollisionEvents>()
        |> Observable.subscribe(fun e -> collisionEvents.Enqueue(e))


    override this.Dispose(disposing) =
      if disposing then
        sub.Dispose()

      base.Dispose(disposing)

    override this.Update(gameTime) =
      let snapshot = gameplay.Projections.ComputeMovementSnapshot()

      // Process collisions
      let mutable collisionEvent =
        Unchecked.defaultof<SystemCommunications.CollisionEvents>

      let frameCollisions =
        System.Collections.Generic.Dictionary<Guid<EntityId>, Vector2>()

      while collisionEvents.TryDequeue(&collisionEvent) do
        match collisionEvent with
        | SystemCommunications.CollisionEvents.MapObjectCollision(eId, _, mtv) when
          eId <> playerId
          ->
          let allPositions = snapshot.Positions
          // Apply MTV to current position
          match allPositions |> HashMap.tryFindV eId with
          | ValueSome currentPos ->
            let mtv =
              MovementLogic.resolveCollision eId currentPos mtv core.EventBus

            match frameCollisions.TryGetValue eId with
            | true, existing -> frameCollisions[eId] <- existing + mtv
            | false, _ -> frameCollisions[eId] <- mtv
          | ValueNone -> ()
        | _ -> ()

      let movementStates = movementStates |> AMap.force

      let positions =
        snapshot.Positions |> HashMap.filter(fun id _ -> id <> playerId)

      let derivedStats = derivedStats |> AMap.force

      // We iterate over all entities that have a movement state
      for entityId, state in movementStates do
        match positions.TryFindV entityId with
        | ValueSome currentPos ->
          let speed =
            match derivedStats.TryFindV entityId with
            | ValueSome stats -> float32 stats.MS
            | ValueNone -> 100.0f // Default speed if no stats

          match state with
          | MovingAlongPath path ->
            currentPaths[entityId] <- path // Update local path state

            match
              MovementLogic.handleMovingAlongPath
                currentPos
                path
                speed
                (match frameCollisions.TryGetValue entityId with
                 | true, mtv -> mtv
                 | false, _ -> Vector2.Zero)
            with
            | MovementLogic.Arrived ->
              MovementLogic.notifyArrived entityId core.EventBus
              lastVelocities[entityId] <- Vector2.Zero
              currentPaths.Remove(entityId) |> ignore
            | MovementLogic.WaypointReached remainingWaypoints ->
              MovementLogic.notifyWaypointReached
                entityId
                remainingWaypoints
                core.EventBus
            | MovementLogic.Moving finalVelocity ->
              let lastVel =
                match lastVelocities.TryGetValue entityId with
                | true, v -> v
                | false, _ -> Vector2.Zero

              lastVelocities[entityId] <-
                MovementLogic.notifyVelocityChange
                  entityId
                  finalVelocity
                  lastVel
                  core.EventBus

          | MovingTo target ->
            match
              MovementLogic.handleMovingTo
                currentPos
                target
                speed
                (match frameCollisions.TryGetValue entityId with
                 | true, mtv -> mtv
                 | false, _ -> Vector2.Zero)
            with
            | MovementLogic.Arrived ->
              MovementLogic.notifyArrived entityId core.EventBus
              lastVelocities[entityId] <- Vector2.Zero
              currentPaths.Remove(entityId) |> ignore // Clear any residual path
            | MovementLogic.Moving finalVelocity ->
              let lastVel =
                match lastVelocities.TryGetValue entityId with
                | true, v -> v
                | false, _ -> Vector2.Zero

              lastVelocities[entityId] <-
                MovementLogic.notifyVelocityChange
                  entityId
                  finalVelocity
                  lastVel
                  core.EventBus
            | _ -> () // Should not happen for MovingTo
          | Idle ->
            // Ensure velocity is zero if idle (and we were previously moving)
            if
              lastVelocities.ContainsKey entityId
              && lastVelocities[entityId] <> Vector2.Zero
            then
              lastVelocities[entityId] <-
                MovementLogic.notifyVelocityChange
                  entityId
                  Vector2.Zero
                  lastVelocities[entityId]
                  core.EventBus

            currentPaths.Remove(entityId) |> ignore // Clear any residual path
        | ValueNone -> ()

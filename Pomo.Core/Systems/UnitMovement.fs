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

      for (scenarioId, _) in scenarios do
        let snapshot = gameplay.Projections.ComputeMovementSnapshot(scenarioId)

        // Process collisions for this scenario
        // Note: collisionEvents is a concurrent queue, so we can't easily peek/filter without dequeuing.
        // But we can dequeue all and re-enqueue or process?
        // Actually, collision events are global. We should process them all once?
        // But we need snapshot to resolve collision.
        // If we process all collisions once, we need to look up scenario for each event.
        // Let's do that separately.
        ()

        // Process movement for entities in this scenario
        let positions =
          snapshot.Positions |> HashMap.filter(fun id _ -> id <> playerId)

        for (entityId, currentPos) in positions do
          match movementStates.TryFindV entityId with
          | ValueSome state ->
            let speed =
              match derivedStats.TryFindV entityId with
              | ValueSome stats -> float32 stats.MS
              | ValueNone -> 100.0f

            // Retrieve accumulated MTV for this entity from the global collision processing (see below)
            // We need to process collisions before movement.
            // So let's move collision processing to the top and use a map of EntityId -> MTV.
            ()
          | ValueNone -> ()

      // Refactored Update Logic:
      // 1. Process all collision events and build a map of MTVs.
      //    To do this, we need positions. But positions depend on scenario.
      //    So we need to know the scenario of the entity in the collision event.
      //    We can look it up in World.EntityScenario.

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
            // We need the position to resolve collision (apply MTV).
            // We can get it from the snapshot of that scenario.
            // But we don't want to compute snapshot for every event.
            // Maybe we can just accumulate MTVs and apply them later?
            // MovementLogic.resolveCollision takes currentPos.
            // It adds MTV to position and publishes PositionChanged?
            // No, resolveCollision returns the MTV to apply.
            // Wait, MovementLogic.resolveCollision signature:
            // entityId -> currentPos -> mtv -> eventBus -> Vector2 (applied mtv)
            // It publishes PositionChanged!
            // So we need currentPos.

            // Optimization: Cache snapshots?
            // Projections.ComputeMovementSnapshot is likely cached per frame if using AVal?
            // No, it's `unit -> MovementSnapshot`. It forces AVal.
            // So calling it multiple times is okay if AVal is cached.
            // `world.Positions` is AVal.
            // `calculateMovementSnapshot` is pure.
            // So we should compute it once per scenario.

            let snapshot =
              gameplay.Projections.ComputeMovementSnapshot(scenarioId)

            match snapshot.Positions.TryFindV eId with
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

        let positions =
          snapshot.Positions |> HashMap.filter(fun id _ -> id <> playerId)

        for (entityId, currentPos) in positions do
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

            match state with
            | MovingAlongPath path ->
              currentPaths[entityId] <- path

              match
                MovementLogic.handleMovingAlongPath currentPos path speed mtv
              with
              | MovementLogic.Arrived ->
                MovementLogic.notifyArrived entityId core.EventBus
                lastVelocities[entityId] <- Vector2.Zero
                currentPaths.Remove(entityId) |> ignore
              | MovementLogic.WaypointReached remaining ->
                MovementLogic.notifyWaypointReached
                  entityId
                  remaining
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
                    core.EventBus

            | MovingTo target ->
              match
                MovementLogic.handleMovingTo currentPos target speed mtv
              with
              | MovementLogic.Arrived ->
                MovementLogic.notifyArrived entityId core.EventBus
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
                    core.EventBus
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
                    core.EventBus

              currentPaths.Remove(entityId) |> ignore
          | ValueNone -> ()

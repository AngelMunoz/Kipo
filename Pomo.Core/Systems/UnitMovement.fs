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

    // Local state for path following
    let mutable currentPaths =
      System.Collections.Generic.Dictionary<Guid<EntityId>, Vector2 list>()

    let movementStates =
      core.World.MovementStates |> AMap.filter(fun id _ -> id <> playerId)



    let derivedStats =
      gameplay.Projections.DerivedStats
      |> AMap.filter(fun id _ -> id <> playerId)

    override val Kind = Movement with get

    override this.Update(gameTime) =
      let scenarios = core.World.Scenarios |> AMap.force
      let movementStates = movementStates |> AMap.force
      let derivedStats = derivedStats |> AMap.force
      let entityScenarios = core.World.EntityScenario |> AMap.force

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

              let mtv = Vector2.Zero

              let currentPos2d = WorldPosition.toVector2 currentPos

              match state with
              | MovingAlongPath path ->
                currentPaths[entityId] <- path

                match
                  MovementLogic.handleMovingAlongPath
                    currentPos2d
                    path
                    speed
                    mtv
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

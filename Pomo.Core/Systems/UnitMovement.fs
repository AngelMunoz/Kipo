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
open Pomo.Core.Projections

module UnitMovement =

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type UnitMovementSystem
    (game: Game, env: PomoEnvironment, playerId: Guid<EntityId>) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let stateWrite = core.StateWrite

    let lastVelocities =
      System.Collections.Generic.Dictionary<Guid<EntityId>, Vector2>()

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

      for (scenarioId, _) in scenarios do
        let snapshot =
          gameplay.Projections.ComputeMovement3DSnapshot(scenarioId)

        for KeyValue(entityId, currentPos) in snapshot.Positions do
          if entityId <> playerId then
            match movementStates.TryFindV entityId with
            | ValueSome state ->
              let speed =
                match derivedStats.TryFindV entityId with
                | ValueSome stats -> float32 stats.MS
                | ValueNone -> 100.0f

              match state with
              | MovingAlongPath path ->
                let path3D = path |> List.map WorldPosition.fromVector2

                match
                  MovementLogic3D.handleMovingAlongPath3D
                    currentPos
                    path3D
                    speed
                with
                | MovementLogic3D.Arrived3D ->
                  MovementLogic3D.notifyArrived3D
                    entityId
                    stateWrite
                    core.EventBus

                  lastVelocities[entityId] <- Vector2.Zero
                | MovementLogic3D.WaypointReached3D remaining ->
                  MovementLogic3D.notifyWaypointReached3D
                    entityId
                    remaining
                    stateWrite
                    core.EventBus
                | MovementLogic3D.Moving3D finalVel ->
                  let lastVel =
                    match lastVelocities.TryGetValue entityId with
                    | true, v -> v
                    | false, _ -> Vector2.Zero

                  lastVelocities[entityId] <-
                    MovementLogic3D.notifyVelocityChange3D
                      entityId
                      finalVel
                      lastVel
                      stateWrite

              | MovingTo target ->
                let target3D = WorldPosition.fromVector2 target

                match
                  MovementLogic3D.handleMovingTo3D currentPos target3D speed
                with
                | MovementLogic3D.Arrived3D ->
                  MovementLogic3D.notifyArrived3D
                    entityId
                    stateWrite
                    core.EventBus

                  lastVelocities[entityId] <- Vector2.Zero
                | MovementLogic3D.Moving3D finalVel ->
                  let lastVel =
                    match lastVelocities.TryGetValue entityId with
                    | true, v -> v
                    | false, _ -> Vector2.Zero

                  lastVelocities[entityId] <-
                    MovementLogic3D.notifyVelocityChange3D
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
                    MovementLogic3D.notifyVelocityChange3D
                      entityId
                      Vector2.Zero
                      lastVelocities[entityId]
                      stateWrite
            | ValueNone -> ()

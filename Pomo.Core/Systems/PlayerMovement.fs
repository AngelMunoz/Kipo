namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Action
open Pomo.Core.Systems.Systems
open Pomo.Core.Algorithms
open Pomo.Core.Systems

module PlayerMovement =

  module Projections =
    let PlayerVelocity
      (world: World)
      (playerId: Guid<EntityId>)
      (speed: float32)
      =
      world.GameActionStates
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue HashMap.empty)
      |> AVal.map(
        HashMap.fold
          (fun acc action _ ->
            let mutable move = Vector2.Zero

            match action with
            | MoveUp -> move <- move - Vector2.UnitY
            | MoveDown -> move <- move + Vector2.UnitY
            | MoveLeft -> move <- move - Vector2.UnitX
            | MoveRight -> move <- move + Vector2.UnitX
            | _ -> ()

            if move.LengthSquared() > 0.0f then
              acc + Vector2.Normalize move * speed
            else
              Vector2.Zero)
          Vector2.Zero
      )

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns
  open System.Collections.Generic

  type PlayerMovementSystem
    (game: Game, env: PomoEnvironment, playerId: Guid<EntityId>) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let stateWrite = core.StateWrite

    let speed = 100.0f

    // Use the projection
    let velocity = Projections.PlayerVelocity core.World playerId speed

    let playerCombatStatuses =
      gameplay.Projections.CombatStatuses
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue IndexList.empty)


    let movementSpeed =
      gameplay.Projections.DerivedStats
      |> AMap.tryFind playerId
      |> AVal.map(Option.map _.MS >> Option.defaultValue 100)

    let movementState = core.World.MovementStates |> AMap.tryFind playerId

    // This is a simple, non-adaptive way to track the last published value.
    // A mutable variable scoped to the system is acceptable for this kind of internal bookkeeping.
    let mutable lastVelocity = Vector2.Zero

    let mutable sub: IDisposable = null

    let collisionEvents =
      System.Collections.Concurrent.ConcurrentQueue<
        SystemCommunications.CollisionEvents
       >()

    override _.Initialize() =
      base.Initialize()

      sub <-
        core.EventBus.Observable
        |> Observable.choose(fun e ->
          match e with
          | GameEvent.Collision(collision) -> Some collision
          | _ -> None)
        |> Observable.subscribe(fun e -> collisionEvents.Enqueue(e))

    override _.Dispose(disposing) =
      if disposing then
        sub.Dispose()

      base.Dispose(disposing)

    override this.Update _ =
      let entityScenarios = gameplay.Projections.EntityScenarios |> AMap.force

      let snapshot =
        match entityScenarios |> HashMap.tryFindV playerId with
        | ValueSome scenarioId ->
          gameplay.Projections.ComputeMovementSnapshot(scenarioId)
        | ValueNone ->
            {
              Positions = Dictionary()
              SpatialGrid = Dictionary()
              Rotations = Dictionary()
              ModelConfigIds = Dictionary()
            }

      // Process collisions
      let mutable accumulatedMtv = Vector2.Zero

      let mutable collisionEvent =
        Unchecked.defaultof<SystemCommunications.CollisionEvents>

      while collisionEvents.TryDequeue(&collisionEvent) do
        match collisionEvent with
        | SystemCommunications.CollisionEvents.MapObjectCollision(eId, _, mtv) when
          eId = playerId
          ->
          let currentPos =
            snapshot.Positions
            |> Dictionary.tryFindV playerId
            |> ValueOption.defaultValue Vector2.Zero

          accumulatedMtv <- accumulatedMtv + mtv
        | _ -> ()

      let currentVelocity = velocity |> AVal.force
      let statuses = playerCombatStatuses |> AVal.force

      let isStunned = statuses |> IndexList.exists(fun _ s -> s.IsStunned)
      let isRooted = statuses |> IndexList.exists(fun _ s -> s.IsRooted)

      if isStunned || isRooted then
        // If stunned or rooted, ensure velocity is zero.
        if lastVelocity <> Vector2.Zero then
          lastVelocity <-
            MovementLogic.notifyVelocityChange
              playerId
              Vector2.Zero
              lastVelocity
              stateWrite
      else
        let movementState = movementState |> AVal.force

        let position =
          snapshot.Positions
          |> Dictionary.tryFindV playerId
          |> ValueOption.defaultValue Vector2.Zero

        let movementSpeed = movementSpeed |> AVal.force

        match movementState with
        | Some(MovingTo destination) ->
          match
            MovementLogic.handleMovingTo
              position
              destination
              (float32 movementSpeed)
              accumulatedMtv
          with
          | MovementLogic.Arrived ->
            MovementLogic.notifyArrived playerId stateWrite core.EventBus
          | MovementLogic.Moving finalVelocity ->
            lastVelocity <-
              MovementLogic.notifyVelocityChange
                playerId
                finalVelocity
                lastVelocity
                stateWrite
          | _ -> () // Should not happen for MovingTo

        | Some(MovingAlongPath path) ->
          match
            MovementLogic.handleMovingAlongPath
              position
              path
              (float32 movementSpeed)
              accumulatedMtv
          with
          | MovementLogic.Arrived ->
            MovementLogic.notifyArrived playerId stateWrite core.EventBus
            lastVelocity <- Vector2.Zero
          | MovementLogic.WaypointReached remainingWaypoints ->
            MovementLogic.notifyWaypointReached
              playerId
              remainingWaypoints
              stateWrite
              core.EventBus
          | MovementLogic.Moving finalVelocity ->
            lastVelocity <-
              MovementLogic.notifyVelocityChange
                playerId
                finalVelocity
                lastVelocity
                stateWrite

        | Some Idle ->
          let mutable targetVelocity = currentVelocity

          // Apply collision sliding to Input velocity
          let targetVelocity =
            Physics.applyCollisionSliding targetVelocity accumulatedMtv

          lastVelocity <-
            MovementLogic.notifyVelocityChange
              playerId
              targetVelocity
              lastVelocity
              stateWrite
        | None -> ()

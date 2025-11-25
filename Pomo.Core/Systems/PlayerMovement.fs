namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

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
      let actionStates =
        world.GameActionStates
        |> AMap.tryFind playerId
        |> AVal.map(Option.defaultValue HashMap.empty)
        |> AMap.ofAVal

      actionStates
      |> AMap.fold
        (fun acc action _ ->
          let mutable move = Vector2.Zero

          match action with
          | MoveUp -> move <- move - Vector2.UnitY
          | MoveDown -> move <- move + Vector2.UnitY
          | MoveLeft -> move <- move - Vector2.UnitX
          | MoveRight -> move <- move + Vector2.UnitX
          | _ -> ()

          if move.LengthSquared() > 0.0f then
            acc + Vector2.Normalize(move) * speed
          else
            Vector2.Zero)
        Vector2.Zero

  type PlayerMovementSystem(game: Game, playerId: Guid<EntityId>) as this =
    inherit GameSystem(game)

    let speed = 100.0f

    // Use the projection
    let velocity = Projections.PlayerVelocity this.World playerId speed

    let playerCombatStatuses =
      this.Projections.CombatStatuses
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue IndexList.empty)

    let position =
      this.Projections.UpdatedPositions
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue Vector2.Zero)

    let movementSpeed =
      this.Projections.DerivedStats
      |> AMap.tryFind playerId
      |> AVal.map(Option.map _.MS >> Option.defaultValue 100)

    let movementState = this.World.MovementStates |> AMap.tryFind playerId

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
        this.EventBus.GetObservableFor<SystemCommunications.CollisionEvents>()
        |> Observable.subscribe(fun e -> collisionEvents.Enqueue(e))

    override _.Dispose(disposing) =
      if disposing then
        sub.Dispose()

      base.Dispose(disposing)

    override this.Update _ =
      // Process collisions
      let mutable accumulatedMtv = Vector2.Zero

      let mutable collisionEvent =
        Unchecked.defaultof<SystemCommunications.CollisionEvents>

      while collisionEvents.TryDequeue(&collisionEvent) do
        match collisionEvent with
        | SystemCommunications.CollisionEvents.MapObjectCollision(eId, _, mtv) when
          eId = playerId
          ->
          // Apply MTV to current position
          let currentPos = position |> AVal.force
          let newPos = currentPos + mtv

          this.EventBus.Publish(
            Physics(PositionChanged struct (playerId, newPos))
          )

          accumulatedMtv <- accumulatedMtv + mtv
        | _ -> ()

      let currentVelocity = velocity |> AVal.force
      let statuses = playerCombatStatuses |> AVal.force

      let isStunned = statuses |> IndexList.exists(fun _ s -> s.IsStunned)
      let isRooted = statuses |> IndexList.exists(fun _ s -> s.IsRooted)

      if isStunned || isRooted then
        // If stunned or rooted, ensure velocity is zero.
        if lastVelocity <> Vector2.Zero then
          this.EventBus.Publish(
            Physics(VelocityChanged struct (playerId, Vector2.Zero))
          )

          lastVelocity <- Vector2.Zero
      else
        let movementState = movementState |> AVal.force

        let position = position |> AVal.force

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
            // We've arrived. Stop moving and return to Idle state.
            this.EventBus.Publish(
              Physics(VelocityChanged struct (playerId, Vector2.Zero))
            )

            this.EventBus.Publish(
              Physics(MovementStateChanged struct (playerId, Idle))
            )
          | MovementLogic.Moving finalVelocity ->
            if finalVelocity <> lastVelocity then
              this.EventBus.Publish(
                Physics(VelocityChanged struct (playerId, finalVelocity))
              )

            lastVelocity <- finalVelocity
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
            // Path finished
            this.EventBus.Publish(
              Physics(VelocityChanged struct (playerId, Vector2.Zero))
            )

            this.EventBus.Publish(
              Physics(MovementStateChanged struct (playerId, Idle))
            )

            lastVelocity <- Vector2.Zero
          | MovementLogic.WaypointReached remainingWaypoints ->
            // Waypoint reached, move to next
            this.EventBus.Publish(
              Physics(
                MovementStateChanged
                  struct (playerId, MovingAlongPath remainingWaypoints)
              )
            )
          | MovementLogic.Moving finalVelocity ->
            if finalVelocity <> lastVelocity then
              this.EventBus.Publish(
                Physics(VelocityChanged struct (playerId, finalVelocity))
              )

            lastVelocity <- finalVelocity

        | Some Idle ->
          let mutable targetVelocity = currentVelocity

          // Apply collision sliding to Input velocity
          let targetVelocity =
            Physics.applyCollisionSliding targetVelocity accumulatedMtv

          if targetVelocity <> lastVelocity then
            this.EventBus.Publish(
              Physics(VelocityChanged struct (playerId, targetVelocity))
            )

            lastVelocity <- targetVelocity
        | None -> ()

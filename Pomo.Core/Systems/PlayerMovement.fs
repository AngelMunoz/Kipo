namespace Pomo.Core.Systems

open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain.Units
open Pomo.Core.Domain
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Core

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
      Pomo.Core.Projections.CalculateCombatStatuses this.World
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue IndexList.empty)

    let position =
      this.World.Positions
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue Vector2.Zero)

    let movementSpeed =
      this.World.DerivedStats
      |> AMap.tryFind playerId
      |> AVal.map(Option.map _.MS >> Option.defaultValue 100)

    let movementState = this.World.MovementStates |> AMap.tryFind playerId

    // This is a simple, non-adaptive way to track the last published value.
    // A mutable variable scoped to the system is acceptable for this kind of internal bookkeeping.
    let mutable lastVelocity = Vector2.Zero

    override this.Update _ =
      let currentVelocity = velocity |> AVal.force
      let statuses = playerCombatStatuses |> AVal.force

      let isStunned = statuses |> IndexList.exists(fun _ s -> s.IsStunned)
      let isRooted = statuses |> IndexList.exists(fun _ s -> s.IsRooted)

      if isStunned || isRooted then
        // If stunned or rooted, ensure velocity is zero.
        if lastVelocity <> Vector2.Zero then
          this.EventBus.Publish(
            StateChangeEvent.Physics(
              PhysicsEvents.VelocityChanged struct (playerId, Vector2.Zero)
            )
          )

          lastVelocity <- Vector2.Zero
      else
        let movementState = movementState |> AVal.force

        let position = position |> AVal.force

        let movementSpeed = movementSpeed |> AVal.force

        match movementState with
        | Some(MovingTo destination) ->
          let distance = Vector2.Distance(position, destination)
          let threshold = 2.0f // Close enough

          if distance < threshold then
            // We've arrived. Stop moving and return to Idle state.
            this.EventBus.Publish(
              StateChangeEvent.Physics(
                PhysicsEvents.VelocityChanged struct (playerId, Vector2.Zero)
              )
            )

            this.EventBus.Publish(
              StateChangeEvent.Physics(
                PhysicsEvents.MovementStateChanged struct (playerId, Idle)
              )
            )
          else
            // Still moving towards the destination.
            let direction = Vector2.Normalize(destination - position)
            let adjustedVelocity = direction * float32 movementSpeed

            if currentVelocity <> adjustedVelocity then
              this.EventBus.Publish(
                StateChangeEvent.Physics(
                  PhysicsEvents.VelocityChanged
                    struct (playerId, adjustedVelocity)
                )
              )

            lastVelocity <- adjustedVelocity

        | Some Idle ->
          if currentVelocity <> lastVelocity then
            this.EventBus.Publish(
              StateChangeEvent.Physics(
                PhysicsEvents.VelocityChanged struct (playerId, currentVelocity)
              )
            )

            lastVelocity <- currentVelocity
        | None -> ()

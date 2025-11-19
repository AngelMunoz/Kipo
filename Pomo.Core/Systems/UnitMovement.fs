namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Events
open Pomo.Core.Systems.Systems

module UnitMovement =

  type UnitMovementSystem(game: Game, playerId: Guid<EntityId>) =
    inherit GameSystem(game)

    // Mutable state to track last known velocities to avoid spamming events
    let lastVelocities =
      System.Collections.Generic.Dictionary<Guid<EntityId>, Vector2>()

    override val Kind = Movement with get

    override this.Update(gameTime) =
      let movementStates =
        this.World.MovementStates
        |> AMap.filter(fun id _ -> id <> playerId)
        |> AMap.force

      let positions =
        this.World.Positions
        |> AMap.filter(fun id _ -> id <> playerId)
        |> AMap.force

      let derivedStats =
        this.Projections.DerivedStats
        |> AMap.filter(fun id _ -> id <> playerId)
        |> AMap.force

      // We iterate over all entities that have a movement state
      for entityId, state in movementStates do
        match state with
        | MovingTo target ->
          match positions.TryFindV entityId with
          | ValueSome currentPos ->
            let speed =
              match derivedStats.TryFindV entityId with
              | ValueSome stats -> float32 stats.MS
              | ValueNone -> 100.0f // Default speed if no stats

            let distance = Vector2.Distance(currentPos, target)
            let threshold = 2.0f

            if distance < threshold then
              // Arrived
              this.EventBus.Publish(
                StateChangeEvent.Physics(
                  PhysicsEvents.VelocityChanged struct (entityId, Vector2.Zero)
                )
              )

              this.EventBus.Publish(
                StateChangeEvent.Physics(
                  PhysicsEvents.MovementStateChanged struct (entityId, Idle)
                )
              )

              lastVelocities[entityId] <- Vector2.Zero
            else
              // Move towards target
              let direction = Vector2.Normalize(target - currentPos)
              let velocity = direction * speed

              let lastVel =
                match lastVelocities.TryGetValue entityId with
                | true, v -> v
                | false, _ -> Vector2.Zero

              if velocity <> lastVel then
                this.EventBus.Publish(
                  StateChangeEvent.Physics(
                    PhysicsEvents.VelocityChanged struct (entityId, velocity)
                  )
                )

                lastVelocities[entityId] <- velocity
          | _ -> () // No position, can't move
        | Idle ->
          // Ensure velocity is zero if idle (and we were previously moving)
          if
            lastVelocities.ContainsKey entityId
            && lastVelocities[entityId] <> Vector2.Zero
          then
            this.EventBus.Publish(
              StateChangeEvent.Physics(
                PhysicsEvents.VelocityChanged struct (entityId, Vector2.Zero)
              )
            )

            lastVelocities[entityId] <- Vector2.Zero

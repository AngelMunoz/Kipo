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

  type UnitMovementSystem(game: Game, playerId: Guid<EntityId>) as this =
    inherit GameSystem(game)

    // Mutable state to track last known velocities to avoid spamming events
    let lastVelocities =
      System.Collections.Generic.Dictionary<Guid<EntityId>, Vector2>()

    let mutable sub: IDisposable = null

    let collisionEvents =
      System.Collections.Concurrent.ConcurrentQueue<
        SystemCommunications.CollisionEvents
       >()

    let movementStates =
      this.World.MovementStates |> AMap.filter(fun id _ -> id <> playerId)

    let positions =
      this.World.Positions |> AMap.filter(fun id _ -> id <> playerId)

    let derivedStats =
      this.Projections.DerivedStats |> AMap.filter(fun id _ -> id <> playerId)

    override val Kind = Movement with get

    override this.Initialize() =
      base.Initialize()

      sub <-
        this.EventBus.GetObservableFor<SystemCommunications.CollisionEvents>()
        |> Observable.subscribe(fun e -> collisionEvents.Enqueue(e))


    override this.Dispose(disposing) =
      if disposing then
        sub.Dispose()

      base.Dispose(disposing)

    override this.Update(gameTime) =
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
          let allPositions = this.World.Positions |> AMap.force
          // Apply MTV to current position
          match allPositions |> HashMap.tryFindV eId with
          | ValueSome currentPos ->
            let newPos = currentPos + mtv

            this.EventBus.Publish(Physics(PositionChanged struct (eId, newPos)))

            match frameCollisions.TryGetValue eId with
            | true, existing -> frameCollisions[eId] <- existing + mtv
            | false, _ -> frameCollisions[eId] <- mtv
          | ValueNone -> ()
        | _ -> ()

      let movementStates = movementStates |> AMap.force

      let positions = positions |> AMap.force

      let derivedStats = derivedStats |> AMap.force

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
                Physics(VelocityChanged struct (entityId, Vector2.Zero))
              )

              this.EventBus.Publish(
                Physics(MovementStateChanged struct (entityId, Idle))
              )

              lastVelocities[entityId] <- Vector2.Zero
            else
              // Move towards target
              let direction = Vector2.Normalize(target - currentPos)
              let velocity = direction * speed

              // Apply collision sliding
              let finalVelocity =
                match frameCollisions.TryGetValue entityId with
                | true, mtv when mtv <> Vector2.Zero ->
                  let normal = Vector2.Normalize(mtv)

                  if Vector2.Dot(velocity, normal) < 0.0f then
                    velocity - normal * Vector2.Dot(velocity, normal)
                  else
                    velocity
                | _ -> velocity

              let lastVel =
                match lastVelocities.TryGetValue entityId with
                | true, v -> v
                | false, _ -> Vector2.Zero

              if finalVelocity <> lastVel then
                this.EventBus.Publish(
                  Physics(VelocityChanged struct (entityId, finalVelocity))
                )

                lastVelocities[entityId] <- finalVelocity
          | _ -> () // No position, can't move
        | Idle ->
          // Ensure velocity is zero if idle (and we were previously moving)
          if
            lastVelocities.ContainsKey entityId
            && lastVelocities[entityId] <> Vector2.Zero
          then
            this.EventBus.Publish(
              Physics(VelocityChanged struct (entityId, Vector2.Zero))
            )

            lastVelocities[entityId] <- Vector2.Zero

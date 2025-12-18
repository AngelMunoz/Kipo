namespace Pomo.Core.Systems

open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Units
open Pomo.Core.Algorithms
open Pomo.Core.EventBus

module MovementLogic =

  /// Result of a movement update
  [<Struct>]
  type MovementResult =
    | Arrived
    | Moving of velocity: Vector2
    | WaypointReached of remainingPath: Vector2 list

  let notifyArrived (entityId: Guid<EntityId>) (eventBus: EventBus) =
    eventBus.Publish(
      GameEvent.State(Physics(VelocityChanged struct (entityId, Vector2.Zero)))
    )

    eventBus.Publish(
      GameEvent.State(Physics(MovementStateChanged struct (entityId, Idle)))
    )

  let notifyWaypointReached
    (entityId: Guid<EntityId>)
    (remainingPath: Vector2 list)
    (eventBus: EventBus)
    =
    eventBus.Publish(
      GameEvent.State(
        Physics(
          MovementStateChanged struct (entityId, MovingAlongPath remainingPath)
        )
      )
    )

  let notifyVelocityChange
    (entityId: Guid<EntityId>)
    (newVelocity: Vector2)
    (lastVelocity: Vector2)
    (eventBus: EventBus)
    =
    if newVelocity <> lastVelocity then
      eventBus.Publish(
        GameEvent.State(Physics(VelocityChanged struct (entityId, newVelocity)))
      )

    newVelocity

  let handleMovingTo
    (currentPos: Vector2)
    (target: Vector2)
    (speed: float32)
    (accumulatedMtv: Vector2)
    =
    let distance = Vector2.Distance(currentPos, target)

    if distance < Physics.ArrivalThreshold then
      Arrived
    else
      // Still moving towards the destination.
      let direction = Vector2.Normalize(target - currentPos)
      let adjustedVelocity = direction * speed

      // Apply collision sliding
      let finalVelocity =
        Physics.applyCollisionSliding adjustedVelocity accumulatedMtv

      Moving finalVelocity

  let handleMovingAlongPath
    (currentPos: Vector2)
    (path: Vector2 list)
    (speed: float32)
    (accumulatedMtv: Vector2)
    =
    match path with
    | [] -> Arrived
    | currentWaypoint :: remainingWaypoints ->
      let distance = Vector2.Distance(currentPos, currentWaypoint)

      let threshold =
        Pomo.Core.Domain.Core.Constants.AI.WaypointReachedThreshold

      if distance < threshold then
        // Waypoint reached
        WaypointReached remainingWaypoints
      else
        // Move towards current waypoint
        let direction = Vector2.Normalize(currentWaypoint - currentPos)
        let adjustedVelocity = direction * speed

        // Apply collision sliding
        let finalVelocity =
          Physics.applyCollisionSliding adjustedVelocity accumulatedMtv

        Moving finalVelocity

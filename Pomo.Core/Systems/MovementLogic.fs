namespace Pomo.Core.Systems

open Microsoft.Xna.Framework
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Units
open Pomo.Core.Algorithms

module MovementLogic =

  /// Result of a movement update
  [<Struct>]
  type MovementResult =
    | Arrived
    | Moving of velocity: Vector2
    | WaypointReached of remainingPath: Vector2 list

  let handleMovingTo
    (currentPos: Vector2)
    (target: Vector2)
    (speed: float32)
    (accumulatedMtv: Vector2)
    =
    let distance = Vector2.Distance(currentPos, target)
    let threshold = 2.0f // Close enough

    if distance < threshold then
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

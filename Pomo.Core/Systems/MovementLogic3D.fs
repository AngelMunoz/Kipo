namespace Pomo.Core.Systems

open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Events
open Pomo.Core.EventBus
open Pomo.Core.Environment

/// Pure functions for 3D movement logic
/// No MTV - collision is handled by BlockCollision in calculate3DSnapshot
module MovementLogic3D =

  /// Result of a 3D movement update
  [<Struct>]
  type MovementResult3D =
    | Arrived3D
    | Moving3D of velocity: Vector3
    | WaypointReached3D of remainingPath: WorldPosition list

  /// Threshold for considering arrived at destination (XZ distance)
  /// Must be reasonable relative to CellSize (64)
  [<Literal>]
  let private ArrivalThreshold = 25.0f

  /// Threshold for considering waypoint reached (XZ distance)
  /// Must be reasonable relative to CellSize (64)
  [<Literal>]
  let private WaypointThreshold = 30.0f

  /// Calculate squared XZ distance (avoids sqrt)
  let inline private distanceSquaredXZ (a: WorldPosition) (b: WorldPosition) =
    let dx = b.X - a.X
    let dz = b.Z - a.Z
    dx * dx + dz * dz

  /// Calculate XZ direction vector (normalized)
  let inline private directionXZ (from: WorldPosition) (target: WorldPosition) =
    let dx = target.X - from.X
    let dz = target.Z - from.Z
    let lenSq = dx * dx + dz * dz

    if lenSq > 0.0001f then
      let len = sqrt lenSq
      Vector2(dx / len, dz / len)
    else
      Vector2.Zero

  /// Handle moving directly to a target position
  let handleMovingTo3D
    (currentPos: WorldPosition)
    (target: WorldPosition)
    (speed: float32)
    : MovementResult3D =
    let distSq = distanceSquaredXZ currentPos target
    let thresholdSq = ArrivalThreshold * ArrivalThreshold

    if distSq < thresholdSq then
      Arrived3D
    else
      let dir = directionXZ currentPos target
      Moving3D(Vector3(dir.X * speed, 0.0f, dir.Y * speed))

  /// Handle moving along a path of waypoints
  let handleMovingAlongPath3D
    (currentPos: WorldPosition)
    (path: WorldPosition list)
    (speed: float32)
    : MovementResult3D =
    match path with
    | [] -> Arrived3D
    | waypoint :: remaining ->
      let distSq = distanceSquaredXZ currentPos waypoint
      let thresholdSq = WaypointThreshold * WaypointThreshold

      if distSq < thresholdSq then
        WaypointReached3D remaining
      else
        let dir = directionXZ currentPos waypoint
        Moving3D(Vector3(dir.X * speed, 0.0f, dir.Y * speed))

  /// Notify that entity has arrived at destination
  let notifyArrived3D
    (entityId: Guid<EntityId>)
    (stateWrite: IStateWriteService)
    (eventBus: EventBus)
    =
    stateWrite.UpdateVelocity(entityId, Vector3.Zero)

    eventBus.Publish(
      GameEvent.State(Physics(MovementStateChanged struct (entityId, Idle)))
    )

  /// Notify velocity change (only publishes if changed)
  let notifyVelocityChange3D
    (entityId: Guid<EntityId>)
    (newVelocity: Vector3)
    (lastVelocity: Vector3)
    (stateWrite: IStateWriteService)
    : Vector3 =
    if newVelocity <> lastVelocity then
      stateWrite.UpdateVelocity(entityId, newVelocity)

    newVelocity

  /// Notify waypoint reached with remaining path
  let notifyWaypointReached3D
    (entityId: Guid<EntityId>)
    (remainingPath: WorldPosition list)
    (stateWrite: IStateWriteService)
    (eventBus: EventBus)
    =
    stateWrite.UpdateMovementState(entityId, MovingAlongPath remainingPath)

    eventBus.Publish(
      GameEvent.State(
        Physics(
          MovementStateChanged struct (entityId, MovingAlongPath remainingPath)
        )
      )
    )

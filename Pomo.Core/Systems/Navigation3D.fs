namespace Pomo.Core.Systems

open FSharp.UMX
open FSharp.Data.Adaptive
open FSharp.Control.Reactive
open Microsoft.Xna.Framework
open Pomo.Core
open Pomo.Core.EventBus
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Algorithms
open Pomo.Core.Algorithms.Pathfinding3D
open Pomo.Core.Projections
open Pomo.Core.Environment
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.Core.Constants

/// 3D Navigation system for BlockMap scenarios
/// Uses Pathfinding3D for click-to-move on block-based terrain
module Navigation3D =

  /// Build NavGrid3D from BlockMap
  let inline private buildNavGrid(blockMap: BlockMapDefinition) : NavGrid3D = {
    BlockMap = blockMap
    CellSize = BlockMap.CellSize
  }

  /// Try to get entity's 3D position from snapshot
  let inline private tryGetPosition3D
    (snapshot: Movement3DSnapshot)
    (entityId: Guid<EntityId>)
    : WorldPosition voption =
    snapshot.Positions |> Dictionary.tryFindV entityId

  let inline private clickTo3D
    (blockMap: BlockMapDefinition)
    (currentPos: WorldPosition)
    (target: Vector2)
    : WorldPosition =
    {
      X = target.X
      Y = currentPos.Y
      Z = target.Y
    }

  let inline private isInXZBounds
    (blockMap: BlockMapDefinition)
    (pos: WorldPosition)
    : bool =
    pos.X >= 0f
    && pos.Z >= 0f
    && pos.X < float32 blockMap.Width * BlockMap.CellSize
    && pos.Z < float32 blockMap.Depth * BlockMap.CellSize

  let private trySnapToNearestWalkable
    (blockMap: BlockMapDefinition)
    (navGrid: NavGrid3D)
    (pos: WorldPosition)
    : WorldPosition voption =

    let startCell = Grid.worldToCell navGrid pos

    let inline tryCandidate(cell: GridCell3D) =
      if
        cell.X < 0
        || cell.X >= blockMap.Width
        || cell.Z < 0
        || cell.Z >= blockMap.Depth
      then
        ValueNone
      else
        let candidatePos = Grid.cellToWorld navGrid cell

        if isInXZBounds blockMap candidatePos then
          if Grid.isWalkable navGrid cell then
            ValueSome candidatePos
          else
            ValueNone
        else
          ValueNone

    let maxRadius = 6

    let mutable found = ValueNone
    let mutable r = 0

    while found.IsNone && r <= maxRadius do
      let mutable dx = -r

      while found.IsNone && dx <= r do
        let absDx = if dx < 0 then -dx else dx
        let dzMax = r - absDx
        let dz1 = -dzMax
        let dz2 = dzMax

        let cell1 = {
          X = startCell.X + dx
          Y = startCell.Y
          Z = startCell.Z + dz1
        }

        found <- tryCandidate cell1

        if found.IsNone && dz2 <> dz1 then
          let cell2 = {
            X = startCell.X + dx
            Y = startCell.Y
            Z = startCell.Z + dz2
          }

          found <- tryCandidate cell2

        dx <- dx + 1

      r <- r + 1

    found

  /// Publish path update for entity
  let inline private publishPath
    (stateWrite: IStateWriteService)
    (eventBus: EventBus)
    (entityId: Guid<EntityId>)
    (path: WorldPosition[])
    =
    let pathList = path |> Array.toList
    stateWrite.UpdateMovementState(entityId, MovingAlongPath pathList)

    eventBus.Publish(
      GameEvent.State(
        Physics(
          MovementStateChanged struct (entityId, MovingAlongPath pathList)
        )
      )
    )

  let inline private publishMoveTo
    (stateWrite: IStateWriteService)
    (eventBus: EventBus)
    (entityId: Guid<EntityId>)
    (target: WorldPosition)
    =
    stateWrite.UpdateMovementState(entityId, MovingTo target)

    eventBus.Publish(
      GameEvent.State(
        Physics(MovementStateChanged struct (entityId, MovingTo target))
      )
    )

  /// Publish idle state when no path found
  let inline private publishNoPath
    (stateWrite: IStateWriteService)
    (eventBus: EventBus)
    (entityId: Guid<EntityId>)
    =
    stateWrite.UpdateMovementState(entityId, Idle)

    eventBus.Publish(
      GameEvent.State(Physics(MovementStateChanged struct (entityId, Idle)))
    )

  /// Context needed to process a movement target
  [<Struct>]
  type private MovementContext = {
    EntityId: Guid<EntityId>
    TargetPos: WorldPosition
    CurrentPos: WorldPosition
    BlockMap: BlockMapDefinition
  }

  /// Build context from target event (returns ValueNone if missing data)
  let private buildContext
    (projections: ProjectionService)
    (getBlockMap: Guid<ScenarioId> -> BlockMapDefinition voption)
    (target: SystemCommunications.SetMovementTarget)
    : MovementContext voption =
    let entityId = target.EntityId

    projections.EntityScenarioContexts
    |> AMap.force
    |> HashMap.tryFindV entityId
    |> ValueOption.bind(fun ctx ->
      getBlockMap ctx.ScenarioId
      |> ValueOption.bind(fun blockMap ->
        let snapshot = projections.ComputeMovement3DSnapshot(ctx.ScenarioId)

        tryGetPosition3D snapshot entityId
        |> ValueOption.map(fun currentPos ->
          let targetPos = clickTo3D blockMap currentPos target.Target

          {
            EntityId = entityId
            TargetPos = targetPos
            CurrentPos = currentPos
            BlockMap = blockMap
          })))

  /// Execute pathfinding and publish result
  let private executePathfinding
    (stateWrite: IStateWriteService)
    (eventBus: EventBus)
    (ctx: MovementContext)
    =
    let navGrid = buildNavGrid ctx.BlockMap

    let startPos =
      let startCell = Grid.worldToCell navGrid ctx.CurrentPos

      if Grid.isWalkable navGrid startCell then
        ctx.CurrentPos
      else
        trySnapToNearestWalkable ctx.BlockMap navGrid ctx.CurrentPos
        |> ValueOption.defaultValue ctx.CurrentPos

    let targetPos =
      let targetCell = Grid.worldToCell navGrid ctx.TargetPos

      if Grid.isWalkable navGrid targetCell then
        ctx.TargetPos
      else
        trySnapToNearestWalkable ctx.BlockMap navGrid ctx.TargetPos
        |> ValueOption.defaultValue ctx.TargetPos

    // Check for free movement: close distance + clear line of sight
    let distance = WorldPosition.distance startPos targetPos

    let useDirect =
      distance < Navigation.FreeMovementThreshold
      && AStar.hasLineOfSight navGrid startPos targetPos

    if useDirect then
      publishMoveTo stateWrite eventBus ctx.EntityId targetPos
    else
      match AStar.findPath navGrid startPos targetPos with
      | ValueSome path -> publishPath stateWrite eventBus ctx.EntityId path
      | ValueNone -> publishNoPath stateWrite eventBus ctx.EntityId

  /// Handle a movement target intent
  let private handleMovementTarget
    (projections: ProjectionService)
    (stateWrite: IStateWriteService)
    (eventBus: EventBus)
    (getBlockMap: Guid<ScenarioId> -> BlockMapDefinition voption)
    (target: SystemCommunications.SetMovementTarget)
    =
    buildContext projections getBlockMap target
    |> ValueOption.iter(executePathfinding stateWrite eventBus)

  /// Create Navigation3D listener (factory function returning object expression)
  let create
    (eventBus: EventBus)
    (stateWrite: IStateWriteService)
    (projections: ProjectionService)
    (getBlockMap: Guid<ScenarioId> -> BlockMapDefinition voption)
    : CoreEventListener =
    { new CoreEventListener with
        member _.StartListening() =
          eventBus.Observable
          |> Observable.choose (function
            | GameEvent.Intent(IntentEvent.MovementTarget target) -> Some target
            | _ -> None)
          |> Observable.subscribe(
            handleMovementTarget projections stateWrite eventBus getBlockMap
          )
    }

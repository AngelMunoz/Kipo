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
open Pomo.Core.Projections
open Pomo.Core.Environment

/// 3D Navigation system for BlockMap scenarios
/// Uses Pathfinding3D for click-to-move on block-based terrain
module Navigation3D =

  /// Build NavGrid3D from BlockMap
  let inline private buildNavGrid
    (blockMap: BlockMapDefinition)
    : Pathfinding3D.NavGrid3D =
    {
      BlockMap = blockMap
      CellSize = CellSize
    }

  /// Try to get entity's 3D position from snapshot
  let inline private tryGetPosition3D
    (snapshot: Movement3DSnapshot)
    (entityId: Guid<EntityId>)
    : WorldPosition voption =
    snapshot.Positions |> Dictionary.tryFindV entityId

  /// Convert 2D click target to 3D position (Y=0, will be adjusted by collision)
  let inline private clickTo3D(target: Vector2) : WorldPosition = {
    X = target.X
    Y = 0f
    Z = target.Y
  }

  /// Publish path update for entity
  let inline private publishPath
    (stateWrite: IStateWriteService)
    (eventBus: EventBus)
    (entityId: Guid<EntityId>)
    (path: WorldPosition list)
    =
    // Convert to Vector2 path for current MovementState compatibility
    let path2D = path |> List.map(fun p -> Vector2(p.X, p.Z))
    stateWrite.UpdateMovementState(entityId, MovingAlongPath path2D)

    eventBus.Publish(
      GameEvent.State(
        Physics(MovementStateChanged struct (entityId, MovingAlongPath path2D))
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
    let targetPos = clickTo3D target.Target

    projections.EntityScenarioContexts
    |> AMap.force
    |> HashMap.tryFindV entityId
    |> ValueOption.bind(fun ctx ->
      getBlockMap ctx.ScenarioId
      |> ValueOption.bind(fun blockMap ->
        let snapshot = projections.ComputeMovement3DSnapshot(ctx.ScenarioId)

        tryGetPosition3D snapshot entityId
        |> ValueOption.map(fun currentPos -> {
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

    match Pathfinding3D.findPath navGrid ctx.CurrentPos ctx.TargetPos with
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

namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.EventBus
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Navigation
open Pomo.Core.Stores
open Pomo.Core.Algorithms
open Pomo.Core.Projections
open Pomo.Core.Algorithms.Pathfinding
open FSharp.Control.Reactive
open Pomo.Core.Environment

module Navigation =
  open Pomo.Core.Domain.Core

  let create
    (
      eventBus: EventBus,
      stateWrite: IStateWriteService,
      mapStore: MapStore,
      projections: Projections.ProjectionService
    ) =
    let entitySize = Constants.Navigation.EntitySize
    let gridCache = System.Collections.Generic.Dictionary<string, NavGrid>()
    let scenarioContexts = projections.EntityScenarioContexts

    let inline getNavGrid(mapKey: string) =
      match gridCache.TryGetValue mapKey with
      | true, grid -> grid
      | false, _ ->
        let mapDef = mapStore.find mapKey

        let grid =
          Grid.generate mapDef Constants.Navigation.GridCellSize entitySize

        gridCache[mapKey] <- grid
        grid

    let freeMovementThreshold = Constants.Entity.Size.X * 5.0f

    let inline tryGetEntityPosition entityId ctx =
      let snapshot = projections.ComputeMovementSnapshot ctx.ScenarioId

      snapshot.Positions
      |> Dictionary.tryFindV entityId
      |> ValueOption.map(fun pos -> struct (ctx, pos))

    let inline publishPath entityId path =
      if not(List.isEmpty path) then
        stateWrite.UpdateMovementState(entityId, MovingAlongPath path)

        eventBus.Publish(
          GameEvent.State(
            Physics(
              MovementStateChanged struct (entityId, MovingAlongPath path)
            )
          )
        )

    let updateMovement entityId targetPosition struct (ctx, currentPosition) =
      let distance = Vector2.Distance(currentPosition, targetPosition)
      let navGrid = getNavGrid ctx.MapKey

      // Check if this entity is AI-controlled (always use A*)
      let isAI =
        projections.AIControlledEntities
        |> ASet.force
        |> HashSet.contains entityId

      // Player can use direct movement if close AND has clear line of sight
      // AI always uses A* pathfinding to avoid getting stuck
      let useDirect =
        not isAI
        && distance < freeMovementThreshold
        && AStar.hasLineOfSight navGrid currentPosition targetPosition

      if useDirect then
        stateWrite.UpdateMovementState(entityId, MovingTo targetPosition)

        eventBus.Publish(
          GameEvent.State(
            Physics(
              MovementStateChanged struct (entityId, MovingTo targetPosition)
            )
          )
        )
      else
        // Use A* pathfinding
        match AStar.findPath navGrid currentPosition targetPosition with
        | ValueSome path -> publishPath entityId path
        | ValueNone ->
          // No path found (unreachable target) - set to Idle
          stateWrite.UpdateMovementState(entityId, Idle)

          eventBus.Publish(
            GameEvent.State(
              Physics(MovementStateChanged struct (entityId, Idle))
            )
          )

    { new CoreEventListener with
        member _.StartListening() =
          eventBus.Observable
          |> Observable.choose(fun event ->
            match event with
            | GameEvent.Intent(IntentEvent.MovementTarget target) ->
              Some target
            | _ -> None)
          |> Observable.subscribe(fun event ->
            let entityId = event.EntityId
            let targetPosition = event.Target
            let scenarioContexts = scenarioContexts |> AMap.force

            scenarioContexts
            |> HashMap.tryFindV entityId
            |> ValueOption.bind(tryGetEntityPosition entityId)
            |> ValueOption.iter(updateMovement entityId targetPosition))
    }

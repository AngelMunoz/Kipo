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

module Navigation =
  open Pomo.Core.Domain.Core

  let create
    (
      eventBus: EventBus,
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
      |> HashMap.tryFindV entityId
      |> ValueOption.map(fun pos -> struct (ctx, pos))

    let inline publishPath entityId path =
      if not(List.isEmpty path) then
        eventBus.Publish(
          GameEvent.State(
            Physics(
              MovementStateChanged struct (entityId, MovingAlongPath path)
            )
          )
        )

    let inline updateMovement
      entityId
      targetPosition
      struct (ctx, currentPosition)
      =
      let distance = Vector2.Distance(currentPosition, targetPosition)

      if distance < freeMovementThreshold then
        eventBus.Publish(
          GameEvent.State(
            Physics(
              MovementStateChanged struct (entityId, MovingTo targetPosition)
            )
          )
        )
      else
        let navGrid = getNavGrid ctx.MapKey

        AStar.findPath navGrid currentPosition targetPosition
        |> ValueOption.iter(publishPath entityId)

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

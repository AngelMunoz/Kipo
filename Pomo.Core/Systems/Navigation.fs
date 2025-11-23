namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.EventBus
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Navigation
open Pomo.Core.Stores
open Pomo.Core.Algorithms
open Pomo.Core.Projections // To get entity positions
open Pomo.Core.Algorithms.Pathfinding

module Navigation =
  open Pomo.Core.Domain.Core

  let create
    (eventBus: EventBus, mapStore: MapStore, mapKey: string, world: World)
    =

    let mapDef = mapStore.find mapKey
    let entitySize = Constants.Entity.Size

    let navGrid =
      Grid.generate mapDef Constants.Navigation.GridCellSize entitySize

    // Define the threshold for free movement (16 units * 5 = 80 units)
    let freeMovementThreshold = Constants.Entity.Size.X * 5.0f

    { new CoreEventListener with
        member _.StartListening() =
          eventBus.GetObservableFor<SystemCommunications.SetMovementTarget>()
          |> Observable.subscribe(fun event ->
            let entityId = event.EntityId
            let targetPosition = event.Target

            match
              world.Positions |> AMap.force |> HashMap.tryFindV entityId
            with
            | ValueSome currentPosition ->
              let distance = Vector2.Distance(currentPosition, targetPosition)

              if distance < freeMovementThreshold then
                // Use direct movement for close targets (free movement)
                eventBus.Publish(
                  StateChangeEvent.Physics(
                    PhysicsEvents.MovementStateChanged
                      struct (entityId, MovingTo targetPosition)
                  )
                )
              else
                // Use pathfinding for distant targets
                match
                  AStar.findPath navGrid currentPosition targetPosition
                with
                | ValueSome path when not(List.isEmpty path) ->
                  eventBus.Publish(
                    StateChangeEvent.Physics(
                      PhysicsEvents.MovementStateChanged
                        struct (entityId, MovingAlongPath path)
                    )
                  )
                | _ -> () // No path found, do nothing (effectively canceling move)
            | ValueNone -> ())
    }

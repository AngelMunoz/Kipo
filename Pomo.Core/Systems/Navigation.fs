namespace Pomo.Core.Domains

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.EventBus

module Navigation =

  type NavigationService =
    abstract StartListening: unit -> IDisposable


  let create(eventBus: EventBus, playerId: Guid<EntityId>) =

    { new NavigationService with
        member _.StartListening() =
          eventBus
          |> Observable.filter(fun ev ->
            match ev with
            | SetMovementTarget struct (id, _) -> id = playerId
            | _ -> false)
          |> Observable.subscribe(fun ev ->
            match ev with
            | SetMovementTarget struct (id, targetPosition) ->
              // Here we would implement pathfinding logic to move the entity
              // towards the targetPosition. For now, we just set the MovementState.
              eventBus.Publish(
                MovementStateChanged struct (id, MovingTo targetPosition)
              )
            | _ -> ())

    }

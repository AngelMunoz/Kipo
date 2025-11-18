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

module Navigation =
  open Pomo.Core.Domain.Core

  let create(eventBus: EventBus) =

    { new CoreEventListener with
        member _.StartListening() =
          eventBus.GetObservableFor<SystemCommunications.SetMovementTarget>()
          |> Observable.subscribe(fun event ->
            // Here we would implement pathfinding logic to move the entity
            // towards the targetPosition. For now, we just set the MovementState.
            eventBus.Publish(
              StateChangeEvent.Physics(
                PhysicsEvents.MovementStateChanged
                  struct (event.EntityId, MovingTo event.Target)
              )
            ))
    }

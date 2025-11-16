namespace Pomo.Core.Systems

open System
open System.Reactive.Disposables

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Events
open Pomo.Core.EventBus
open Pomo.Core.Stores
open Pomo.Core.Domain.World

module Inventory =
  open Pomo.Core.Domain.Core


  let create(eventBus: EventBus) =
    let handlePickUpItemIntent(intent: SystemCommunications.PickUpItemIntent) =
      eventBus.Publish(Inventory(ItemInstanceCreated intent.Item))

      eventBus.Publish(
        Inventory(
          ItemAddedToInventory struct (intent.Picker, intent.Item.InstanceId)
        )
      )

    { new CoreEventListener with
        member this.StartListening() : IDisposable =
          eventBus
            .GetObservableFor<SystemCommunications.PickUpItemIntent>()
            .Subscribe(handlePickUpItemIntent)
    }

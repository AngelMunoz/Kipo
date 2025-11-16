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
open Pomo.Core.Domain.Core

module Equipment =

  let equipedItemsForEntity (entityId: Guid<EntityId>) (world: World) =
    world.EquippedItems
    |> AMap.tryFind entityId
    |> AVal.map(Option.defaultValue HashMap.empty)

  let create (world: World) (eventBus: EventBus) : CoreEventListener =

    let handleEquipItemIntent(intent: SystemCommunications.EquipItemIntent) =
      eventBus.Publish(
        Inventory(
          ItemEquipped
            struct (intent.EntityId, intent.Slot, intent.ItemInstanceId)
        )
      )

    let handleUnequipItemIntent
      (intent: SystemCommunications.UnequipItemIntent)
      =
      let equippedItems =
        equipedItemsForEntity intent.EntityId world |> AVal.force

      match HashMap.tryFindV intent.Slot equippedItems with
      | ValueSome itemInstanceId ->
        eventBus.Publish(
          Inventory(
            ItemUnequipped struct (intent.EntityId, intent.Slot, itemInstanceId)
          )
        )
      | ValueNone -> () // Item not found in slot, do nothing

    { new CoreEventListener with
        member this.StartListening() : IDisposable =
          let disposable = new CompositeDisposable()

          eventBus
            .GetObservableFor<SystemCommunications.EquipItemIntent>()
            .Subscribe(handleEquipItemIntent)
          |> disposable.Add

          eventBus
            .GetObservableFor<SystemCommunications.UnequipItemIntent>()
            .Subscribe(handleUnequipItemIntent)
          |> disposable.Add

          disposable
    }

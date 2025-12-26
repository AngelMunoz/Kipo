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
open Pomo.Core.Environment

module Equipment =

  let equippedItemsForEntity (entityId: Guid<EntityId>) (world: World) =
    world.EquippedItems
    |> AMap.tryFind entityId
    |> AVal.map(Option.defaultValue HashMap.empty)

  let create
    (world: World)
    (eventBus: EventBus)
    (stateWrite: IStateWriteService)
    : CoreEventListener =

    let handleEquipItemIntent(intent: SystemCommunications.EquipItemIntent) =
      stateWrite.EquipItem(intent.EntityId, intent.Slot, intent.ItemInstanceId)

    let handleUnequipItemIntent
      (intent: SystemCommunications.UnequipItemIntent)
      =
      stateWrite.UnequipItem(intent.EntityId, intent.Slot)

    { new CoreEventListener with
        member this.StartListening() : IDisposable =
          let disposable = new CompositeDisposable()

          eventBus.Observable
          |> Observable.choose(fun e ->
            match e with
            | GameEvent.ItemIntent(ItemIntentEvent.Equip intent) -> Some intent
            | _ -> None)
          |> Observable.subscribe handleEquipItemIntent
          |> disposable.Add

          eventBus.Observable
          |> Observable.choose(fun e ->
            match e with
            | GameEvent.ItemIntent(ItemIntentEvent.Unequip intent) ->
              Some intent
            | _ -> None)
          |> Observable.subscribe handleUnequipItemIntent
          |> disposable.Add

          disposable
    }

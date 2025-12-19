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
open Pomo.Core.Environment

module Inventory =
  open Pomo.Core.Domain.Core
  open Pomo.Core.Projections

  let handlePickUpItemIntent
    (stateWrite: IStateWriteService)
    (intent: SystemCommunications.PickUpItemIntent)
    =
    stateWrite.CreateItemInstance(intent.Item)
    stateWrite.AddItemToInventory(intent.Picker, intent.Item.InstanceId)

  let handleUseItemIntent
    (
      eventBus: EventBus,
      itemStore: ItemStore,
      world: World,
      stateWrite: IStateWriteService
    )
    (intent: SystemCommunications.UseItemIntent)
    =
    match world.ItemInstances.TryGetValue intent.ItemInstanceId with
    | true, itemInstance ->
      match itemStore.tryFind itemInstance.ItemId with
      | ValueSome itemDef ->
        match itemDef.Kind with
        | Item.ItemKind.Usable props ->
          match itemInstance.UsesLeft with
          | ValueNone ->
            eventBus.Publish(
              GameEvent.Intent(
                IntentEvent.EffectApplication {
                  SourceEntity = intent.EntityId
                  TargetEntity = intent.EntityId
                  Effect = props.Effect
                }
              )
            )
          | ValueSome usesLeft ->

            if usesLeft > 0 then
              eventBus.Publish(
                GameEvent.Intent(
                  IntentEvent.EffectApplication {
                    SourceEntity = intent.EntityId
                    TargetEntity = intent.EntityId
                    Effect = props.Effect
                  }
                )
              )

              stateWrite.UpdateItemInstance {
                itemInstance with
                    UsesLeft =
                      itemInstance.UsesLeft
                      |> ValueOption.map(fun uses -> uses - 1)
              }
            else
              ()
        | Item.ItemKind.NonUsable -> ()
        | Item.ItemKind.Wearable _ -> ()
      | ValueNone -> ()
    | false, _ -> ()


  let create
    (
      eventBus: EventBus,
      itemStore: ItemStore,
      world: World,
      stateWrite: IStateWriteService
    ) =

    { new CoreEventListener with
        member _.StartListening() : IDisposable =
          new CompositeDisposable(
            eventBus.Observable
            |> Observable.choose(fun e ->
              match e with
              | GameEvent.ItemIntent(ItemIntentEvent.PickUp intent) ->
                Some intent
              | _ -> None)
            |> Observable.subscribe(handlePickUpItemIntent stateWrite),
            eventBus.Observable
            |> Observable.choose(fun e ->
              match e with
              | GameEvent.ItemIntent(ItemIntentEvent.Use intent) -> Some intent
              | _ -> None)
            |> Observable.subscribe(
              handleUseItemIntent(eventBus, itemStore, world, stateWrite)
            )
          )
    }

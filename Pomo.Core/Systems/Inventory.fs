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
  open Pomo.Core.Projections

  let handlePickUpItemIntent
    (eventBus: EventBus)
    (intent: SystemCommunications.PickUpItemIntent)
    =
    eventBus.Publish(Inventory(ItemInstanceCreated intent.Item))

    eventBus.Publish(
      Inventory(
        ItemAddedToInventory struct (intent.Picker, intent.Item.InstanceId)
      )
    )

  let handleUseItemIntent
    (eventBus: EventBus, itemStore: ItemStore, world: World)
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
            // infinite uses, so just publish effect application intent
            eventBus.Publish(
              {
                SourceEntity = intent.EntityId
                TargetEntity = intent.EntityId
                Effect = props.Effect
              }
              : SystemCommunications.EffectApplicationIntent
            )
          | ValueSome usesLeft ->

            if usesLeft > 0 then
              // publsh effect application intent
              eventBus.Publish(
                {
                  SourceEntity = intent.EntityId
                  TargetEntity = intent.EntityId
                  Effect = props.Effect
                }
                : SystemCommunications.EffectApplicationIntent
              )
              // decrement usages left and publish update event
              eventBus.Publish(
                Inventory(
                  UpdateItemInstance {
                    itemInstance with
                        UsesLeft =
                          itemInstance.UsesLeft
                          |> ValueOption.map(fun uses -> uses - 1)
                  }
                )
              )
            else
              // no uses left or infinite uses
              ()
        | Item.ItemKind.NonUsable ->
          // cannot use non-usable items
          ()
        | Item.ItemKind.Wearable _ ->
          // cannot use wearable items
          ()
      | ValueNone ->
        // item definition not found
        ()
    | false, _ ->
      // item instance not found
      ()


  let create(eventBus: EventBus, itemStore: ItemStore, world: World) =

    { new CoreEventListener with
        member _.StartListening() : IDisposable =
          new CompositeDisposable(
            eventBus
              .GetObservableFor<SystemCommunications.PickUpItemIntent>()
              .Subscribe(handlePickUpItemIntent eventBus),
            eventBus
              .GetObservableFor<SystemCommunications.UseItemIntent>()
              .Subscribe(handleUseItemIntent(eventBus, itemStore, world))
          )
    }

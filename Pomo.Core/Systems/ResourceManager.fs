namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.Control.Reactive
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.EventBus
open Pomo.Core.Domain
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Systems

module ResourceManager =
  open System.Reactive.Disposables

  module private Handlers =
    let handleDamageDealt
      (world: World)
      (eventBus: EventBus)
      (event: SystemCommunications.DamageDealt)
      =
      let resources = world.Resources |> AMap.force

      match resources.TryFindV event.Target with
      | ValueSome currentResources ->
        let newHP = max 0 (currentResources.HP - event.Amount)
        let newResources = { currentResources with HP = newHP }

        let stateChange =
          CombatEvents.ResourcesChanged struct (event.Target, newResources)
          |> StateChangeEvent.Combat

        eventBus.Publish stateChange
      | ValueNone -> ()

  type ResourceManagerSystem(game: Game) =
    inherit GameSystem(game)

    let subscriptions = new CompositeDisposable()

    override this.Initialize() =
      base.Initialize()

      this.EventBus.GetObservableFor<SystemCommunications.DamageDealt>()
      |> Observable.subscribe(
        Handlers.handleDamageDealt this.World this.EventBus
      )
      |> subscriptions.Add

    override _.Dispose disposing =
      if disposing then
        subscriptions.Dispose()

      base.Dispose disposing

    override _.Kind = ResourceManager

    override _.Update gameTime = base.Update gameTime

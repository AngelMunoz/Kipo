namespace Pomo.Core.Systems

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.Control.Reactive
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.EventBus
open Pomo.Core.Domain
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Systems.Systems

module ResourceManager =
  open System.Reactive.Disposables

  module private Handlers =
    let handleDamageDealt
      (world: World)
      (eventBus: EventBus)
      (event: SystemCommunications.DamageDealt)
      =
      // Force the amap to a regular map to get the value now.
      let resources = world.Resources |> AMap.force

      match resources.TryFindV event.Target with
      | ValueSome currentResources ->
        let newHP = max 0 (currentResources.HP - event.Amount)
        let newResources = { currentResources with HP = newHP }

        eventBus.Publish(
          StateChangeEvent.Combat(
            CombatEvents.ResourcesChanged struct (event.Target, newResources)
          )
        )

        eventBus.Publish(
          StateChangeEvent.Combat(
            CombatEvents.InCombatTimerRefreshed event.Target
          )
        )
      | ValueNone -> ()

  let handleResourceRestored
    (world: World)
    (eventBus: EventBus)
    (derivedStats: amap<Guid<EntityId>, Entity.DerivedStats>)
    (event: SystemCommunications.ResourceRestored)
    =
    // Force the amap to a regular map to get the value now.
    let resources = world.Resources |> AMap.force
    let stats = derivedStats |> AMap.force

    let position =
      world.Positions
      |> AMap.force
      |> HashMap.tryFindV event.Target
      |> ValueOption.defaultValue Vector2.Zero

    match resources.TryFindV event.Target with
    | ValueSome currentResources ->
      let resources =
        match event.ResourceType with
        | Entity.ResourceType.HP ->
          let maxHP =
            stats
            |> HashMap.tryFindV event.Target
            |> ValueOption.map(fun s -> s.HP)
            |> ValueOption.defaultValue event.Amount

          let newHP = min maxHP (currentResources.HP + event.Amount)

          { currentResources with HP = newHP }
        | Entity.ResourceType.MP ->
          let maxMP =
            stats
            |> HashMap.tryFindV event.Target
            |> ValueOption.map(fun s -> s.MP)
            |> ValueOption.defaultValue event.Amount

          let newMP = min maxMP (currentResources.MP + event.Amount)
          { currentResources with MP = newMP }

      eventBus.Publish(
        StateChangeEvent.Combat(
          ResourcesChanged struct (event.Target, resources)
        )
      )

      eventBus.Publish<SystemCommunications.ShowNotification> {
        Message =
          let amount = event.Amount
          $"%d{amount} {event.ResourceType}"
        Position = position
      }
    | ValueNone -> ()

  module private Regeneration =
    let processAutoRegen
      (allResources: HashMap<Guid<EntityId>, Entity.Resource>)
      (allInCombat: HashMap<Guid<EntityId>, TimeSpan>)
      (allStats: HashMap<Guid<EntityId>, Entity.DerivedStats>)
      (totalGameTime: TimeSpan)
      (deltaTime: TimeSpan)
      (eventBus: EventBus)
      (accumulators: Dictionary<Guid<EntityId>, struct (float * float)>)
      =
      for entityId, currentResources in allResources do
        let inCombatUntil =
          allInCombat.TryFindV entityId
          |> ValueOption.defaultValue TimeSpan.Zero

        if totalGameTime > inCombatUntil then
          match allStats.TryFindV entityId with
          | ValueSome stats ->
            let struct (currentHpAcc, currentMpAcc) =
              match accumulators.TryGetValue entityId with
              | true, value -> value
              | false, _ -> struct (0.0, 0.0)

            let hpRegenThisFrame = float stats.HPRegen * deltaTime.TotalSeconds
            let mpRegenThisFrame = float stats.MPRegen * deltaTime.TotalSeconds

            let newHpAcc = currentHpAcc + hpRegenThisFrame
            let newMpAcc = currentMpAcc + mpRegenThisFrame

            let hpToHeal = int newHpAcc
            let mpToHeal = int newMpAcc

            let remainderHpAcc = newHpAcc - float hpToHeal
            let remainderMpAcc = newMpAcc - float mpToHeal

            // Update the mutable dictionary with the new remainder
            accumulators[entityId] <- remainderHpAcc, remainderMpAcc

            if hpToHeal > 0 || mpToHeal > 0 then
              let newHP = min stats.HP (currentResources.HP + hpToHeal)
              let newMP = min stats.MP (currentResources.MP + mpToHeal)

              if
                newHP <> currentResources.HP || newMP <> currentResources.MP
              then
                let newResources = {
                  currentResources with
                      HP = newHP
                      MP = newMP
                }

                eventBus.Publish(
                  StateChangeEvent.Combat(
                    CombatEvents.ResourcesChanged
                      struct (entityId, newResources)
                  )
                )
          | ValueNone -> ()

  type ResourceManagerSystem(game: Game) as this =
    inherit GameSystem(game)
    let subscriptions = new CompositeDisposable()
    let allStats = this.Projections.DerivedStats
    let regenAccumulators = Dictionary<Guid<EntityId>, struct (float * float)>()

    override this.Initialize() =
      base.Initialize()

      this.EventBus.GetObservableFor<SystemCommunications.DamageDealt>()
      |> Observable.subscribe(
        Handlers.handleDamageDealt this.World this.EventBus
      )
      |> subscriptions.Add

      this.EventBus.GetObservableFor<SystemCommunications.ResourceRestored>()
      |> Observable.subscribe(
        handleResourceRestored
          this.World
          this.EventBus
          this.Projections.DerivedStats
      )
      |> subscriptions.Add

    override _.Dispose disposing =
      if disposing then
        subscriptions.Dispose()

      base.Dispose disposing

    override _.Kind = ResourceManager

    override this.Update gameTime =
      base.Update gameTime
      let allResources = this.World.Resources |> AMap.force
      let allInCombat = this.World.InCombatUntil |> AMap.force
      let allStats = allStats |> AMap.force

      let struct (totalGameTime, deltaTime) =
        this.World.Time
        |> AVal.map(fun t -> struct (t.TotalGameTime, t.Delta))
        |> AVal.force

      Regeneration.processAutoRegen
        allResources
        allInCombat
        allStats
        totalGameTime
        deltaTime
        this.EventBus
        regenAccumulators

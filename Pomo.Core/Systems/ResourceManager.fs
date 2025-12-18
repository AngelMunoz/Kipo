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
          GameEvent.State(
            StateChangeEvent.Combat(
              CombatEvents.ResourcesChanged struct (event.Target, newResources)
            )
          )
        )

        eventBus.Publish(
          GameEvent.State(
            StateChangeEvent.Combat(
              CombatEvents.InCombatTimerRefreshed event.Target
            )
          )
        )

        // Emit EntityDied if HP dropped to 0 or below
        if newHP <= 0 then
          let scenarioId =
            world.EntityScenario |> AMap.force |> HashMap.tryFindV event.Target

          match scenarioId with
          | ValueSome sid ->
            eventBus.Publish(
              GameEvent.Lifecycle(
                LifecycleEvent.EntityDied {
                  EntityId = event.Target
                  ScenarioId = sid
                }
              )
            )
          | ValueNone -> ()
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
        GameEvent.State(
          StateChangeEvent.Combat(
            ResourcesChanged struct (event.Target, resources)
          )
        )
      )

      eventBus.Publish(
        GameEvent.Notification(
          NotificationEvent.ShowMessage {
            Message =
              let amount = event.Amount
              $"%d{amount} {event.ResourceType}"
            Position = position
          }
        )
      )
    | ValueNone -> ()

  module private Regeneration =
    open Pomo.Core.Projections

    let processAutoRegen
      (regenContexts: HashMap<Guid<EntityId>, RegenerationContext>)
      (totalGameTime: TimeSpan)
      (deltaTime: TimeSpan)
      (eventBus: EventBus)
      (accumulators: Dictionary<Guid<EntityId>, struct (float * float)>)
      =
      for entityId, ctx in regenContexts do
        if totalGameTime > ctx.InCombatUntil then
          let currentResources = ctx.Resources
          let stats = ctx.DerivedStats

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

            if newHP <> currentResources.HP || newMP <> currentResources.MP then
              let newResources = {
                currentResources with
                    HP = newHP
                    MP = newMP
              }

              eventBus.Publish(
                GameEvent.State(
                  StateChangeEvent.Combat(
                    CombatEvents.ResourcesChanged
                      struct (entityId, newResources)
                  )
                )
              )

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type ResourceManagerSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices

    let subscriptions = new CompositeDisposable()
    let regenAccumulators = Dictionary<Guid<EntityId>, struct (float * float)>()

    override this.Initialize() =
      base.Initialize()

      core.EventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Notification(NotificationEvent.DamageDealt dmg) -> Some dmg
        | _ -> None)
      |> Observable.subscribe(
        Handlers.handleDamageDealt core.World core.EventBus
      )
      |> subscriptions.Add

      core.EventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Notification(NotificationEvent.ResourceRestored restored) ->
          Some restored
        | _ -> None)
      |> Observable.subscribe(
        handleResourceRestored
          core.World
          core.EventBus
          gameplay.Projections.DerivedStats
      )
      |> subscriptions.Add

    override _.Dispose disposing =
      if disposing then
        subscriptions.Dispose()

      base.Dispose disposing

    override _.Kind = ResourceManager

    override this.Update gameTime =
      base.Update gameTime

      // Force the pre-joined, pre-filtered projection
      let regenContexts =
        gameplay.Projections.RegenerationContexts |> AMap.force

      let struct (totalGameTime, deltaTime) =
        core.World.Time
        |> AVal.map(fun t -> struct (t.TotalGameTime, t.Delta))
        |> AVal.force

      Regeneration.processAutoRegen
        regenContexts
        totalGameTime
        deltaTime
        core.EventBus
        regenAccumulators

namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.EventBus
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Systems
open Pomo.Core.Systems.DamageCalculator

module Combat =
  open System.Reactive.Disposables

  module private Handlers =
    let applySkillDamage
      (world: World)
      (eventBus: EventBus)
      (casterId: Guid<EntityId>)
      (targetId: Guid<EntityId>)
      (skill: ActiveSkill)
      =
      let stats = Projections.CalculateDerivedStats world |> AMap.force
      let positions = Projections.UpdatedPositions world |> AMap.force

      match stats.TryFindV casterId, stats.TryFindV targetId with
      | ValueSome attackerStats, ValueSome defenderStats ->
        let result =
          if casterId = targetId then
            DamageCalculator.calculateRawDamageSelfTarget
              world.Rng
              attackerStats
              defenderStats
              skill
          else
            DamageCalculator.calculateFinalDamage
              world.Rng
              attackerStats
              defenderStats
              skill

        let targetPos =
          positions
          |> HashMap.tryFindV targetId
          |> ValueOption.defaultValue Vector2.Zero

        result
      | _ ->
          // Caster or target stats not found
          {
            Amount = 0
            IsCritical = false
            IsEvaded = false
          }

    let private applyInstantaneousSkillEffects
      (world: World)
      (eventBus: EventBus)
      (casterId: Guid<EntityId>)
      (targetId: Guid<EntityId>)
      (activeSkill: ActiveSkill)
      =
      let positions = Projections.UpdatedPositions world |> AMap.force

      let result =
        applySkillDamage world eventBus casterId targetId activeSkill

      let targetPos =
        positions
        |> HashMap.tryFindV targetId
        |> ValueOption.defaultValue Vector2.Zero

      if result.IsEvaded then
        eventBus.Publish(
          {
            Message = "Miss"
            Position = targetPos
          }
          : SystemCommunications.ShowNotification
        )
      else
        eventBus.Publish(
          {
            Target = targetId
            Amount = result.Amount
          }
          : SystemCommunications.DamageDealt
        )

        eventBus.Publish(
          {
            Message = $"-{result.Amount}"
            Position = targetPos
          }
          : SystemCommunications.ShowNotification
        )

        if result.IsCritical then
          eventBus.Publish(
            {
              Message = "Crit!"
              Position = targetPos
            }
            : SystemCommunications.ShowNotification
          )

        for effect in activeSkill.Effects do
          let intent =
            {
              SourceEntity = casterId
              TargetEntity = targetId
              Effect = effect
            }
            : SystemCommunications.EffectApplicationIntent

          eventBus.Publish intent

    let handleEffectDamageIntent
      (world: World)
      (eventBus: EventBus)
      (intent: SystemCommunications.EffectDamageIntent)
      =
      let stats = Projections.CalculateDerivedStats world |> AMap.force
      let positions = Projections.UpdatedPositions world |> AMap.force

      match
        stats.TryFindV intent.SourceEntity, stats.TryFindV intent.TargetEntity
      with
      | ValueSome attackerStats, ValueSome defenderStats ->

        let totalDamageFromModifiers =
          intent.Effect.Modifiers
          |> Array.choose(fun m ->
            match m with
            | AbilityDamageMod(mathExpr, element) ->
              let dmg =
                DamageCalculator.calculateEffectDamage
                  attackerStats
                  defenderStats
                  mathExpr
                  intent.Effect.DamageSource
                  element

              Some dmg
            | _ -> None)
          |> Array.sum


        if totalDamageFromModifiers > 0 then
          eventBus.Publish(
            {
              Target = intent.TargetEntity
              Amount = totalDamageFromModifiers
            }
            : SystemCommunications.DamageDealt
          )

          let targetPos =
            positions
            |> HashMap.tryFindV intent.TargetEntity
            |> ValueOption.defaultValue Vector2.Zero

          eventBus.Publish(
            {
              Message = $"-{totalDamageFromModifiers}"
              Position = targetPos
            }
            : SystemCommunications.ShowNotification
          )
      | _ -> () // Attacker or defender stats not found

    let handleAbilityIntent
      (world: World)
      (eventBus: EventBus)
      (skillStore: Stores.SkillStore)
      (casterId: Guid<EntityId>)
      (skillId: int<SkillId>)
      (targetId: Guid<EntityId>)
      =
      match skillStore.tryFind skillId with
      | ValueSome(Skill.Active activeSkill) ->
        // Apply cost and cooldown now that the ability is confirmed
        let totalGameTime =
          world.Time |> AVal.map _.TotalGameTime |> AVal.force

        let resources = world.Resources |> AMap.force
        let cooldowns = world.AbilityCooldowns |> AMap.force


        // 1. Apply resource cost
        match activeSkill.Cost with
        | ValueSome cost ->
          let currentResources =
            resources.TryFindV casterId
            |> ValueOption.defaultValue {
              HP = 0
              MP = 0
              Status = Entity.Status.Dead
            }

          let requiredAmount = cost.Amount |> ValueOption.defaultValue 0

          let newResources =
            match cost.ResourceType with
            | Entity.ResourceType.HP -> {
                currentResources with
                    HP = currentResources.HP - requiredAmount
              }
            | Entity.ResourceType.MP ->
                {
                  currentResources with
                      MP = currentResources.MP - requiredAmount
                }

          eventBus.Publish(
            StateChangeEvent.Combat(
              CombatEvents.ResourcesChanged struct (casterId, newResources)
            )
          )
        | ValueNone -> ()

        // 2. Apply cooldown
        match activeSkill.Cooldown with
        | ValueSome cd ->
          let currentCooldowns =
            cooldowns.TryFindV casterId
            |> ValueOption.defaultValue HashMap.empty

          let readyTime = totalGameTime + cd
          let newCooldowns = currentCooldowns.Add(skillId, readyTime)

          eventBus.Publish(
            StateChangeEvent.Combat(
              CombatEvents.CooldownsChanged struct (casterId, newCooldowns)
            )
          )
        | ValueNone -> ()

        // 3. Handle delivery
        match activeSkill.Delivery with
        | Delivery.Projectile projectileInfo ->
          let projectileId = Guid.NewGuid() |> UMX.tag<EntityId>

          let liveProjectile: Projectile.LiveProjectile = {
            Caster = casterId
            Target = targetId
            SkillId = skillId
            Info = projectileInfo
          }

          eventBus.Publish(
            StateChangeEvent.CreateProjectile
              struct (projectileId, liveProjectile)
          )
        | Delivery.Melee ->
          applyInstantaneousSkillEffects
            world
            eventBus
            casterId
            targetId
            activeSkill
        | Delivery.Instant ->
          applyInstantaneousSkillEffects
            world
            eventBus
            casterId
            targetId
            activeSkill
      | _ -> ()

    let handleProjectileImpact
      (world: World, eventBus: EventBus, skillStore: Stores.SkillStore)
      (impact: SystemCommunications.ProjectileImpacted)
      =
      match skillStore.tryFind impact.SkillId with
      | ValueSome(Active skill) ->
        let result =
          applySkillDamage world eventBus impact.CasterId impact.TargetId skill

        let positions = Projections.UpdatedPositions world |> AMap.force

        let targetPos =
          positions
          |> HashMap.tryFindV impact.TargetId
          |> ValueOption.defaultValue Vector2.Zero

        if result.IsEvaded then
          eventBus.Publish(
            {
              Message = "Miss"
              Position = targetPos
            }
            : SystemCommunications.ShowNotification
          )
        else
          eventBus.Publish(
            {
              Target = impact.TargetId
              Amount = result.Amount
            }
            : SystemCommunications.DamageDealt
          )

          eventBus.Publish(
            {
              Message = $"-{result.Amount}"
              Position = targetPos
            }
            : SystemCommunications.ShowNotification
          )

          if result.IsCritical then
            eventBus.Publish(
              {
                Message = "Crit!"
                Position = targetPos
              }
              : SystemCommunications.ShowNotification
            )

          for effect in skill.Effects do
            let intent =
              {
                SourceEntity = impact.CasterId
                TargetEntity = impact.TargetId
                Effect = effect
              }
              : SystemCommunications.EffectApplicationIntent

            eventBus.Publish intent
      | _ -> () // Skill not found

    let handleAbilityIntentEvent
      (world: World, eventBus: EventBus, skillStore: Stores.SkillStore)
      (event: SystemCommunications.AbilityIntent)
      =
      match event.Target with
      | ValueSome targetId ->
        handleAbilityIntent
          world
          eventBus
          skillStore
          event.Caster
          event.SkillId
          targetId
      | ValueNone -> ()


  type CombatSystem(game: Game) as this =
    inherit GameSystem(game)

    let eventBus = this.EventBus
    let skillStore = this.Game.Services.GetService<Stores.SkillStore>()

    let subscriptions = new CompositeDisposable()

    override this.Initialize() =
      base.Initialize()

      eventBus.GetObservableFor<SystemCommunications.AbilityIntent>()
      |> Observable.subscribe(
        Handlers.handleAbilityIntentEvent(this.World, eventBus, skillStore)
      )
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.ProjectileImpacted>()
      |> Observable.subscribe(
        Handlers.handleProjectileImpact(this.World, eventBus, skillStore)
      )
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.EffectDamageIntent>()
      |> Observable.subscribe(
        Handlers.handleEffectDamageIntent this.World eventBus
      )
      |> subscriptions.Add

    override _.Dispose disposing =
      if disposing then
        subscriptions.Dispose()

      base.Dispose disposing

    override _.Kind = SystemKind.Combat

    override _.Update gameTime = base.Update gameTime

namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.EventBus
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Systems
open Pomo.Core.Systems.DamageCalculator

module Combat =

  module Handlers =
    let private applySkillDamage
      (world: World)
      (eventBus: EventBus)
      (casterId: Guid<EntityId>)
      (targetId: Guid<EntityId>)
      (skill: ActiveSkill)
      =
      let stats = world.DerivedStats |> AMap.force
      let positions = world.Positions |> AMap.force

      match stats.TryFindV casterId, stats.TryFindV targetId with
      | ValueSome attackerStats, ValueSome defenderStats ->
        let result =
          DamageCalculator.calculateFinalDamage
            world.Rng
            attackerStats
            defenderStats
            skill

        let targetPos =
          positions.TryFindV targetId
          |> ValueOption.defaultValue Vector2.Zero

        if result.IsEvaded then
          eventBus.Publish(ShowNotification("Miss", targetPos))
        else
          eventBus.Publish(DamageDealt(targetId, result.Amount))

          if result.IsCritical then
            eventBus.Publish(ShowNotification("Crit!", targetPos))
      | _ -> () // Attacker or defender not found

    let private handleAbilityIntent
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
        let gameTime = world.DeltaTime |> AVal.force
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

          eventBus.Publish(ResourcesChanged struct (casterId, newResources))
        | ValueNone -> ()

        // 2. Apply cooldown
        match activeSkill.Cooldown with
        | ValueSome cd ->
          let currentCooldowns =
            cooldowns.TryFindV casterId
            |> ValueOption.defaultValue HashMap.empty

          let readyTime = gameTime + cd
          let newCooldowns = currentCooldowns.Add(skillId, readyTime)
          eventBus.Publish(CooldownsChanged struct (casterId, newCooldowns))
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
            CreateProjectile struct (projectileId, liveProjectile)
          )
        | Delivery.Melee ->
          applySkillDamage world eventBus casterId targetId activeSkill
        | Delivery.Instant -> () // TODO: Implement instant-hit logic
      | _ -> ()

    let private handleProjectileImpact
      (world: World)
      (eventBus: EventBus)
      (skillStore: Stores.SkillStore)
      (impact: ProjectileImpact)
      =
      match skillStore.tryFind impact.SkillId with
      | ValueSome(Active skill) ->
        applySkillDamage
          world
          eventBus
          impact.CasterId
          impact.TargetId
          skill
      | _ -> () // Skill not found

    let handleEvent
      (dependencies: World * EventBus * Stores.SkillStore)
      (event: WorldEvent)
      =
      let world, eventBus, skillStore = dependencies

      match event with
      | AbilityIntent(casterId, skillId, ValueSome targetId) ->
        handleAbilityIntent world eventBus skillStore casterId skillId targetId
      | ProjectileImpacted impact ->
        handleProjectileImpact world eventBus skillStore impact
      | _ -> ()


  type CombatSystem(game: Game) as this =
    inherit GameSystem(game)

    let eventBus = this.EventBus
    let skillStore = this.Game.Services.GetService<Stores.SkillStore>()

    let mutable subscription: IDisposable = null

    let injectedHandler = Handlers.handleEvent(this.World, eventBus, skillStore)

    override this.Initialize() =
      base.Initialize()
      subscription <- eventBus |> Observable.subscribe injectedHandler

    override this.Dispose disposing =
      base.Dispose disposing

      match subscription with
      | null -> ()
      | sub -> sub.Dispose()

    override _.Kind = SystemKind.Combat

    override this.Update gameTime = base.Update gameTime

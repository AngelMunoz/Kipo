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
open Pomo.Core.Systems
open Pomo.Core.Systems.Systems
open Pomo.Core.Systems.DamageCalculator

module Combat =
  open System.Reactive.Disposables

  type CombatContext = {
    World: World
    EventBus: EventBus
    SearchContext: Spatial.Search.SearchContext
    DerivedStats: HashMap<Guid<EntityId>, Entity.DerivedStats>
    Positions: HashMap<Guid<EntityId>, Vector2>
    SkillStore: Stores.SkillStore
  }

  module private Targeting =

    let getPosition (ctx: CombatContext) (entityId: Guid<EntityId>) =
      ctx.Positions
      |> HashMap.tryFindV entityId
      |> ValueOption.defaultValue Vector2.Zero

    let resolveCircle
      (ctx: CombatContext)
      (casterId: Guid<EntityId>)
      (center: Vector2)
      (radius: float32)
      (target: SystemCommunications.SkillTarget)
      =
      match target with
      | SystemCommunications.TargetDirection pos ->
        // Origin is caster. Radius is distance to cursor, clamped by skill radius.
        let casterPos = getPosition ctx casterId
        let dist = Vector2.Distance(casterPos, pos)
        let r = Math.Min(dist, radius)
        casterPos, r
      | _ -> center, radius

    let resolveCone
      (ctx: CombatContext)
      (casterId: Guid<EntityId>)
      (center: Vector2)
      (target: SystemCommunications.SkillTarget)
      =
      let casterPos = getPosition ctx casterId

      match target with
      | SystemCommunications.TargetDirection pos ->
        // Origin is caster, direction is towards target position
        let offset = pos - casterPos

        let dir =
          if offset = Vector2.Zero then
            Vector2.UnitX
          else
            Vector2.Normalize(offset)

        casterPos, dir
      | SystemCommunications.TargetPosition pos ->
        // Origin is the target position. Direction is from caster to position.
        let offset = pos - casterPos

        let dir =
          if offset = Vector2.Zero then
            Vector2.UnitX
          else
            Vector2.Normalize(offset)

        pos, dir
      | SystemCommunications.TargetEntity targetId ->
        // Origin is target entity.
        // Direction is from caster to target entity ("shatter")
        let targetPos = getPosition ctx targetId
        let offset = targetPos - casterPos

        let dir =
          if offset = Vector2.Zero then
            Vector2.UnitX
          else
            Vector2.Normalize(offset)

        targetPos, dir
      | _ -> center, Vector2.UnitX

    let resolveLine
      (ctx: CombatContext)
      (casterId: Guid<EntityId>)
      (center: Vector2)
      (length: float32)
      (target: SystemCommunications.SkillTarget)
      =
      let casterPos = getPosition ctx casterId

      match target with
      | SystemCommunications.TargetDirection pos ->
        // Start at caster, end at caster + direction * length
        let offset = pos - casterPos

        let dir =
          if offset = Vector2.Zero then
            Vector2.UnitX
          else
            Vector2.Normalize(offset)

        casterPos, casterPos + dir * length
      | SystemCommunications.TargetPosition pos ->
        // Start at caster. End at position, clamped by length.
        let offset = pos - casterPos
        let dist = offset.Length()

        let dir =
          if dist > 0.0f then
            Vector2.Normalize(offset)
          else
            Vector2.UnitX

        let actualLength = Math.Min(dist, length)
        casterPos, casterPos + dir * actualLength
      | SystemCommunications.TargetEntity targetId ->
        // Start at target entity.
        // Direction from caster to target entity.
        let targetPos = getPosition ctx targetId
        let offset = targetPos - casterPos

        let dir =
          if offset = Vector2.Zero then
            Vector2.UnitX
          else
            Vector2.Normalize(offset)

        targetPos, targetPos + dir * length
      | _ -> center, center + Vector2.UnitX * length

    let calculateAdaptiveAperture(direction: Vector2) =
      // Reference forward vector (e.g., along positive Y for isometric, or check caster facing)
      // Assuming Caster's forward is `Vector2.UnitY` for simplicity or use specific entity facing
      let referenceForward = Vector2.UnitY

      // Angle of target relative to reference forward (in degrees)
      let angleFromForwardRad =
        MathF.Acos(Vector2.Dot(referenceForward, direction))

      let angleFromForwardDeg = MathHelper.ToDegrees(angleFromForwardRad)

      // Map 0-90 degrees from forward to 30-180 degrees aperture
      // This assumes symmetrical spread. If not, needs left/right calc.
      // Aperture = 30 + (angleFromForwardDeg / 90.0) * 150.0
      if angleFromForwardDeg <= 90.0f then
        30.0f + (angleFromForwardDeg / 90.0f) * 150.0f
      else // If target is behind (90-180 deg), mirror the angle or cap.
        // For now, let's just cap at max if it goes beyond 90 (very wide spread)
        180.0f

    let resolveAdaptiveCone
      (ctx: CombatContext)
      (casterId: Guid<EntityId>)
      (target: SystemCommunications.SkillTarget)
      =
      let casterPos = getPosition ctx casterId

      match target with
      | SystemCommunications.TargetPosition pos ->
        let offset = pos - casterPos
        let dist = offset.Length()

        let dir =
          if dist > 0.001f then
            Vector2.Normalize(offset)
          else
            Vector2.UnitX

        let apertureAngle = calculateAdaptiveAperture dir
        casterPos, dir, apertureAngle

      | SystemCommunications.TargetDirection pos ->
        // Similar to TargetPosition but direction is explicit?
        // Usually TargetDirection implies origin is caster.
        let offset = pos - casterPos
        let dist = offset.Length()

        let dir =
          if dist > 0.001f then
            Vector2.Normalize(offset)
          else
            Vector2.UnitX

        let apertureAngle = calculateAdaptiveAperture dir
        casterPos, dir, apertureAngle
      | _ -> Vector2.Zero, Vector2.UnitX, 0.0f // Fallback/Invalid

  module private Execution =

    let applySkillDamage
      (ctx: CombatContext)
      (casterId: Guid<EntityId>)
      (targetId: Guid<EntityId>)
      (skill: ActiveSkill)
      =
      match
        ctx.DerivedStats.TryFindV casterId, ctx.DerivedStats.TryFindV targetId
      with
      | ValueSome attackerStats, ValueSome defenderStats ->
        if casterId = targetId then
          DamageCalculator.calculateRawDamageSelfTarget
            ctx.World.Rng
            attackerStats
            defenderStats
            skill
        else
          DamageCalculator.calculateFinalDamage
            ctx.World.Rng
            attackerStats
            defenderStats
            skill
      | _ ->
          {
            Amount = 0
            IsCritical = false
            IsEvaded = false
          }

    let applyInstantaneousSkillEffects
      (ctx: CombatContext)
      (casterId: Guid<EntityId>)
      (targetId: Guid<EntityId>)
      (activeSkill: ActiveSkill)
      =
      let result = applySkillDamage ctx casterId targetId activeSkill
      let targetPos = Targeting.getPosition ctx targetId

      if result.IsEvaded then
        ctx.EventBus.Publish(
          {
            Message = "Miss"
            Position = targetPos
          }
          : SystemCommunications.ShowNotification
        )
      else
        ctx.EventBus.Publish(
          {
            Target = targetId
            Amount = result.Amount
          }
          : SystemCommunications.DamageDealt
        )

        ctx.EventBus.Publish(
          {
            Message = $"-{result.Amount}"
            Position = targetPos
          }
          : SystemCommunications.ShowNotification
        )

        if result.IsCritical then
          ctx.EventBus.Publish(
            {
              Message = "Crit!"
              Position = targetPos
            }
            : SystemCommunications.ShowNotification
          )

        for effect in activeSkill.Effects do
          let intent: SystemCommunications.EffectApplicationIntent = {
            SourceEntity = casterId
            TargetEntity = targetId
            Effect = effect
          }

          ctx.EventBus.Publish intent

    let applyResourceCost
      (ctx: CombatContext)
      (casterId: Guid<EntityId>)
      (activeSkill: ActiveSkill)
      =
      match activeSkill.Cost with
      | ValueSome cost ->
        let resources = ctx.World.Resources |> AMap.force

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

        ctx.EventBus.Publish(
          StateChangeEvent.Combat(
            CombatEvents.ResourcesChanged struct (casterId, newResources)
          )
        )
      | ValueNone -> ()

    let applyCooldown
      (ctx: CombatContext)
      (casterId: Guid<EntityId>)
      (skillId: int<SkillId>)
      (activeSkill: ActiveSkill)
      =
      match activeSkill.Cooldown with
      | ValueSome cd ->
        let totalGameTime =
          ctx.World.Time |> AVal.map _.TotalGameTime |> AVal.force

        let cooldowns = ctx.World.AbilityCooldowns |> AMap.force

        let currentCooldowns =
          cooldowns.TryFindV casterId |> ValueOption.defaultValue HashMap.empty

        let readyTime = totalGameTime + cd
        let newCooldowns = currentCooldowns.Add(skillId, readyTime)

        ctx.EventBus.Publish(
          StateChangeEvent.Combat(
            CombatEvents.CooldownsChanged struct (casterId, newCooldowns)
          )
        )
      | ValueNone -> ()


  module private Handlers =

    let handleEffectResourceIntent
      (ctx: CombatContext)
      (intent: SystemCommunications.EffectResourceIntent)
      =
      match ctx.DerivedStats.TryFindV intent.SourceEntity with
      | ValueSome attackerStats ->
        let totalResourceChangeFromModifiers =
          intent.Effect.Modifiers
          |> Array.choose(fun m ->
            match m with
            | ResourceChange(resource, formula) ->
              let changeAmount =
                DamageCalculator.caculateEffectRestoration
                  attackerStats
                  formula

              Some(
                {
                  Amount = changeAmount
                  ResourceType = resource
                  Target = intent.TargetEntity
                }
                : SystemCommunications.ResourceRestored
              )
            | _ -> None)

        for resourceChangeEvent in totalResourceChangeFromModifiers do
          ctx.EventBus.Publish resourceChangeEvent

        ctx.EventBus.Publish(
          EffectExpired struct (intent.TargetEntity, intent.ActiveEffectId)
        )
      | _ -> ()

    let handleEffectDamageIntent
      (ctx: CombatContext)
      (intent: SystemCommunications.EffectDamageIntent)
      =
      match
        ctx.DerivedStats.TryFindV intent.SourceEntity,
        ctx.DerivedStats.TryFindV intent.TargetEntity
      with
      | ValueSome attackerStats, ValueSome defenderStats ->
        let totalDamageFromModifiers =
          intent.Effect.Modifiers
          |> Array.choose(fun m ->
            match m with
            | AbilityDamageMod(mathExpr, element) ->
              Some(
                DamageCalculator.calculateEffectDamage
                  attackerStats
                  defenderStats
                  mathExpr
                  intent.Effect.DamageSource
                  element
              )
            | _ -> None)
          |> Array.sum

        if totalDamageFromModifiers > 0 then
          ctx.EventBus.Publish(
            {
              Target = intent.TargetEntity
              Amount = totalDamageFromModifiers
            }
            : SystemCommunications.DamageDealt
          )

          let targetPos = Targeting.getPosition ctx intent.TargetEntity

          ctx.EventBus.Publish(
            {
              Message = $"-{totalDamageFromModifiers}"
              Position = targetPos
            }
            : SystemCommunications.ShowNotification
          )
      | _ -> ()

    let handleProjectileDelivery
      (ctx: CombatContext)
      (casterId: Guid<EntityId>)
      (skillId: int<SkillId>)
      (target: SystemCommunications.SkillTarget)
      (activeSkill: ActiveSkill)
      (projectileInfo: Projectile.ProjectileInfo)
      =

      // Handle Multi-Target Projectiles (Fan of Knives, etc.)
      let targets =
        match activeSkill.Area with
        | AdaptiveCone(length, maxTargets) ->
          let origin, direction, angle =
            Targeting.resolveAdaptiveCone ctx casterId target

          if angle > 0.0f then
            Spatial.Search.findTargetsInCone ctx.SearchContext {
              CasterId = casterId
              Cone = {
                Origin = origin
                Direction = direction
                AngleDegrees = angle
                Length = length
              }
              MaxTargets = maxTargets
            }
          else
            IndexList.empty
        | _ -> IndexList.empty

      // If we found area targets, fire projectiles at them
      if not targets.IsEmpty then
        for targetId in targets do
          let projectileId = Guid.NewGuid() |> UMX.tag<EntityId>

          let liveProjectile: Projectile.LiveProjectile = {
            Caster = casterId
            Target = targetId
            SkillId = skillId
            Info = projectileInfo
          }

          ctx.EventBus.Publish(
            StateChangeEvent.CreateProjectile
              struct (projectileId, liveProjectile, ValueNone)
          )
      else
        // Fallback to single target logic (Standard Homing Projectile)
        let targetEntity =
          match target with
          | SystemCommunications.TargetEntity te -> Some te
          | _ -> None

        match targetEntity with
        | Some targetId ->
          let projectileId = Guid.NewGuid() |> UMX.tag<EntityId>

          let liveProjectile: Projectile.LiveProjectile = {
            Caster = casterId
            Target = targetId
            SkillId = skillId
            Info = projectileInfo
          }

          ctx.EventBus.Publish(
            StateChangeEvent.CreateProjectile
              struct (projectileId, liveProjectile, ValueNone)
          )
        | None -> ()

    let handleInstantDelivery
      (ctx: CombatContext)
      (casterId: Guid<EntityId>)
      (target: SystemCommunications.SkillTarget)
      (activeSkill: ActiveSkill)
      =
      let targetCenter =
        match target with
        | SystemCommunications.TargetEntity targetId ->
          ctx.Positions.TryFindV targetId
        | SystemCommunications.TargetPosition pos -> ValueSome pos
        // Default the center to the caster
        // allow the area to decide its own center based on the direction
        | SystemCommunications.TargetDirection _
        | SystemCommunications.TargetSelf -> ctx.Positions.TryFindV casterId

      match targetCenter with
      | ValueNone -> () // No center to apply AoE
      | ValueSome center ->
        let targets =
          match activeSkill.Area with
          | Point ->
            match target with
            | SystemCommunications.TargetSelf -> IndexList.single casterId
            | SystemCommunications.TargetEntity targetId ->
              IndexList.single targetId
            | SystemCommunications.TargetPosition _ -> IndexList.empty // Can't target entities with a point on the ground
            | SystemCommunications.TargetDirection _ -> IndexList.empty // Can't target entities with a direction
          | Circle(radius, maxTargets) ->
            let origin, effectiveRadius =
              Targeting.resolveCircle ctx casterId center radius target

            Spatial.Search.findTargetsInCircle ctx.SearchContext {
              CasterId = casterId
              Circle = {
                Center = origin
                Radius = effectiveRadius
              }
              MaxTargets = maxTargets
            }
          | Cone(angle, length, maxTargets) ->
            let origin, direction =
              Targeting.resolveCone ctx casterId center target

            Spatial.Search.findTargetsInCone ctx.SearchContext {
              CasterId = casterId
              Cone = {
                Origin = origin
                Direction = direction
                AngleDegrees = angle
                Length = length
              }
              MaxTargets = maxTargets
            }
          | Line(width, length, maxTargets) ->
            let start, endPoint =
              Targeting.resolveLine ctx casterId center length target

            Spatial.Search.findTargetsInLine ctx.SearchContext {
              CasterId = casterId
              Line = {
                Start = start
                End = endPoint
                Width = width
              }
              MaxTargets = maxTargets
            }
          | MultiPoint _ ->
            // This is for spawning multiple projectiles, which is handled in AbilityActivation.
            // If an instant skill has this, it's probably a bug in the skill definition.
            IndexList.empty
          | AdaptiveCone _ -> IndexList.empty

        for targetId in targets do
          Execution.applyInstantaneousSkillEffects
            ctx
            casterId
            targetId
            activeSkill


    let handleAbilityIntent
      (ctx: CombatContext)
      (casterId: Guid<EntityId>)
      (skillId: int<SkillId>)
      (target: SystemCommunications.SkillTarget)
      =
      match ctx.SkillStore.tryFind skillId with
      | ValueSome(Skill.Active activeSkill) ->
        // Apply cost and cooldown now that the ability is confirmed
        Execution.applyResourceCost ctx casterId activeSkill
        Execution.applyCooldown ctx casterId skillId activeSkill

        match activeSkill.Delivery with
        | Delivery.Projectile projectileInfo ->
          handleProjectileDelivery
            ctx
            casterId
            skillId
            target
            activeSkill
            projectileInfo
        | Delivery.Instant ->
          handleInstantDelivery ctx casterId target activeSkill
      | _ -> ()

    let handleProjectileImpact
      (ctx: CombatContext)
      (impact: SystemCommunications.ProjectileImpacted)
      =
      match ctx.SkillStore.tryFind impact.SkillId with
      | ValueSome(Active skill) ->
        let impactPosition = ctx.Positions.TryFindV impact.TargetId

        match impactPosition with
        | ValueNone -> () // Impacted entity has no position, should not happen
        | ValueSome center ->
          let targets =
            match skill.Area with
            | Point -> IndexList.single impact.TargetId
            | Circle(radius, maxTargets) ->
              Spatial.Search.findTargetsInCircle ctx.SearchContext {
                CasterId = impact.CasterId
                Circle = { Center = center; Radius = radius }
                MaxTargets = maxTargets
              }
            | Cone(angle, length, maxTargets) ->
              // For projectile impact, direction is from caster to impact point
              let casterPos = Targeting.getPosition ctx impact.CasterId
              let direction = Vector2.Normalize(center - casterPos)

              Spatial.Search.findTargetsInCone ctx.SearchContext {
                CasterId = impact.CasterId
                Cone = {
                  Origin = center
                  Direction = direction
                  AngleDegrees = angle
                  Length = length
                }
                MaxTargets = maxTargets
              }
            | Line(width, length, maxTargets) ->
              // For projectile impact, line is along the trajectory
              let casterPos = Targeting.getPosition ctx impact.CasterId
              let direction = Vector2.Normalize(center - casterPos)
              let endPoint = center + direction * length

              Spatial.Search.findTargetsInLine ctx.SearchContext {
                CasterId = impact.CasterId
                Line = {
                  Start = center
                  End = endPoint
                  Width = width
                }
                MaxTargets = maxTargets
              }
            | MultiPoint _ ->
              // This shouldn't happen. MultiPoint is for firing multiple projectiles,
              // not for the area of a single projectile impact.
              IndexList.single impact.TargetId
            | AdaptiveCone _ ->
              // This shouldn't happen. AdaptiveCone is gets calculated from selected targets/positions
              // then generates projectiles for each target/position. impacts should just deal damage to the
              // target/position. not creating new cones/projectiles.
              IndexList.single impact.TargetId

          for targetId in targets do
            let result =
              Execution.applySkillDamage ctx impact.CasterId targetId skill

            let targetPos = Targeting.getPosition ctx targetId

            if result.IsEvaded then
              ctx.EventBus.Publish(
                {
                  Message = "Miss"
                  Position = targetPos
                }
                : SystemCommunications.ShowNotification
              )
            else
              ctx.EventBus.Publish(
                {
                  Target = targetId
                  Amount = result.Amount
                }
                : SystemCommunications.DamageDealt
              )

              let baseMessage = $"-{result.Amount}"

              let debugText =
                match impact.RemainingJumps with
                | ValueSome jumps -> $" ({jumps} left)"
                | ValueNone -> ""

              let finalMessage = baseMessage + debugText

              ctx.EventBus.Publish(
                {
                  Message = finalMessage
                  Position = targetPos
                }
                : SystemCommunications.ShowNotification
              )

              if result.IsCritical then
                ctx.EventBus.Publish(
                  {
                    Message = "Crit!"
                    Position = targetPos
                  }
                  : SystemCommunications.ShowNotification
                )

              for effect in skill.Effects do
                ctx.EventBus.Publish(
                  {
                    SourceEntity = impact.CasterId
                    TargetEntity = targetId
                    Effect = effect
                  }
                  : SystemCommunications.EffectApplicationIntent
                )
      | _ -> () // Skill not found


  type CombatSystem(game: Game, mapKey: string) as this =
    inherit GameSystem(game)

    let eventBus = this.EventBus
    let skillStore = this.Game.Services.GetService<Stores.SkillStore>()
    let subscriptions = new CompositeDisposable()

    override this.Initialize() =
      base.Initialize()
      let derivedStats = this.Projections.DerivedStats
      let positions = this.Projections.UpdatedPositions
      let getNearbyEntities = this.Projections.GetNearbyEntities
      let mapStore = this.Game.Services.GetService<Stores.MapStore>()
      let mapDef = mapStore.find mapKey

      let searchCtx: Spatial.Search.SearchContext = {
        MapDef = mapDef
        GetNearbyEntities = getNearbyEntities
      }

      // make it inline to be sure the AMap forcing
      // is done in the subscription callbacks
      let inline createCtx() = {
        World = this.World
        EventBus = eventBus
        SearchContext = searchCtx
        DerivedStats = derivedStats |> AMap.force
        Positions = positions |> AMap.force
        SkillStore = skillStore
      }

      eventBus.GetObservableFor<SystemCommunications.AbilityIntent>()
      |> Observable.subscribe(fun event ->
        Handlers.handleAbilityIntent
          (createCtx())
          event.Caster
          event.SkillId
          event.Target)
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.ProjectileImpacted>()
      |> Observable.subscribe(fun event ->
        Handlers.handleProjectileImpact (createCtx()) event)
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.EffectDamageIntent>()
      |> Observable.subscribe(fun event ->
        Handlers.handleEffectDamageIntent (createCtx()) event)
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.EffectResourceIntent>()
      |> Observable.subscribe(fun event ->
        Handlers.handleEffectResourceIntent (createCtx()) event)
      |> subscriptions.Add

    override _.Dispose disposing =
      if disposing then
        subscriptions.Dispose()

      base.Dispose disposing

    override _.Kind = Combat
    override _.Update gameTime = base.Update gameTime

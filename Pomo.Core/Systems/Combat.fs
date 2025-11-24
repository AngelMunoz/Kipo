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

  module private Handlers =
    let applySkillDamage
      (world: World)
      (derivedStats: HashMap<Guid<EntityId>, Entity.DerivedStats>)
      (casterId: Guid<EntityId>)
      (targetId: Guid<EntityId>)
      (skill: ActiveSkill)
      =

      match derivedStats.TryFindV casterId, derivedStats.TryFindV targetId with
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
      (derivedStats: HashMap<Guid<EntityId>, Entity.DerivedStats>)
      (positions: HashMap<Guid<EntityId>, Vector2>)
      (casterId: Guid<EntityId>)
      (targetId: Guid<EntityId>)
      (activeSkill: ActiveSkill)
      =

      let result =
        applySkillDamage world derivedStats casterId targetId activeSkill

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

    let handleEffectResourceIntent
      (derivedStats: amap<Guid<EntityId>, Entity.DerivedStats>)
      (eventBus: EventBus)
      (intent: SystemCommunications.EffectResourceIntent)
      =
      let derivedStats = derivedStats |> AMap.force

      match derivedStats.TryFindV intent.SourceEntity with
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

              {
                Amount = changeAmount
                ResourceType = resource
                Target = intent.TargetEntity
              }
              : SystemCommunications.ResourceRestored
              |> Some
            | _ -> None)

        for resourceChangeEvent in totalResourceChangeFromModifiers do
          eventBus.Publish resourceChangeEvent

        eventBus.Publish(
          EffectExpired struct (intent.TargetEntity, intent.ActiveEffectId)
        )
      | _ -> () // Attacker stats not found

    let handleEffectDamageIntent
      (derivedStats: amap<Guid<EntityId>, Entity.DerivedStats>)
      (positions: amap<Guid<EntityId>, Vector2>)
      (eventBus: EventBus)
      (intent: SystemCommunications.EffectDamageIntent)
      =
      let positions = positions |> AMap.force
      let derivedStats = derivedStats |> AMap.force

      match
        derivedStats.TryFindV intent.SourceEntity,
        derivedStats.TryFindV intent.TargetEntity
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
      (searchCtx: Spatial.Search.SearchContext)
      (derivedStats: amap<Guid<EntityId>, Entity.DerivedStats>)
      (positions: amap<Guid<EntityId>, Vector2>)
      (skillStore: Stores.SkillStore)
      (casterId: Guid<EntityId>)
      (skillId: int<SkillId>)
      (target: SystemCommunications.SkillTarget)
      =
      let positions = positions |> AMap.force
      let derivedStats = derivedStats |> AMap.force

      match skillStore.tryFind skillId with
      | ValueSome(Skill.Active activeSkill) ->
        // Apply cost and cooldown now that the ability is confirmed
        let totalGameTime = world.Time |> AVal.map _.TotalGameTime |> AVal.force

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
          
          // Handle Multi-Target Projectiles (Fan of Knives, etc.)
          let targets =
            match activeSkill.Area with
            | AdaptiveCone(length, maxTargets) ->
                let origin, direction, angle =
                    match target with
                    | SystemCommunications.TargetPosition pos ->
                        let casterPos =
                            positions
                            |> HashMap.tryFindV casterId
                            |> ValueOption.defaultValue Vector2.Zero
                        
                        let offset = pos - casterPos
                        let dist = offset.Length()

                        let dir =
                            if dist > 0.001f then Vector2.Normalize(offset)
                            else Vector2.UnitX
                        
                        // Reference forward vector (e.g., along positive Y for isometric, or check caster facing)
                        // Assuming Caster's forward is `Vector2.UnitY` for simplicity or use specific entity facing
                        let referenceForward = Vector2.UnitY // Or caster's actual facing vector

                        // Angle of target relative to reference forward (in degrees)
                        let angleFromForwardRad = MathF.Acos(Vector2.Dot(referenceForward, dir))
                        let angleFromForwardDeg = MathHelper.ToDegrees(angleFromForwardRad)

                        // Map 0-90 degrees from forward to 30-180 degrees aperture
                        // This assumes symmetrical spread. If not, needs left/right calc.
                        // Aperture = 30 + (angleFromForwardDeg / 90.0) * 150.0
                        let apertureAngle = 
                            if angleFromForwardDeg <= 90.0f then
                                30.0f + (angleFromForwardDeg / 90.0f) * 150.0f
                            else // If target is behind (90-180 deg), mirror the angle or cap. 
                                // For now, let's just cap at max if it goes beyond 90 (very wide spread)
                                180.0f // Or 30.0f + ((180.0f - angleFromForwardDeg) / 90.0f) * 150.0f 
                        
                        casterPos, dir, apertureAngle
                        
                    | SystemCommunications.TargetDirection pos ->
                         // Similar to TargetPosition but direction is explicit? 
                         // Usually TargetDirection implies origin is caster.
                        let casterPos =
                            positions
                            |> HashMap.tryFindV casterId
                            |> ValueOption.defaultValue Vector2.Zero
                        
                        let offset = pos - casterPos
                        let dist = offset.Length()

                        let dir =
                            if dist > 0.001f then Vector2.Normalize(offset)
                            else Vector2.UnitX
                            
                        // Reference forward vector
                        let referenceForward = Vector2.UnitY 
                        let angleFromForwardRad = MathF.Acos(Vector2.Dot(referenceForward, dir))
                        let angleFromForwardDeg = MathHelper.ToDegrees(angleFromForwardRad)

                        let apertureAngle = 
                            if angleFromForwardDeg <= 90.0f then
                                30.0f + (angleFromForwardDeg / 90.0f) * 150.0f
                            else 
                                180.0f 
                                
                        casterPos, dir, apertureAngle
                        
                    | _ -> Vector2.Zero, Vector2.UnitX, 0.0f // Fallback/Invalid

                if angle > 0.0f then
                    Spatial.Search.findTargetsInCone
                        searchCtx
                        casterId
                        origin
                        direction
                        angle
                        length
                        maxTargets
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
                eventBus.Publish(
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

                eventBus.Publish(
                  StateChangeEvent.CreateProjectile
                    struct (projectileId, liveProjectile, ValueNone)
                )
              | None -> ()
        | Delivery.Instant ->

          let targetCenter =
            match target with
            | SystemCommunications.TargetEntity targetId ->
              positions.TryFindV targetId
            | SystemCommunications.TargetPosition pos -> ValueSome pos
            // Default the center to the caster
            // allow the area to decide its own center based on the direction
            | SystemCommunications.TargetDirection _
            | SystemCommunications.TargetSelf -> positions.TryFindV casterId

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
                  match target with
                  | SystemCommunications.TargetDirection pos ->
                    // Origin is caster. Radius is distance to cursor, clamped by skill radius.
                    let casterPos =
                      positions
                      |> HashMap.tryFindV casterId
                      |> ValueOption.defaultValue center

                    let dist = Vector2.Distance(casterPos, pos)
                    let r = Math.Min(dist, radius)
                    casterPos, r
                  | _ -> center, radius

                Spatial.Search.findTargetsInCircle
                  searchCtx
                  casterId
                  origin
                  effectiveRadius
                  maxTargets
              | Cone(angle, length, maxTargets) ->
                let origin, direction =
                  match target with
                  | SystemCommunications.TargetDirection pos ->
                    // Origin is caster, direction is towards target position
                    let casterPos =
                      positions
                      |> HashMap.tryFindV casterId
                      |> ValueOption.defaultValue center

                    let offset = pos - casterPos

                    let dir =
                      if offset = Vector2.Zero then
                        Vector2.UnitX
                      else
                        Vector2.Normalize(offset)

                    casterPos, dir

                  | SystemCommunications.TargetPosition pos ->
                    // Origin is the target position. Direction is from caster to position.
                    let casterPos =
                      positions
                      |> HashMap.tryFindV casterId
                      |> ValueOption.defaultValue center

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
                    let casterPos =
                      positions
                      |> HashMap.tryFindV casterId
                      |> ValueOption.defaultValue center

                    let targetPos =
                      positions
                      |> HashMap.tryFindV targetId
                      |> ValueOption.defaultValue center

                    let offset = targetPos - casterPos

                    let dir =
                      if offset = Vector2.Zero then
                        Vector2.UnitX
                      else
                        Vector2.Normalize(offset)

                    targetPos, dir

                  | _ -> center, Vector2.UnitX

                Spatial.Search.findTargetsInCone
                  searchCtx
                  casterId
                  origin
                  direction
                  angle
                  length
                  maxTargets
              | Line(width, length, maxTargets) ->
                let start, endPoint =
                  match target with
                  | SystemCommunications.TargetDirection pos ->
                    // Start at caster, end at caster + direction * length
                    let casterPos =
                      positions
                      |> HashMap.tryFindV casterId
                      |> ValueOption.defaultValue center

                    let offset = pos - casterPos

                    let dir =
                      if offset = Vector2.Zero then
                        Vector2.UnitX
                      else
                        Vector2.Normalize(offset)

                    casterPos, casterPos + dir * length

                  | SystemCommunications.TargetPosition pos ->
                    // Start at caster. End at position, clamped by length.
                    let casterPos =
                      positions
                      |> HashMap.tryFindV casterId
                      |> ValueOption.defaultValue center

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
                    let casterPos =
                      positions
                      |> HashMap.tryFindV casterId
                      |> ValueOption.defaultValue center

                    let targetPos =
                      positions
                      |> HashMap.tryFindV targetId
                      |> ValueOption.defaultValue center

                    let offset = targetPos - casterPos

                    let dir =
                      if offset = Vector2.Zero then
                        Vector2.UnitX
                      else
                        Vector2.Normalize(offset)

                    targetPos, targetPos + dir * length

                  | _ -> center, center + Vector2.UnitX * length

                Spatial.Search.findTargetsInLine
                  searchCtx
                  casterId
                  start
                  endPoint
                  width
                  maxTargets
              | MultiPoint _ ->
                // This is for spawning multiple projectiles, which is handled in AbilityActivation.
                // If an instant skill has this, it's probably a bug in the skill definition.
                IndexList.empty
              | AdaptiveCone _ -> IndexList.empty

            for targetId in targets do
              applyInstantaneousSkillEffects
                world
                eventBus
                derivedStats
                positions
                casterId
                targetId
                activeSkill
      | _ -> ()

    let handleProjectileImpact
      (
        world: World,
        eventBus: EventBus,
        derivedStats: amap<Guid<EntityId>, Entity.DerivedStats>,
        positions: amap<Guid<EntityId>, Vector2>,
        skillStore: Stores.SkillStore,
        searchCtx: Spatial.Search.SearchContext
      )
      (impact: SystemCommunications.ProjectileImpacted)
      =
      let positions = positions |> AMap.force
      let derivedStats = derivedStats |> AMap.force

      match skillStore.tryFind impact.SkillId with
      | ValueSome(Active skill) ->
        let impactPosition = positions.TryFindV impact.TargetId

        match impactPosition with
        | ValueNone -> () // Impacted entity has no position, should not happen
        | ValueSome center ->
          let targets =
            match skill.Area with
            | Point -> IndexList.single impact.TargetId
            | Circle(radius, maxTargets) ->

              Spatial.Search.findTargetsInCircle
                searchCtx
                impact.CasterId
                center
                radius
                maxTargets
            | Cone(angle, length, maxTargets) ->
              // For projectile impact, direction is from caster to impact point
              let casterPos =
                positions
                |> HashMap.tryFindV impact.CasterId
                |> ValueOption.defaultValue center

              let direction = Vector2.Normalize(center - casterPos)

              Spatial.Search.findTargetsInCone
                searchCtx
                impact.CasterId
                center
                direction
                angle
                length
                maxTargets
            | Line(width, length, maxTargets) ->
              // For projectile impact, line is along the trajectory
              let casterPos =
                positions
                |> HashMap.tryFindV impact.CasterId
                |> ValueOption.defaultValue center

              let direction = Vector2.Normalize(center - casterPos)
              let endPoint = center + direction * length

              Spatial.Search.findTargetsInLine
                searchCtx
                impact.CasterId
                center
                endPoint
                width
                maxTargets
            | MultiPoint _ ->
              // This shouldn't happen. MultiPoint is for firing multiple projectiles,
              // not for the area of a single projectile impact.
              IndexList.single impact.TargetId
            | AdaptiveCone _ -> IndexList.single impact.TargetId

          for targetId in targets do
            let result =
              applySkillDamage world derivedStats impact.CasterId targetId skill

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

              let baseMessage = $"-{result.Amount}"

              let debugText =
                match impact.RemainingJumps with
                | ValueSome jumps -> $" ({jumps} left)"
                | ValueNone -> ""

              let finalMessage = baseMessage + debugText

              eventBus.Publish(
                {
                  Message = finalMessage
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
                eventBus.Publish(
                  {
                    SourceEntity = impact.CasterId
                    TargetEntity = targetId
                    Effect = effect
                  }
                  : SystemCommunications.EffectApplicationIntent
                )
      | _ -> () // Skill not found

    let inline handleAbilityIntentEvent
      (
        world: World,
        eventBus: EventBus,
        derivedStats: amap<Guid<EntityId>, Entity.DerivedStats>,
        positions: amap<Guid<EntityId>, Vector2>,
        skillStore: Stores.SkillStore,
        searchCtx: Spatial.Search.SearchContext
      )
      (event: SystemCommunications.AbilityIntent)
      =
      handleAbilityIntent
        world
        eventBus
        searchCtx
        derivedStats
        positions
        skillStore
        event.Caster
        event.SkillId
        event.Target


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

      eventBus.GetObservableFor<SystemCommunications.AbilityIntent>()
      |> Observable.subscribe(
        Handlers.handleAbilityIntentEvent(
          this.World,
          eventBus,
          derivedStats,
          positions,
          skillStore,
          searchCtx
        )
      )
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.ProjectileImpacted>()
      |> Observable.subscribe(
        Handlers.handleProjectileImpact(
          this.World,
          eventBus,
          derivedStats,
          positions,
          skillStore,
          searchCtx
        )
      )
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.EffectDamageIntent>()
      |> Observable.subscribe(
        Handlers.handleEffectDamageIntent derivedStats positions eventBus
      )
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.EffectResourceIntent>()
      |> Observable.subscribe(
        Handlers.handleEffectResourceIntent derivedStats eventBus
      )
      |> subscriptions.Add

    override _.Dispose disposing =
      if disposing then
        subscriptions.Dispose()

      base.Dispose disposing

    override _.Kind = Combat

    override _.Update gameTime = base.Update gameTime

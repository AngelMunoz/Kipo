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
open Pomo.Core.Domain.Particles
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Skill
open Pomo.Core.Systems
open Pomo.Core.Systems.Systems
open Pomo.Core.Systems.DamageCalculator

module Combat =
  open System.Reactive.Disposables

  [<Struct>]
  type EntityContext = {
    Cooldowns: HashMap<Guid<EntityId>, HashMap<int<SkillId>, TimeSpan>>
    Resources: HashMap<Guid<EntityId>, Entity.Resource>
    DerivedStats: HashMap<Guid<EntityId>, Entity.DerivedStats>
    Positions: HashMap<Guid<EntityId>, Vector2>
  }

  type WorldContext = {
    Time: Time
    Rng: Random
    EventBus: EventBus
    EntityContext: EntityContext
    SearchContext: Spatial.Search.SearchContext
    SkillStore: Stores.SkillStore
    ParticleStore: Stores.ParticleStore
    VisualEffects: ResizeArray<Particles.ActiveEffect>
  }

  module private Targeting =

    let getPosition (ctx: EntityContext) (entityId: Guid<EntityId>) =
      ctx.Positions
      |> HashMap.tryFindV entityId
      |> ValueOption.defaultValue Vector2.Zero

    let resolveCircle
      (ctx: EntityContext)
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
      (ctx: WorldContext)
      (casterId: Guid<EntityId>)
      (center: Vector2)
      (target: SystemCommunications.SkillTarget)
      =
      let casterPos = getPosition ctx.EntityContext casterId

      match target with
      | SystemCommunications.TargetDirection pos ->
        // Origin is caster, direction is towards target position
        let offset = pos - casterPos

        let dir =
          if offset = Vector2.Zero then
            Vector2.UnitX
          else
            Vector2.Normalize offset

        casterPos, dir
      | SystemCommunications.TargetPosition pos ->
        // Origin is the target position. Direction is from caster to position.
        let offset = pos - casterPos

        let dir =
          if offset = Vector2.Zero then
            Vector2.UnitX
          else
            Vector2.Normalize offset

        pos, dir
      | SystemCommunications.TargetEntity targetId ->
        // Origin is target entity.
        // Direction is from caster to target entity ("shatter")
        let targetPos = getPosition ctx.EntityContext targetId
        let offset = targetPos - casterPos

        let dir =
          if offset = Vector2.Zero then
            Vector2.UnitX
          else
            Vector2.Normalize offset

        targetPos, dir
      | _ -> center, Vector2.UnitX

    let resolveLine
      (ctx: WorldContext)
      (casterId: Guid<EntityId>)
      (center: Vector2)
      (length: float32)
      (target: SystemCommunications.SkillTarget)
      =
      let casterPos = getPosition ctx.EntityContext casterId

      match target with
      | SystemCommunications.TargetDirection pos ->
        // Start at caster, end at caster + direction * length
        let offset = pos - casterPos

        let dir =
          if offset = Vector2.Zero then
            Vector2.UnitX
          else
            Vector2.Normalize offset

        casterPos, casterPos + dir * length
      | SystemCommunications.TargetPosition pos ->
        // Start at caster. End at position, clamped by length.
        let offset = pos - casterPos
        let dist = offset.Length()

        let dir =
          if dist > 0.0f then
            Vector2.Normalize offset
          else
            Vector2.UnitX

        let actualLength = Math.Min(dist, length)
        casterPos, casterPos + dir * actualLength
      | SystemCommunications.TargetEntity targetId ->
        // Start at target entity.
        // Direction from caster to target entity.
        let targetPos = getPosition ctx.EntityContext targetId
        let offset = targetPos - casterPos

        let dir =
          if offset = Vector2.Zero then
            Vector2.UnitX
          else
            Vector2.Normalize offset

        targetPos, targetPos + dir * length
      | _ -> center, center + Vector2.UnitX * length

    let calculateAdaptiveAperture(direction: Vector2) =
      // Reference forward vector (e.g., along positive Y for isometric, or check caster facing)
      // Assuming Caster's forward is `Vector2.UnitY` for simplicity or use specific entity facing
      let referenceForward = Vector2.UnitY

      // Angle of target relative to reference forward (in degrees)
      let angleFromForwardRad =
        MathF.Acos(Vector2.Dot(referenceForward, direction))

      let angleFromForwardDeg = MathHelper.ToDegrees angleFromForwardRad

      // Map 0-90 degrees from forward to 30-180 degrees aperture
      // This assumes symmetrical spread. If not, needs left/right calc.
      // Aperture = 30 + (angleFromForwardDeg / 90.0) * 150.0
      if angleFromForwardDeg <= 90.0f then
        30.0f + angleFromForwardDeg / 90.0f * 150.0f
      else // If target is behind (90-180 deg), mirror the angle or cap.
        // For now, let's just cap at max if it goes beyond 90 (very wide spread)
        180.0f

    let resolveAdaptiveCone
      (ctx: WorldContext)
      (casterId: Guid<EntityId>)
      (target: SystemCommunications.SkillTarget)
      =
      let casterPos = getPosition ctx.EntityContext casterId

      match target with
      | SystemCommunications.TargetPosition pos ->
        let offset = pos - casterPos
        let dist = offset.Length()

        let dir =
          if dist > 0.001f then
            Vector2.Normalize offset
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
            Vector2.Normalize offset
          else
            Vector2.UnitX

        let apertureAngle = calculateAdaptiveAperture dir
        casterPos, dir, apertureAngle
      | _ -> Vector2.Zero, Vector2.UnitX, 0.0f // Fallback/Invalid

  module private Execution =

    let applySkillDamage
      (ctx: WorldContext)
      (casterId: Guid<EntityId>)
      (targetId: Guid<EntityId>)
      (skill: ActiveSkill)
      =
      match
        ctx.EntityContext.DerivedStats.TryFindV casterId,
        ctx.EntityContext.DerivedStats.TryFindV targetId
      with
      | ValueSome attackerStats, ValueSome defenderStats ->
        if casterId = targetId then
          DamageCalculator.calculateRawDamageSelfTarget
            ctx.Rng
            attackerStats
            defenderStats
            skill
        else
          DamageCalculator.calculateFinalDamage
            ctx.Rng
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
      (ctx: WorldContext)
      (casterId: Guid<EntityId>)
      (targetId: Guid<EntityId>)
      (activeSkill: ActiveSkill)
      =
      let result = applySkillDamage ctx casterId targetId activeSkill
      let targetPos = Targeting.getPosition ctx.EntityContext targetId

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
      (ctx: WorldContext)
      (casterId: Guid<EntityId>)
      (activeSkill: ActiveSkill)
      =
      match activeSkill.Cost with
      | ValueSome cost ->
        let resources = ctx.EntityContext.Resources

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
      (ctx: WorldContext)
      (casterId: Guid<EntityId>)
      (skillId: int<SkillId>)
      (activeSkill: ActiveSkill)
      =
      match activeSkill.Cooldown with
      | ValueSome cd ->
        let totalGameTime = ctx.Time.TotalGameTime

        let cooldowns = ctx.EntityContext.Cooldowns

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


  module private Visuals =
    let spawnEffect
      (ctx: WorldContext)
      (vfxId: string)
      (pos: Vector2)
      (rotation: Quaternion)
      =
      match ctx.ParticleStore.tryFind vfxId with
      | ValueSome configs ->
        let emitters =
          configs
          |> List.map(fun config -> ({
            Config = config
            Particles = ResizeArray()
            Accumulator = ref 0.0f
            BurstDone = ref false
          } : Pomo.Core.Domain.Particles.ActiveEmitter))
          |> ResizeArray

        let effect: Pomo.Core.Domain.Particles.ActiveEffect = {
          Id = System.Guid.NewGuid().ToString()
          Emitters = emitters |> Seq.toList
          Position = ref(Vector3(pos.X, 0.0f, pos.Y))
          Rotation = ref rotation
          Scale = ref Vector3.One
          IsAlive = ref true
          Owner = ValueNone
        }

        ctx.VisualEffects.Add(effect)
      | ValueNone -> ()

  module private Handlers =

    let handleEffectResourceIntent
      (ctx: WorldContext)
      (intent: SystemCommunications.EffectResourceIntent)
      =
      match ctx.EntityContext.DerivedStats.TryFindV intent.SourceEntity with
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
      (ctx: WorldContext)
      (intent: SystemCommunications.EffectDamageIntent)
      =
      match
        ctx.EntityContext.DerivedStats.TryFindV intent.SourceEntity,
        ctx.EntityContext.DerivedStats.TryFindV intent.TargetEntity
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

          let targetPos =
            Targeting.getPosition ctx.EntityContext intent.TargetEntity

          ctx.EventBus.Publish(
            {
              Message = $"-{totalDamageFromModifiers}"
              Position = targetPos
            }
            : SystemCommunications.ShowNotification
          )
      | _ -> ()

    let handleProjectileDelivery
      (ctx: WorldContext)
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
      (ctx: WorldContext)
      (casterId: Guid<EntityId>)
      (target: SystemCommunications.SkillTarget)
      (activeSkill: ActiveSkill)
      =
      let targetCenter =
        match target with
        | SystemCommunications.TargetEntity targetId ->
          ctx.EntityContext.Positions.TryFindV targetId
        | SystemCommunications.TargetPosition pos -> ValueSome pos
        // Default the center to the caster
        // allow the area to decide its own center based on the direction
        | SystemCommunications.TargetDirection _
        | SystemCommunications.TargetSelf ->
          ctx.EntityContext.Positions.TryFindV casterId

      match targetCenter with
      | ValueNone -> () // No center to apply AoE
      | ValueSome center ->
        // Spawn Visuals
        match activeSkill.ImpactVisuals.VfxId with
        | ValueSome vfxId ->
          // Calculate Rotation based on target direction
          // Center is caster pos usually for cones.
          let casterPos = Targeting.getPosition ctx.EntityContext casterId

          let direction =
            match target with
            | SystemCommunications.TargetDirection pos ->
              Vector2.Normalize(pos - casterPos)
            | SystemCommunications.TargetPosition pos ->
              Vector2.Normalize(pos - casterPos)
            | SystemCommunications.TargetEntity targetId ->
              let tPos = Targeting.getPosition ctx.EntityContext targetId
              Vector2.Normalize(tPos - casterPos)
            | _ -> Vector2.UnitY // Default Forward?

          // Convert 2D direction (Top Down Y=Depth) to 3D Rotation (Yaw)
          // X -> X, Y -> Z.
          // Angle is atan2(Y, X).
          // Quaternion Yaw is around Y-axis.
          // In standard Math, 0 is X+.
          // In our game, 0 Yaw is usually -Z?
          // Let's use atan2(Y, X) and rotate around Up.
          let angle = MathF.Atan2(direction.Y, direction.X)
          // Adjust for coordinate system mismatch?
          // If Y+ is "Down/Depth", and X+ is "Right".
          // MathF.Atan2(1, 0) = PI/2 (Down).
          // Our Rotation should align the Cone (Forward) to this.
          // If Cone Forward is -Z (0,0,-1).
          // And direction (0, 1) means +Z.
          // We need to rotate 180 deg?
          // Let's assume standard mapping: Yaw = -angle.
          let rotation = Quaternion.CreateFromAxisAngle(Vector3.Up, -angle)

          Visuals.spawnEffect ctx vfxId center rotation
        | ValueNone -> ()

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
              Targeting.resolveCircle
                ctx.EntityContext
                casterId
                center
                radius
                target

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
      (ctx: WorldContext)
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
      (ctx: WorldContext)
      (impact: SystemCommunications.ProjectileImpacted)
      =
      match ctx.SkillStore.tryFind impact.SkillId with
      | ValueSome(Active skill) ->
        let impactPosition =
          ctx.EntityContext.Positions.TryFindV impact.TargetId

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
              let casterPos =
                Targeting.getPosition ctx.EntityContext impact.CasterId

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
              let casterPos =
                Targeting.getPosition ctx.EntityContext impact.CasterId

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

            let targetPos = Targeting.getPosition ctx.EntityContext targetId

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


  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type CombatSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices
    let (Gameplay gameplay) = env.GameplayServices

    let eventBus = core.EventBus
    let skillStore = stores.SkillStore
    let subscriptions = new CompositeDisposable()

    override this.Initialize() =
      base.Initialize()
      let derivedStats = gameplay.Projections.DerivedStats

      // make it inline to be sure the AMap forcing
      // is done in the subscription callbacks
      // make it inline to be sure the AMap forcing
      // is done in the subscription callbacks
      let inline createCtx(entityId: Guid<EntityId>) =
        let entityScenarios = gameplay.Projections.EntityScenarios |> AMap.force

        match entityScenarios |> HashMap.tryFindV entityId with
        | ValueSome scenarioId ->
          let snapshot =
            gameplay.Projections.ComputeMovementSnapshot(scenarioId)

          let getNearbyEntities center radius =
            gameplay.Projections.GetNearbyEntitiesSnapshot(
              snapshot,
              center,
              radius
            )

          let searchCtx: Spatial.Search.SearchContext = {
            GetNearbyEntities = getNearbyEntities
          }

          let entityContext = {
            Positions = snapshot.Positions
            Cooldowns = core.World.AbilityCooldowns |> AMap.force
            Resources = core.World.Resources |> AMap.force
            DerivedStats = derivedStats |> AMap.force
          }

          ValueSome {
            Rng = core.World.Rng
            Time = core.World.Time |> AVal.force
            EventBus = eventBus
            SearchContext = searchCtx
            EntityContext = entityContext
            SkillStore = skillStore
            ParticleStore = stores.ParticleStore
            VisualEffects = core.World.VisualEffects
          }
        | ValueNone -> ValueNone

      eventBus.GetObservableFor<SystemCommunications.AbilityIntent>()
      |> Observable.subscribe(fun event ->
        match createCtx event.Caster with
        | ValueSome ctx ->
          Handlers.handleAbilityIntent
            ctx
            event.Caster
            event.SkillId
            event.Target
        | ValueNone -> ())
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.ProjectileImpacted>()
      |> Observable.subscribe(fun event ->
        match createCtx event.CasterId with
        | ValueSome ctx -> Handlers.handleProjectileImpact ctx event
        | ValueNone -> ())
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.EffectDamageIntent>()
      |> Observable.subscribe(fun event ->
        match createCtx event.SourceEntity with
        | ValueSome ctx -> Handlers.handleEffectDamageIntent ctx event
        | ValueNone -> ())
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.EffectResourceIntent>()
      |> Observable.subscribe(fun event ->
        match createCtx event.SourceEntity with
        | ValueSome ctx -> Handlers.handleEffectResourceIntent ctx event
        | ValueNone -> ())
      |> subscriptions.Add

    override _.Dispose disposing =
      if disposing then
        subscriptions.Dispose()

      base.Dispose disposing

    override _.Kind = Combat
    override _.Update gameTime = base.Update gameTime

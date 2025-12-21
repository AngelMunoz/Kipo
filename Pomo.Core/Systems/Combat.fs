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
  open Pomo.Core.Environment

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
    StateWrite: IStateWriteService
    EntityContext: EntityContext
    SearchContext: Spatial.Search.SearchContext
    SkillStore: Stores.SkillStore
    ParticleStore: Stores.ParticleStore
    VisualEffects: ResizeArray<Particles.ActiveEffect>
    ActiveOrbitals: HashMap<Guid<EntityId>, Orbital.ActiveOrbital>
  }

  module private Targeting =

    let inline getPosition (ctx: EntityContext) (entityId: Guid<EntityId>) =
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
          GameEvent.Notification(
            NotificationEvent.ShowMessage {
              Message = "Miss"
              Position = targetPos
            }
          )
        )
      else
        ctx.EventBus.Publish(
          GameEvent.Notification(
            NotificationEvent.DamageDealt {
              Target = targetId
              Amount = result.Amount
            }
          )
        )

        ctx.EventBus.Publish(
          GameEvent.Notification(
            NotificationEvent.ShowMessage {
              Message = $"-{result.Amount}"
              Position = targetPos
            }
          )
        )

        if result.IsCritical then
          ctx.EventBus.Publish(
            GameEvent.Notification(
              NotificationEvent.ShowMessage {
                Message = "Crit!"
                Position = targetPos
              }
            )
          )

        for effect in activeSkill.Effects do
          let intent: SystemCommunications.EffectApplicationIntent = {
            SourceEntity = casterId
            TargetEntity = targetId
            Effect = effect
          }

          ctx.EventBus.Publish(
            GameEvent.Intent(IntentEvent.EffectApplication intent)
          )

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

        ctx.StateWrite.UpdateResources(casterId, newResources)
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

        ctx.StateWrite.UpdateCooldowns(casterId, newCooldowns)
      | ValueNone -> ()


  module private Visuals =
    let spawnEffect
      (ctx: WorldContext)
      (vfxId: string)
      (pos: Vector2)
      (rotation: Quaternion)
      (area: SkillArea)
      =
      ctx.ParticleStore.tryFind vfxId
      |> ValueOption.iter(fun configs ->
        let struct (billboardEmitters, meshEmitters) =
          Particles.splitEmittersByRenderMode configs

        let effect: Particles.ActiveEffect = {
          Id = System.Guid.NewGuid().ToString()
          Emitters = billboardEmitters
          MeshEmitters = meshEmitters
          Position = ref(Vector3(pos.X, 0.0f, pos.Y))
          Rotation = ref rotation
          Scale = ref Vector3.One
          IsAlive = ref true
          Owner = ValueNone
          Overrides = {
            EffectOverrides.empty with
                Rotation = ValueSome rotation
                Area = ValueSome area
          }
        }

        ctx.VisualEffects.Add effect)

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
                GameEvent.Notification(
                  NotificationEvent.ResourceRestored {
                    Amount = changeAmount
                    ResourceType = resource
                    Target = intent.TargetEntity
                  }
                )
              )
            | _ -> None)

        for resourceChangeEvent in totalResourceChangeFromModifiers do
          ctx.EventBus.Publish resourceChangeEvent

        ctx.StateWrite.ExpireEffect(intent.TargetEntity, intent.ActiveEffectId)
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
        let mutable totalDamage = 0

        for m in intent.Effect.Modifiers do
          match m with
          | AbilityDamageMod(mathExpr, element) ->
            totalDamage <-
              totalDamage
              + DamageCalculator.calculateEffectDamage
                  attackerStats
                  defenderStats
                  mathExpr
                  intent.Effect.DamageSource
                  element
          | _ -> ()

        if totalDamage > 0 then
          ctx.EventBus.Publish(
            GameEvent.Notification(
              NotificationEvent.DamageDealt {
                Target = intent.TargetEntity
                Amount = totalDamage
              }
            )
          )

          let targetPos =
            Targeting.getPosition ctx.EntityContext intent.TargetEntity

          ctx.EventBus.Publish(
            GameEvent.Notification(
              NotificationEvent.ShowMessage {
                Message = $"-{totalDamage}"
                Position = targetPos
              }
            )
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
            Target = Projectile.EntityTarget targetId
            SkillId = skillId
            Info = projectileInfo
          }

          ctx.StateWrite.CreateProjectile(
            projectileId,
            liveProjectile,
            ValueNone
          )
      else
        // Fallback to single target logic
        match target with
        | SystemCommunications.TargetEntity targetId ->
          // Standard Homing Projectile - targets an entity
          let projectileId = Guid.NewGuid() |> UMX.tag<EntityId>

          let liveProjectile: Projectile.LiveProjectile = {
            Caster = casterId
            Target = Projectile.EntityTarget targetId
            SkillId = skillId
            Info = projectileInfo
          }

          ctx.StateWrite.CreateProjectile(
            projectileId,
            liveProjectile,
            ValueNone
          )
        | SystemCommunications.TargetPosition targetPos ->
          // Position-targeted projectile (e.g., falling boulder)
          let projectileId = Guid.NewGuid() |> UMX.tag<EntityId>

          let liveProjectile: Projectile.LiveProjectile = {
            Caster = casterId
            Target = Projectile.PositionTarget targetPos
            SkillId = skillId
            Info = projectileInfo
          }

          // Start projectile at caster position
          ctx.StateWrite.CreateProjectile(
            projectileId,
            liveProjectile,
            ValueNone
          )
        | _ -> ()

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
          // Yaw to face target: PI/2 - angle maps Logic direction to Particle space
          // (Particle +Z = Logic South, so East requires +90deg yaw)
          let yaw =
            Quaternion.CreateFromAxisAngle(
              Vector3.Up,
              -angle + MathHelper.PiOver2
            )

          // Pitch to re-orient "Up" particles to "Forward"
          let pitch =
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.PiOver2)

          let rotation = yaw * pitch

          Visuals.spawnEffect ctx vfxId center rotation activeSkill.Area
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
      | ValueSome(Active activeSkill) ->
        Execution.applyResourceCost ctx casterId activeSkill
        Execution.applyCooldown ctx casterId skillId activeSkill

        // Check if skill has a charge phase
        match activeSkill.ChargePhase with
        | ValueSome chargeConfig ->
          // Create ActiveOrbital if needed
          chargeConfig.Orbitals
          |> ValueOption.iter(fun orbitalConfig ->
            let activeOrbital: Orbital.ActiveOrbital = {
              Center = Orbital.EntityCenter casterId
              Config = orbitalConfig
              StartTime = float32 ctx.Time.TotalGameTime.TotalSeconds
            }


            ctx.StateWrite.UpdateActiveOrbital(casterId, activeOrbital))

          // Create ActiveCharge
          let activeCharge: ActiveCharge = {
            SkillId = skillId
            Target = target
            StartTime = ctx.Time.TotalGameTime
            Duration = TimeSpan.FromSeconds(float chargeConfig.Duration)
          }

          ctx.StateWrite.UpdateActiveCharge(casterId, activeCharge)

        | ValueNone ->
          // No charge phase - execute delivery immediately
          match activeSkill.Delivery with
          | Projectile projectileInfo ->
            handleProjectileDelivery
              ctx
              casterId
              skillId
              target
              activeSkill
              projectileInfo
          | Instant -> handleInstantDelivery ctx casterId target activeSkill
      | _ -> ()


    let handleProjectileImpact
      (ctx: WorldContext)
      (impact: SystemCommunications.ProjectileImpacted)
      =
      match ctx.SkillStore.tryFind impact.SkillId with
      | ValueSome(Active skill) ->
        let center = impact.ImpactPosition

        let targets =
          match skill.Area with
          | Point ->
            match impact.TargetEntity with
            | ValueSome targetId -> IndexList.single targetId
            | ValueNone -> IndexList.empty
          | Circle(radius, maxTargets) ->
            Spatial.Search.findTargetsInCircle ctx.SearchContext {
              CasterId = impact.CasterId
              Circle = { Center = center; Radius = radius }
              MaxTargets = maxTargets
            }
          | Cone(angle, length, maxTargets) ->
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
            match impact.TargetEntity with
            | ValueSome targetId -> IndexList.single targetId
            | ValueNone -> IndexList.empty
          | AdaptiveCone _ ->
            match impact.TargetEntity with
            | ValueSome targetId -> IndexList.single targetId
            | ValueNone -> IndexList.empty

        for targetId in targets do
          let result =
            Execution.applySkillDamage ctx impact.CasterId targetId skill

          let targetPos = Targeting.getPosition ctx.EntityContext targetId

          if result.IsEvaded then
            ctx.EventBus.Publish(
              GameEvent.Notification(
                NotificationEvent.ShowMessage {
                  Message = "Miss"
                  Position = targetPos
                }
              )
            )
          else
            ctx.EventBus.Publish(
              GameEvent.Notification(
                NotificationEvent.DamageDealt {
                  Target = targetId
                  Amount = result.Amount
                }
              )
            )

            let baseMessage = $"-{result.Amount}"

            let debugText =
              match impact.RemainingJumps with
              | ValueSome jumps -> $" ({jumps} left)"
              | ValueNone -> ""

            let finalMessage = baseMessage + debugText

            ctx.EventBus.Publish(
              GameEvent.Notification(
                NotificationEvent.ShowMessage {
                  Message = finalMessage
                  Position = targetPos
                }
              )
            )

            if result.IsCritical then
              ctx.EventBus.Publish(
                GameEvent.Notification(
                  NotificationEvent.ShowMessage {
                    Message = "Crit!"
                    Position = targetPos
                  }
                )
              )

            for effect in skill.Effects do
              ctx.EventBus.Publish(
                GameEvent.Intent(
                  IntentEvent.EffectApplication {
                    SourceEntity = impact.CasterId
                    TargetEntity = targetId
                    Effect = effect
                  }
                )
              )
      | _ -> () // Skill not found


    let handleChargeCompleted
      (ctx: WorldContext)
      (completed: SystemCommunications.ChargeCompleted)
      =
      ctx.SkillStore.tryFind completed.SkillId
      |> ValueOption.bind (function
        | Skill.Active s -> ValueSome s
        | _ -> ValueNone)
      |> ValueOption.iter(fun skill ->
        // Only spawn if skill has a ChargePhase and Projectile delivery
        match skill.ChargePhase, skill.Delivery with
        | ValueSome chargeConfig, Projectile projectileInfo ->
          let baseTargetPos =
            match completed.Target with
            | SystemCommunications.TargetPosition pos -> pos
            | SystemCommunications.TargetDirection pos -> pos
            | SystemCommunications.TargetEntity tid ->
              Targeting.getPosition ctx.EntityContext tid
            | SystemCommunications.TargetSelf ->
              Targeting.getPosition ctx.EntityContext completed.CasterId

          // If we have orbitals, we should spawn one projectile per orbital
          // originating from the orbital's position
          match
            chargeConfig.Orbitals,
            ctx.ActiveOrbitals.TryFindV completed.CasterId
          with
          | ValueSome orbitalConfig, ValueSome activeOrbital ->
            let orbitalCount = orbitalConfig.Count
            let totalTime = float32 ctx.Time.TotalGameTime.TotalSeconds
            let elapsed = totalTime - activeOrbital.StartTime

            // Find potential targets in the area to map projectiles to
            let potentialTargets =
              match skill.Area with
              | Circle(radius, maxTargets) ->
                Spatial.Search.findTargetsInCircle ctx.SearchContext {
                  CasterId = completed.CasterId
                  Circle = {
                    Center = baseTargetPos
                    Radius = radius
                  }
                  MaxTargets = Math.Max(maxTargets, orbitalCount)
                }

              | SkillArea.Line(width, length, maxTargets) ->
                Spatial.Search.findTargetsInLine ctx.SearchContext {
                  CasterId = completed.CasterId
                  Line = {
                    Start = baseTargetPos
                    End = baseTargetPos + Vector2.UnitX * length
                    Width = width
                  }
                  MaxTargets = Math.Max(maxTargets, orbitalCount)
                }
              | SkillArea.Cone(angle, length, maxTargets) ->
                Spatial.Search.findTargetsInCone ctx.SearchContext {
                  CasterId = completed.CasterId
                  Cone =
                    ({
                      Origin = baseTargetPos
                      Direction =
                        match completed.Target with
                        | SystemCommunications.TargetPosition pos -> pos
                        | SystemCommunications.TargetDirection pos -> pos
                        | SystemCommunications.TargetEntity tid ->
                          Targeting.getPosition ctx.EntityContext tid
                        | SystemCommunications.TargetSelf ->
                          Targeting.getPosition
                            ctx.EntityContext
                            completed.CasterId
                      AngleDegrees = angle
                      Length = length
                    })
                  MaxTargets = Math.Max(maxTargets, orbitalCount)
                }
              | SkillArea.AdaptiveCone(length, maxTargets) ->
                Spatial.Search.findTargetsInCone ctx.SearchContext {
                  CasterId = completed.CasterId
                  Cone =
                    ({
                      Origin = baseTargetPos
                      Direction =
                        match completed.Target with
                        | SystemCommunications.TargetPosition pos -> pos
                        | SystemCommunications.TargetDirection pos -> pos
                        | SystemCommunications.TargetEntity tid ->
                          Targeting.getPosition ctx.EntityContext tid
                        | SystemCommunications.TargetSelf ->
                          Targeting.getPosition
                            ctx.EntityContext
                            completed.CasterId
                      AngleDegrees = 85.0f
                      Length = length
                    })
                  MaxTargets = Math.Max(maxTargets, orbitalCount)
                }
              | Point
              | MultiPoint _ -> IndexList.empty

            let targetList = potentialTargets |> Seq.toArray

            for i = 0 to orbitalCount - 1 do
              let projectileId = Guid.NewGuid() |> UMX.tag<EntityId>

              // Calculate orb position as origin
              let localOffset =
                Orbital.calculatePosition orbitalConfig elapsed i

              let casterPos =
                Targeting.getPosition ctx.EntityContext completed.CasterId

              // Calculate 3D world position same as OrbitalSystem does
              let spawnPos3D =
                Vector3(casterPos.X, 0.0f, casterPos.Y)
                + orbitalConfig.CenterOffset
                + localOffset

              // 3D -> 2D logic: X stays X, Z becomes Y
              // Compensate for altitude (3D Y) - in 2:1 isometric, altitude
              // visually raises the coin on screen. To match that position with
              // a grounded projectile, we subtract half the altitude from logic Y.
              let spawnPos2D =
                Vector2(spawnPos3D.X, spawnPos3D.Z - spawnPos3D.Y * 2.0f)

              // Determine specific target for this projectile
              let target =
                if i < targetList.Length then
                  Projectile.EntityTarget targetList[i]
                else
                  // No more entity targets, fire at a random point in the area
                  // TODO: us the shape to determine the right area
                  // for point we should use the selected point
                  // for multipoint we should select random points in the area
                  match skill.Area with
                  | Circle(radius, _)
                  | MultiPoint(radius, _) ->
                    let angle = float(ctx.Rng.NextDouble()) * 2.0 * Math.PI
                    let dist = float(ctx.Rng.NextDouble()) * float radius
                    let offsetX = cos angle * dist
                    let offsetY = sin angle * dist

                    Projectile.PositionTarget(
                      baseTargetPos + Vector2(float32 offsetX, float32 offsetY)
                    )
                  | _ -> Projectile.PositionTarget baseTargetPos

              let liveProjectile: Projectile.LiveProjectile = {
                Caster = completed.CasterId
                Target = target
                SkillId = completed.SkillId
                Info = projectileInfo
              }

              // Start projectile at calculated orb position
              ctx.StateWrite.CreateProjectile(
                projectileId,
                liveProjectile,
                ValueSome spawnPos2D
              )

          | _ ->
            // Fallback to single projectile if no orbitals or config mismatch
            let projectileId = Guid.NewGuid() |> UMX.tag<EntityId>

            let baseTarget =
              match completed.Target with
              | SystemCommunications.TargetEntity tid ->
                Projectile.EntityTarget tid
              | SystemCommunications.TargetPosition pos ->
                Projectile.PositionTarget pos
              | SystemCommunications.TargetDirection pos ->
                Projectile.PositionTarget pos
              | SystemCommunications.TargetSelf ->
                Projectile.EntityTarget completed.CasterId

            let liveProjectile: Projectile.LiveProjectile = {
              Caster = completed.CasterId
              Target = baseTarget
              SkillId = completed.SkillId
              Info = projectileInfo
            }

            ctx.StateWrite.CreateProjectile(
              projectileId,
              liveProjectile,
              ValueNone
            )

        | ValueSome _, Instant ->
          // Charged instant skill - execute instant delivery
          handleInstantDelivery ctx completed.CasterId completed.Target skill

        | _ -> ())


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
        let liveEntities = gameplay.Projections.LiveEntities |> ASet.force

        match entityScenarios |> HashMap.tryFindV entityId with
        | ValueSome scenarioId ->
          let snapshot =
            gameplay.Projections.ComputeMovementSnapshot(scenarioId)

          let getNearbyEntities center radius =
            gameplay.Projections.GetNearbyEntitiesSnapshot(
              snapshot,
              liveEntities,
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
            StateWrite = core.StateWrite
            SearchContext = searchCtx
            EntityContext = entityContext
            SkillStore = skillStore
            ParticleStore = stores.ParticleStore
            VisualEffects = core.World.VisualEffects
            ActiveOrbitals = core.World.ActiveOrbitals |> AMap.force
          }
        | ValueNone -> ValueNone

      eventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Intent(IntentEvent.Ability intent) -> Some intent
        | _ -> None)
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

      eventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Lifecycle(LifecycleEvent.ProjectileImpacted impact) ->
          Some impact
        | _ -> None)
      |> Observable.subscribe(fun event ->
        match createCtx event.CasterId with
        | ValueSome ctx -> Handlers.handleProjectileImpact ctx event
        | ValueNone -> ())
      |> subscriptions.Add

      eventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Lifecycle(LifecycleEvent.ChargeCompleted completed) ->
          Some completed
        | _ -> None)
      |> Observable.subscribe(fun event ->
        match createCtx event.CasterId with
        | ValueSome ctx -> Handlers.handleChargeCompleted ctx event
        | ValueNone -> ())
      |> subscriptions.Add

      eventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Intent(IntentEvent.EffectDamage intent) -> Some intent
        | _ -> None)
      |> Observable.subscribe(fun event ->
        match createCtx event.SourceEntity with
        | ValueSome ctx -> Handlers.handleEffectDamageIntent ctx event
        | ValueNone -> ())
      |> subscriptions.Add

      eventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Intent(IntentEvent.EffectResource intent) -> Some intent
        | _ -> None)
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

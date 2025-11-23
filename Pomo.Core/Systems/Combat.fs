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

  module private AreaOfEffect =
    let findTargetsInCircle
      (mapDef: Map.MapDefinition)
      (getNearbyEntities:
        Vector2 -> float32 -> alist<struct (Guid<EntityId> * Vector2)>)
      (casterId: Guid<EntityId>)
      (center: Vector2)
      (radius: float32)
      (maxTargets: int)
      =
      let nearby = getNearbyEntities center radius |> AList.force

      // Convert radius to grid units for the check
      // Assuming radius is in pixels, and we want to check against grid distance
      // We use TileWidth as the conversion factor, adjusted by sqrt(2) because
      // 1 grid unit diagonal (width of tile) corresponds to TileWidth pixels.
      // Grid diagonal length is sqrt(2). So TileWidth = sqrt(2) grid units.
      // 1 pixel = sqrt(2) / TileWidth grid units.
      let radiusGrid = radius * 1.41421356f / float32 mapDef.TileWidth

      let targets =
        nearby
        |> IndexList.filter(fun struct (id, pos) ->
          id <> casterId
          && Spatial.Isometric.isPointInIsometricCircle
            mapDef
            center
            radiusGrid
            pos)
        |> IndexList.sortBy(fun struct (_, pos) ->
          Vector2.DistanceSquared(center, pos))
        |> IndexList.map(fun struct (id, _) -> id)

      if maxTargets >= IndexList.count targets then
        targets
      else
        targets |> IndexList.take maxTargets

    let findTargetsInCone
      (mapDef: Map.MapDefinition)
      (getNearbyEntities:
        Vector2 -> float32 -> alist<struct (Guid<EntityId> * Vector2)>)
      (casterId: Guid<EntityId>)
      (origin: Vector2)
      (direction: Vector2)
      (angle: float32)
      (length: float32)
      (maxTargets: int)
      =
      let nearby = getNearbyEntities origin length |> AList.force

      // Convert length to grid units
      // Assuming length is in pixels, and we want to check against grid distance
      // We use TileWidth as the conversion factor, adjusted by sqrt(2) because
      // 1 grid unit diagonal (width of tile) corresponds to TileWidth pixels.
      // Grid diagonal length is sqrt(2). So TileWidth = sqrt(2) grid units.
      // 1 pixel = sqrt(2) / TileWidth grid units.
      let lengthGrid = length * 1.41421356f / float32 mapDef.TileWidth

      let targets =
        nearby
        |> IndexList.filter(fun struct (id, pos) ->
          id <> casterId
          && Spatial.Isometric.isPointInIsometricCone
            mapDef
            origin
            direction
            angle
            lengthGrid
            pos)
        |> IndexList.sortBy(fun struct (_, pos) ->
          Vector2.DistanceSquared(origin, pos))
        |> IndexList.map(fun struct (id, _) -> id)

      if maxTargets >= IndexList.count targets then
        targets
      else
        targets |> IndexList.take maxTargets

    let findTargetsInLine
      (mapDef: Map.MapDefinition)
      (getNearbyEntities:
        Vector2 -> float32 -> alist<struct (Guid<EntityId> * Vector2)>)
      (casterId: Guid<EntityId>)
      (start: Vector2)
      (endPoint: Vector2)
      (width: float32)
      (maxTargets: int)
      =
      // Radius for broad phase is half the length + half the width, roughly, or just length from start
      let length = Vector2.Distance(start, endPoint)
      let nearby = getNearbyEntities start (length + width) |> AList.force

      // Convert width to grid units
      // Assuming width is in pixels, and we want to check against grid distance
      // We use TileWidth as the conversion factor, adjusted by sqrt(2) because
      // 1 grid unit diagonal (width of tile) corresponds to TileWidth pixels.
      // Grid diagonal length is sqrt(2). So TileWidth = sqrt(2) grid units.
      // 1 pixel = sqrt(2) / TileWidth grid units.
      let widthGrid = width * 1.41421356f / float32 mapDef.TileWidth

      let targets =
        nearby
        |> IndexList.filter(fun struct (id, pos) ->
          id <> casterId
          && Spatial.Isometric.isPointInIsometricLine
            mapDef
            start
            endPoint
            widthGrid
            pos)
        |> IndexList.sortBy(fun struct (_, pos) ->
          Vector2.DistanceSquared(start, pos))
        |> IndexList.map(fun struct (id, _) -> id)

      if maxTargets >= IndexList.count targets then
        targets
      else
        targets |> IndexList.take maxTargets

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
      (mapDef: Map.MapDefinition)
      (derivedStats: amap<Guid<EntityId>, Entity.DerivedStats>)
      (positions: amap<Guid<EntityId>, Vector2>)
      (skillStore: Stores.SkillStore)
      (getNearbyEntities:
        Vector2 -> float32 -> alist<struct (Guid<EntityId> * Vector2)>)
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
          let targetEntity =
            match target with
            | SystemCommunications.TargetEntity te -> Some te
            // If a projectile is targeted at a position, we can't create a homing projectile.
            // This logic can be extended to support non-homing projectiles to a point.
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
            | SystemCommunications.TargetSelf -> positions.TryFindV casterId
            | SystemCommunications.TargetEntity targetId ->
              positions.TryFindV targetId
            | SystemCommunications.TargetPosition pos -> ValueSome pos

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
              | Circle(radius, maxTargets) ->

                AreaOfEffect.findTargetsInCircle
                  mapDef
                  getNearbyEntities
                  casterId
                  center
                  radius
                  maxTargets
              | Cone(angle, length, maxTargets) ->
                let casterPos =
                  positions
                  |> HashMap.tryFindV casterId
                  |> ValueOption.defaultValue center

                let direction =
                  match target with
                  | SystemCommunications.TargetPosition pos ->
                    // For position targeting, direction is from caster to target position
                    let offset = pos - casterPos

                    if offset = Vector2.Zero then
                      Vector2.UnitX // Default direction when target is same as caster
                    else
                      Vector2.Normalize(offset)
                  | SystemCommunications.TargetEntity targetId ->
                    // For entity targeting, direction is from caster to target entity
                    let targetPos =
                      positions
                      |> HashMap.tryFindV targetId
                      |> ValueOption.defaultValue center

                    let offset = targetPos - casterPos

                    if offset = Vector2.Zero then
                      Vector2.UnitX // Default direction when target is same as caster
                    else
                      Vector2.Normalize(offset)
                  | _ -> Vector2.UnitX // Default direction if self-targeted?

                AreaOfEffect.findTargetsInCone
                  mapDef
                  getNearbyEntities
                  casterId
                  casterPos
                  direction
                  angle
                  length
                  maxTargets
              | Line(width, length, maxTargets) ->
                let casterPos =
                  positions
                  |> HashMap.tryFindV casterId
                  |> ValueOption.defaultValue center

                let endPoint =
                  match target with
                  | SystemCommunications.TargetPosition pos ->
                    // For position targeting, direction is from caster to target position
                    let offset = pos - casterPos

                    if offset = Vector2.Zero then
                      casterPos + Vector2.UnitX * length // Default direction when target is same as caster
                    else
                      let direction = Vector2.Normalize(offset)
                      casterPos + direction * length
                  | SystemCommunications.TargetEntity targetId ->
                    // For entity targeting, direction is from caster to target entity
                    let targetPos =
                      positions
                      |> HashMap.tryFindV targetId
                      |> ValueOption.defaultValue center

                    let offset = targetPos - casterPos

                    if offset = Vector2.Zero then
                      casterPos + Vector2.UnitX * length // Default direction when target is same as caster
                    else
                      let direction = Vector2.Normalize(offset)
                      casterPos + direction * length
                  | _ -> casterPos + Vector2.UnitX * length

                AreaOfEffect.findTargetsInLine
                  mapDef
                  getNearbyEntities
                  casterId
                  casterPos
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
        mapDef: Map.MapDefinition,
        derivedStats: amap<Guid<EntityId>, Entity.DerivedStats>,
        positions: amap<Guid<EntityId>, Vector2>,
        skillStore: Stores.SkillStore,
        getNearbyEntities:
          Vector2 -> float32 -> alist<struct (Guid<EntityId> * Vector2)>
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

              AreaOfEffect.findTargetsInCircle
                mapDef
                getNearbyEntities
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

              AreaOfEffect.findTargetsInCone
                mapDef
                getNearbyEntities
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

              AreaOfEffect.findTargetsInLine
                mapDef
                getNearbyEntities
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
        mapDef: Map.MapDefinition,
        derivedStats: amap<Guid<EntityId>, Entity.DerivedStats>,
        positions: amap<Guid<EntityId>, Vector2>,
        skillStore: Stores.SkillStore,
        getNearbyEntities:
          Vector2 -> float32 -> alist<struct (Guid<EntityId> * Vector2)>
      )
      (event: SystemCommunications.AbilityIntent)
      =
      handleAbilityIntent
        world
        eventBus
        mapDef
        derivedStats
        positions
        skillStore
        getNearbyEntities
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

      eventBus.GetObservableFor<SystemCommunications.AbilityIntent>()
      |> Observable.subscribe(
        Handlers.handleAbilityIntentEvent(
          this.World,
          eventBus,
          mapDef,
          derivedStats,
          positions,
          skillStore,
          getNearbyEntities
        )
      )
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.ProjectileImpacted>()
      |> Observable.subscribe(
        Handlers.handleProjectileImpact(
          this.World,
          eventBus,
          mapDef,
          derivedStats,
          positions,
          skillStore,
          getNearbyEntities
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

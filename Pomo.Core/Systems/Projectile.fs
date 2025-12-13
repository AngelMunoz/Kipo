namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Projectile
open Pomo.Core.Domain.Particles
open Pomo.Core.Domain.Skill
open Pomo.Core.Systems.Systems

module Projectile =
  let private findNextChainTarget
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (liveEntities: HashSet<Guid<EntityId>>)
    (casterId: Guid<EntityId>)
    (currentTargetId: Guid<EntityId>)
    (originPos: Vector2)
    (maxRange: float32)
    =
    positions
    |> HashMap.filter(fun id _ ->
      liveEntities.Contains id && id <> casterId && id <> currentTargetId)
    |> HashMap.chooseV(fun _ pos ->
      let distance = Vector2.DistanceSquared(originPos, pos)

      if distance <= maxRange * maxRange then
        ValueSome distance
      else
        ValueNone)
    |> HashMap.toArrayV

  let processProjectile
    (rng: Random)
    (dt: float32)
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (liveEntities: HashSet<Guid<EntityId>>)
    (projectileId: Guid<EntityId>)
    (projectile: Projectile.LiveProjectile)
    =
    let projPos = positions.TryFindV projectileId

    let targetPos =
      match projectile.Target with
      | EntityTarget entityId -> positions.TryFindV entityId
      | PositionTarget pos -> ValueSome pos

    let targetEntity =
      match projectile.Target with
      | EntityTarget entityId -> ValueSome entityId
      | PositionTarget _ -> ValueNone

    let commEvents = ResizeArray()
    let stateEvents = ResizeArray()

    let inline addComms event = commEvents.Add event
    let inline addState event = stateEvents.Add event

    // Handle Descending projectiles (falling from sky)
    match projectile.Info.Variations with
    | ValueSome(Descending(currentAltitude, fallSpeed)) ->
      match projPos, targetPos with
      | ValueSome projPos, ValueSome targetPos ->
        let newAltitude = currentAltitude - (fallSpeed * dt)

        if newAltitude <= 0.0f then
          // Impact!
          let impact: SystemCommunications.ProjectileImpacted = {
            ProjectileId = projectileId
            CasterId = projectile.Caster
            ImpactPosition = targetPos
            TargetEntity = targetEntity
            SkillId = projectile.SkillId
            RemainingJumps = ValueNone
          }

          addComms impact
          addState <| EntityLifecycle(Removed projectileId)
        else
          // Update altitude by recreating projectile with new variation
          let updatedProjectile: LiveProjectile = {
            projectile with
                Info = {
                  projectile.Info with
                      Variations = ValueSome(Descending(newAltitude, fallSpeed))
                }
          }

          addState <| EntityLifecycle(Removed projectileId)

          addState
          <| CreateProjectile
            struct (projectileId, updatedProjectile, ValueSome projPos)

        struct (commEvents |> IndexList.ofSeq, stateEvents |> IndexList.ofSeq)
      | _ ->
        struct (IndexList.empty,
                IndexList.single(EntityLifecycle(Removed projectileId)))

    // Handle horizontal projectiles (existing behavior)
    | _ ->
      match projPos, targetPos with
      | ValueSome projPos, ValueSome targetPos ->
        let distance = Vector2.Distance(projPos, targetPos)
        let threshold = 4.0f

        if distance < threshold then
          let remainingJumps =
            match projectile.Info.Variations with
            | ValueSome(Chained(jumpsLeft, _)) -> ValueSome jumpsLeft
            | _ -> ValueNone

          let impact: SystemCommunications.ProjectileImpacted = {
            ProjectileId = projectileId
            CasterId = projectile.Caster
            ImpactPosition = targetPos
            TargetEntity = targetEntity
            SkillId = projectile.SkillId
            RemainingJumps = remainingJumps
          }

          addComms impact
          addState <| EntityLifecycle(Removed projectileId)

          match projectile.Info.Variations with
          | ValueSome(Chained(jumpsLeft, maxRange)) when jumpsLeft >= 0 ->
            match targetEntity with
            | ValueSome currentTargetId ->
              let nextTargets =
                findNextChainTarget
                  positions
                  liveEntities
                  projectile.Caster
                  currentTargetId
                  targetPos
                  maxRange

              if nextTargets.Length > 0 then
                let index = rng.Next(0, nextTargets.Length)
                let struct (newTargetId, _) = nextTargets[index]

                let newProjectileId = Guid.NewGuid() |> UMX.tag<EntityId>

                let newLiveProjectile: LiveProjectile = {
                  projectile with
                      Target = EntityTarget newTargetId
                      Info = {
                        projectile.Info with
                            Variations =
                              ValueSome(Chained(jumpsLeft - 1, maxRange))
                      }
                }

                do
                  addState
                  <| CreateProjectile
                    struct (newProjectileId,
                            newLiveProjectile,
                            ValueSome targetPos)
            | ValueNone -> ()
          | _ -> ()

          struct (commEvents |> IndexList.ofSeq, stateEvents |> IndexList.ofSeq)
        else
          let direction = Vector2.Normalize(targetPos - projPos)
          let velocity = direction * projectile.Info.Speed

          addState <| Physics(VelocityChanged struct (projectileId, velocity))

          struct (commEvents |> IndexList.ofSeq, stateEvents |> IndexList.ofSeq)

      | ValueSome _, ValueNone
      | ValueNone, ValueSome _
      | ValueNone, ValueNone ->
        struct (IndexList.empty,
                IndexList.single(EntityLifecycle(Removed projectileId)))

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns
  open Pomo.Core.Domain.Animation

  type ProjectileSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let (Stores stores) = env.StoreServices

    let spawnEffect
      (vfxId: string)
      (pos: Vector2)
      (rotation: Quaternion)
      (owner: Guid<EntityId> voption)
      (area: SkillArea voption)
      =
      match stores.ParticleStore.tryFind vfxId with
      | ValueSome configs ->
        let emitters =
          configs
          |> List.map(fun config -> {
            Config = config
            Particles = ResizeArray()
            Accumulator = ref 0.0f
            BurstDone = ref false
          })
          |> ResizeArray

        let effect = {
          Id = System.Guid.NewGuid().ToString() // temp ID
          Emitters = emitters |> Seq.toList
          Position = ref(Vector3(pos.X, 0.0f, pos.Y))
          Rotation = ref rotation
          Scale = ref Vector3.One
          IsAlive = ref true
          Owner = owner
          Overrides = {
            EffectOverrides.empty with
                Rotation = ValueSome rotation
                Area = area
          }
        }

        core.World.VisualEffects.Add(effect)
      | ValueNone -> ()

    override this.Update gameTime =
      let dt = float32 gameTime.ElapsedGameTime.TotalSeconds
      let liveEntities = gameplay.Projections.LiveEntities |> ASet.force
      let liveProjectiles = core.World.LiveProjectiles |> AMap.force
      let entityScenarios = core.World.EntityScenario |> AMap.force

      // Group projectiles by scenario
      let projectilesByScenario =
        liveProjectiles
        |> HashMap.toSeq
        |> Seq.choose(fun (id, proj) ->
          match entityScenarios.TryFindV id with
          | ValueSome sId -> Some(sId, (id, proj))
          | ValueNone -> None)
        |> Seq.groupBy fst

      let sysEvents = ResizeArray()
      let stateEvents = ResizeArray()

      for (scenarioId, projectiles) in projectilesByScenario do
        let snapshot = gameplay.Projections.ComputeMovementSnapshot(scenarioId)

        for (_, (projectileId, projectile)) in projectiles do
          // Ensure projectile has a default spinning animation if none is set.
          // We publish an ActiveAnimationsChanged event so the StateUpdate handler
          // will attach the animations to the world (consistent with other systems).
          let activeAnims = core.World.ActiveAnimations |> AMap.force

          match activeAnims |> HashMap.tryFindV projectileId with
          | ValueNone ->
            let spinAnim: AnimationState = {
              ClipId = "Projectile_Spin"
              Time = TimeSpan.Zero
              Speed = 1.0f
            }

            core.EventBus.Publish(
              Animation(
                ActiveAnimationsChanged
                  struct (projectileId, IndexList.single spinAnim)
              )
            )
          | ValueSome _ -> ()

          let struct (evs, states) =
            processProjectile
              core.World.Rng
              dt
              snapshot.Positions
              liveEntities
              projectileId
              projectile

          sysEvents.AddRange evs
          stateEvents.AddRange states

          // Visuals Logic
          // 1. Flight Visuals (Projectile itself)
          match projectile.Info.Visuals.VfxId with
          | ValueSome vfxId ->
            // Check if we already have an effect for this projectile
            let hasEffect =
              core.World.VisualEffects
              |> Seq.exists(fun e -> e.Owner = ValueSome projectileId)

            if not hasEffect then
              match snapshot.Positions |> HashMap.tryFindV projectileId with
              | ValueSome pos ->
                spawnEffect
                  vfxId
                  pos
                  Quaternion.Identity
                  (ValueSome projectileId)
                  ValueNone // No skill area for flight visuals
              | ValueNone -> ()
          | ValueNone -> ()

          // 2. Impact Visuals
          for impact in evs do
            match stores.SkillStore.tryFind impact.SkillId with
            | ValueSome(Skill.Active skill) ->
              match skill.ImpactVisuals.VfxId with
              | ValueSome vfxId ->
                let targetPos = impact.ImpactPosition

                let rotation =
                  match
                    snapshot.Positions |> HashMap.tryFindV impact.ProjectileId
                  with
                  | ValueSome projPos when projPos <> targetPos ->
                    let dir = Vector2.Normalize(targetPos - projPos)
                    let angle = MathF.Atan2(dir.Y, dir.X)

                    let yaw =
                      Quaternion.CreateFromAxisAngle(
                        Vector3.Up,
                        -angle + MathHelper.PiOver2
                      )

                    let pitch =
                      Quaternion.CreateFromAxisAngle(
                        Vector3.UnitX,
                        MathHelper.PiOver2
                      )

                    yaw * pitch
                  | _ -> Quaternion.Identity

                spawnEffect
                  vfxId
                  targetPos
                  rotation
                  ValueNone
                  (ValueSome skill.Area)
              | ValueNone -> ()
            | _ -> ()

      sysEvents |> Seq.iter core.EventBus.Publish
      stateEvents |> Seq.iter core.EventBus.Publish

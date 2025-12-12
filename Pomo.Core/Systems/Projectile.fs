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
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (liveEntities: HashSet<Guid<EntityId>>)
    (projectileId: Guid<EntityId>)
    (projectile: Projectile.LiveProjectile)
    =
    let projPos = positions.TryFindV projectileId
    let targetPos = positions.TryFindV projectile.Target

    match projPos, targetPos with
    | ValueSome projPos, ValueSome targetPos ->
      let distance = Vector2.Distance(projPos, targetPos)
      let threshold = 4.0f // Close enough for impact
      let commEvents = ResizeArray()
      let stateEvents = ResizeArray()

      let inline addComms event = commEvents.Add event

      let inline addState event = stateEvents.Add event

      if distance < threshold then
        let remainingJumps =
          match projectile.Info.Variations with
          | ValueSome(Chained(jumpsLeft, _)) -> ValueSome jumpsLeft
          | _ -> ValueNone
        // Impact: Create impact and removal events
        let impact: SystemCommunications.ProjectileImpacted = {
          ProjectileId = projectileId
          CasterId = projectile.Caster
          TargetId = projectile.Target
          SkillId = projectile.SkillId
          RemainingJumps = remainingJumps
        }

        addComms impact

        // Always remove the current projectile after impact
        addState <| EntityLifecycle(Removed projectileId)

        // Handle chaining
        match projectile.Info.Variations with
        | ValueSome(Chained(jumpsLeft, maxRange)) when jumpsLeft >= 0 ->
          let nextTargets =
            findNextChainTarget
              positions
              liveEntities
              projectile.Caster
              projectile.Target
              targetPos
              maxRange

          if nextTargets.Length > 0 then
            let index = rng.Next(0, nextTargets.Length)
            let struct (newTargetId, _) = nextTargets[index]

            let newProjectileId = Guid.NewGuid() |> UMX.tag<EntityId>

            let newLiveProjectile: LiveProjectile = {
              projectile with
                  Target = newTargetId
                  Info = {
                    projectile.Info with
                        Variations = ValueSome(Chained(jumpsLeft - 1, maxRange))
                  }
            }

            do
              addState
              <| CreateProjectile
                struct (newProjectileId, newLiveProjectile, ValueSome targetPos)
          else
            ()
        | _ -> ()

        struct (commEvents |> IndexList.ofSeq, stateEvents |> IndexList.ofSeq)
      else
        // Keep moving: Create a velocity change event
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
      (owner: Guid<EntityId> voption)
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
          Rotation = ref Quaternion.Identity
          Scale = ref Vector3.One
          IsAlive = ref true
          Owner = owner
        }

        core.World.VisualEffects.Add(effect)
      | ValueNone -> ()

    override this.Update _ =
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
              | ValueSome pos -> spawnEffect vfxId pos (ValueSome projectileId)
              | ValueNone -> ()
          | ValueNone -> ()

          // 2. Impact Visuals
          for impact in evs do
            match stores.SkillStore.tryFind impact.SkillId with
            | ValueSome(Skill.Active skill) ->
              match skill.ImpactVisuals.VfxId with
              | ValueSome vfxId ->
                match
                  snapshot.Positions |> HashMap.tryFindV impact.TargetId
                with
                | ValueSome targetPos -> spawnEffect vfxId targetPos ValueNone
                | ValueNone -> ()
              | ValueNone -> ()
            | _ -> ()

      sysEvents |> Seq.iter core.EventBus.Publish
      stateEvents |> Seq.iter core.EventBus.Publish

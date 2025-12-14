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

  /// World state needed for projectile processing lookups.
  [<Struct>]
  type WorldContext = {
    Rng: Random
    Positions: HashMap<Guid<EntityId>, Vector2>
    LiveEntities: HashSet<Guid<EntityId>>
  }

  /// A projectile being processed with its resolved positions.
  [<Struct>]
  type ProjectileContext = {
    Id: Guid<EntityId>
    Projectile: LiveProjectile
    Position: Vector2
    TargetPosition: Vector2
    TargetEntity: Guid<EntityId> voption
  }

  let private findNextChainTarget
    (world: WorldContext)
    (casterId: Guid<EntityId>)
    (currentTargetId: Guid<EntityId>)
    (originPos: Vector2)
    (maxRange: float32)
    =
    world.Positions
    |> HashMap.filter(fun id _ ->
      world.LiveEntities.Contains id && id <> casterId && id <> currentTargetId)
    |> HashMap.chooseV(fun _ pos ->
      let distance = Vector2.DistanceSquared(originPos, pos)

      if distance <= maxRange * maxRange then
        ValueSome distance
      else
        ValueNone)
    |> HashMap.toArrayV

  /// Creates an impact event record for when a projectile reaches its target.
  let inline private makeImpact
    (ctx: ProjectileContext)
    remainingJumps
    : SystemCommunications.ProjectileImpacted =
    {
      ProjectileId = ctx.Id
      CasterId = ctx.Projectile.Caster
      ImpactPosition = ctx.TargetPosition
      TargetEntity = ctx.TargetEntity
      SkillId = ctx.Projectile.SkillId
      RemainingJumps = remainingJumps
    }

  /// Processes a descending (falling from sky) projectile.
  let private processDescendingProjectile
    (dt: float32)
    (ctx: ProjectileContext)
    (currentAltitude: float32)
    (fallSpeed: float32)
    =
    let commEvents = ResizeArray()
    let stateEvents = ResizeArray()
    let newAltitude = currentAltitude - (fallSpeed * dt)

    if newAltitude <= 0.0f then
      commEvents.Add(makeImpact ctx ValueNone)
      stateEvents.Add(EntityLifecycle(Removed ctx.Id))
    else
      let updatedProjectile: LiveProjectile = {
        ctx.Projectile with
            Info = {
              ctx.Projectile.Info with
                  Variations = ValueSome(Descending(newAltitude, fallSpeed))
            }
      }

      stateEvents.Add(EntityLifecycle(Removed ctx.Id))

      stateEvents.Add(
        CreateProjectile
          struct (ctx.Id, updatedProjectile, ValueSome ctx.Position)
      )

    struct (commEvents |> IndexList.ofSeq, stateEvents |> IndexList.ofSeq)

  /// Helper to spawn a chain projectile to the next target.
  let private spawnChainProjectile
    (world: WorldContext)
    (ctx: ProjectileContext)
    (currentTargetId: Guid<EntityId>)
    (jumpsLeft: int)
    (maxRange: float32)
    =
    let nextTargets =
      findNextChainTarget
        world
        ctx.Projectile.Caster
        currentTargetId
        ctx.TargetPosition
        maxRange

    if nextTargets.Length > 0 then
      let index = world.Rng.Next(0, nextTargets.Length)
      let struct (newTargetId, _) = nextTargets[index]
      let newProjectileId = Guid.NewGuid() |> UMX.tag<EntityId>

      let newLiveProjectile: LiveProjectile = {
        ctx.Projectile with
            Target = EntityTarget newTargetId
            Info = {
              ctx.Projectile.Info with
                  Variations = ValueSome(Chained(jumpsLeft - 1, maxRange))
            }
      }

      ValueSome(
        CreateProjectile
          struct (newProjectileId,
                  newLiveProjectile,
                  ValueSome ctx.TargetPosition)
      )
    else
      ValueNone

  /// Handles the impact of a horizontal/chained projectile.
  let private handleHorizontalImpact
    (world: WorldContext)
    (ctx: ProjectileContext)
    =
    let commEvents = ResizeArray()
    let stateEvents = ResizeArray()

    let remainingJumps =
      match ctx.Projectile.Info.Variations with
      | ValueSome(Chained(jumpsLeft, _)) -> ValueSome jumpsLeft
      | _ -> ValueNone

    commEvents.Add(makeImpact ctx remainingJumps)
    stateEvents.Add(EntityLifecycle(Removed ctx.Id))

    // Handle chaining to next target
    match ctx.Projectile.Info.Variations, ctx.TargetEntity with
    | ValueSome(Chained(jumpsLeft, maxRange)), ValueSome currentTargetId when
      jumpsLeft >= 0
      ->
      match
        spawnChainProjectile world ctx currentTargetId jumpsLeft maxRange
      with
      | ValueSome event -> stateEvents.Add event
      | ValueNone -> ()
    | _ -> ()

    struct (commEvents |> IndexList.ofSeq, stateEvents |> IndexList.ofSeq)

  /// Handles the in-flight movement of a horizontal projectile.
  let private handleHorizontalFlight(ctx: ProjectileContext) =
    let direction = Vector2.Normalize(ctx.TargetPosition - ctx.Position)
    let velocity = direction * ctx.Projectile.Info.Speed

    struct (IndexList.empty,
            IndexList.single(Physics(VelocityChanged struct (ctx.Id, velocity))))

  /// Processes a horizontal (standard) or chained projectile.
  let private processHorizontalProjectile
    (world: WorldContext)
    (ctx: ProjectileContext)
    =
    let distance = Vector2.Distance(ctx.Position, ctx.TargetPosition)
    let threshold = 4.0f

    if distance < threshold then
      handleHorizontalImpact world ctx
    else
      handleHorizontalFlight ctx

  let processProjectile
    (world: WorldContext)
    (dt: float32)
    (projectileId: Guid<EntityId>)
    (projectile: Projectile.LiveProjectile)
    =
    let projPos = world.Positions.TryFindV projectileId

    let targetPos =
      match projectile.Target with
      | EntityTarget entityId -> world.Positions.TryFindV entityId
      | PositionTarget pos -> ValueSome pos

    let targetEntity =
      match projectile.Target with
      | EntityTarget entityId -> ValueSome entityId
      | PositionTarget _ -> ValueNone

    // Guard: bail early if positions are missing
    match projPos, targetPos with
    | ValueNone, _
    | _, ValueNone ->
      struct (IndexList.empty,
              IndexList.single(EntityLifecycle(Removed projectileId)))

    | ValueSome projPos, ValueSome targetPos ->
      let ctx: ProjectileContext = {
        Id = projectileId
        Projectile = projectile
        Position = projPos
        TargetPosition = targetPos
        TargetEntity = targetEntity
      }

      match projectile.Info.Variations with
      | ValueSome(Descending(currentAltitude, fallSpeed)) ->
        processDescendingProjectile dt ctx currentAltitude fallSpeed
      | _ -> processHorizontalProjectile world ctx

  open Pomo.Core.Environment
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

    override _.Update _ =
      let dt =
        core.World.Time
        |> AVal.map(_.Delta.TotalSeconds >> float32)
        |> AVal.force

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

      for scenarioId, projectiles in projectilesByScenario do
        let snapshot = gameplay.Projections.ComputeMovementSnapshot(scenarioId)

        let worldCtx = {
          Rng = core.World.Rng
          Positions = snapshot.Positions
          LiveEntities = liveEntities
        }

        for _, (projectileId, projectile) in projectiles do
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
            processProjectile worldCtx dt projectileId projectile

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

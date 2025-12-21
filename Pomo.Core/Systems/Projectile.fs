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
open Pomo.Core.Environment

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
    (stateWrite: IStateWriteService)
    (dt: float32)
    (ctx: ProjectileContext)
    (currentAltitude: float32)
    (fallSpeed: float32)
    =
    let commEvents = ResizeArray()
    let newAltitude = currentAltitude - (fallSpeed * dt)

    if newAltitude <= 0.0f then
      commEvents.Add(makeImpact ctx ValueNone)
      stateWrite.RemoveEntity(ctx.Id)
    else
      let updatedProjectile: LiveProjectile = {
        ctx.Projectile with
            Info = {
              ctx.Projectile.Info with
                  Variations = ValueSome(Descending(newAltitude, fallSpeed))
            }
      }

      stateWrite.RemoveEntity(ctx.Id)

      stateWrite.CreateProjectile(
        ctx.Id,
        updatedProjectile,
        ValueSome ctx.Position
      )

    commEvents |> IndexList.ofSeq

  /// Helper to spawn a chain projectile to the next target.
  let private spawnChainProjectile
    (stateWrite: IStateWriteService)
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

      stateWrite.CreateProjectile(
        newProjectileId,
        newLiveProjectile,
        ValueSome ctx.TargetPosition
      )
    else
      ()

  /// Handles the impact of a horizontal/chained projectile.
  let private handleHorizontalImpact
    (stateWrite: IStateWriteService)
    (world: WorldContext)
    (ctx: ProjectileContext)
    =
    let commEvents = ResizeArray()

    let remainingJumps =
      match ctx.Projectile.Info.Variations with
      | ValueSome(Chained(jumpsLeft, _)) -> ValueSome jumpsLeft
      | _ -> ValueNone

    commEvents.Add(makeImpact ctx remainingJumps)
    stateWrite.RemoveEntity(ctx.Id)

    // Handle chaining to next target
    match ctx.Projectile.Info.Variations, ctx.TargetEntity with
    | ValueSome(Chained(jumpsLeft, maxRange)), ValueSome currentTargetId when
      jumpsLeft >= 0
      ->
      spawnChainProjectile
        stateWrite
        world
        ctx
        currentTargetId
        jumpsLeft
        maxRange
    | _ -> ()

    commEvents |> IndexList.ofSeq

  /// Handles the in-flight movement of a horizontal projectile.
  /// Updates velocity via StateWriteService directly.
  let private handleHorizontalFlight
    (stateWrite: IStateWriteService)
    (ctx: ProjectileContext)
    =
    let direction = Vector2.Normalize(ctx.TargetPosition - ctx.Position)
    let velocity = direction * ctx.Projectile.Info.Speed
    stateWrite.UpdateVelocity(ctx.Id, velocity)
    IndexList.empty

  /// Processes a horizontal (standard) or chained projectile.
  let private processHorizontalProjectile
    (stateWrite: IStateWriteService)
    (world: WorldContext)
    (ctx: ProjectileContext)
    =
    let distance = Vector2.Distance(ctx.Position, ctx.TargetPosition)
    let threshold = 4.0f

    if distance < threshold then
      handleHorizontalImpact stateWrite world ctx
    else
      handleHorizontalFlight stateWrite ctx

  let processProjectile
    (stateWrite: IStateWriteService)
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
      stateWrite.RemoveEntity(projectileId)
      IndexList.empty

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
        processDescendingProjectile stateWrite dt ctx currentAltitude fallSpeed
      | _ -> processHorizontalProjectile stateWrite world ctx


  open Pomo.Core.Domain.Animation
  open Pomo.Core.EventBus

  /// Context for visual effect spawning operations
  [<Struct>]
  type VisualEffectContext = {
    ParticleStore: Pomo.Core.Stores.ParticleStore
    SkillStore: Pomo.Core.Stores.SkillStore
    VisualEffects: ResizeArray<Particles.ActiveEffect>
    Positions: HashMap<Guid<EntityId>, Vector2>
    EffectOwners: System.Collections.Generic.HashSet<Guid<EntityId>>
  }

  /// Calculates rotation quaternion for impact visuals based on projectile direction
  let inline calculateImpactRotation
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (projectileId: Guid<EntityId>)
    (targetPos: Vector2)
    =
    match positions |> HashMap.tryFindV projectileId with
    | ValueSome projPos when projPos <> targetPos ->
      let dir = Vector2.Normalize(targetPos - projPos)
      let angle = MathF.Atan2(dir.Y, dir.X)

      let yaw =
        Quaternion.CreateFromAxisAngle(Vector3.Up, -angle + MathHelper.PiOver2)

      let pitch =
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.PiOver2)

      yaw * pitch
    | _ -> Quaternion.Identity

  let inline spawnEffect
    (particleStore: Pomo.Core.Stores.ParticleStore)
    (visualEffects: ResizeArray<Particles.ActiveEffect>)
    (vfxId: string)
    (pos: Vector2)
    (rotation: Quaternion)
    (owner: Guid<EntityId> voption)
    (area: SkillArea voption)
    =
    match particleStore.tryFind vfxId with
    | ValueSome configs ->
      let struct (billboardEmitters, meshEmitters) =
        splitEmittersByRenderMode configs

      let effect = {
        Id = System.Guid.NewGuid().ToString()
        Emitters = billboardEmitters
        MeshEmitters = meshEmitters
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

      visualEffects.Add(effect)
    | ValueNone -> ()

  let inline ensureProjectileAnimation
    (stateWrite: IStateWriteService)
    (activeAnims: HashMap<Guid<EntityId>, IndexList<AnimationState>>)
    (projectileId: Guid<EntityId>)
    =
    match activeAnims |> HashMap.tryFindV projectileId with
    | ValueNone ->
      let spinAnim: AnimationState = {
        ClipId = "Projectile_Spin"
        Time = TimeSpan.Zero
        Speed = 1.0f
      }

      stateWrite.UpdateActiveAnimations(projectileId, IndexList.single spinAnim)
    | ValueSome _ -> ()

  /// Spawns flight visuals for a projectile if not already present
  let inline spawnFlightVisual
    (ctx: VisualEffectContext)
    (projectileId: Guid<EntityId>)
    (projectile: LiveProjectile)
    =
    match projectile.Info.Visuals.VfxId with
    | ValueSome vfxId ->
      if not(ctx.EffectOwners.Contains projectileId) then
        match ctx.Positions |> HashMap.tryFindV projectileId with
        | ValueSome pos ->
          spawnEffect
            ctx.ParticleStore
            ctx.VisualEffects
            vfxId
            pos
            Quaternion.Identity
            (ValueSome projectileId)
            ValueNone
        | ValueNone -> ()
    | ValueNone -> ()

  /// Spawns impact visuals for a projectile impact event
  let inline spawnImpactVisual
    (ctx: VisualEffectContext)
    (impact: SystemCommunications.ProjectileImpacted)
    =
    match ctx.SkillStore.tryFind impact.SkillId with
    | ValueSome(Skill.Active skill) ->
      match skill.ImpactVisuals.VfxId with
      | ValueSome vfxId ->
        let rotation =
          calculateImpactRotation
            ctx.Positions
            impact.ProjectileId
            impact.ImpactPosition

        spawnEffect
          ctx.ParticleStore
          ctx.VisualEffects
          vfxId
          impact.ImpactPosition
          rotation
          ValueNone
          (ValueSome skill.Area)
      | ValueNone -> ()
    | _ -> ()


  type ProjectileSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let (Stores stores) = env.StoreServices
    let stateWrite = core.StateWrite

    // Track which projectiles already have visual effects (O(1) lookup)
    let effectOwners = System.Collections.Generic.HashSet<Guid<EntityId>>()

    override _.Update _ =
      let dt =
        core.World.Time
        |> AVal.map(_.Delta.TotalSeconds >> float32)
        |> AVal.force

      let liveEntities = gameplay.Projections.LiveEntities |> ASet.force
      let liveProjectiles = core.World.LiveProjectiles |> AMap.force
      let entityScenarios = core.World.EntityScenario |> AMap.force
      let activeAnims = core.World.ActiveAnimations |> AMap.force
      let visualEffects = core.World.VisualEffects

      // Rebuild effectOwners set each frame for accuracy
      effectOwners.Clear()

      for e in visualEffects do
        match e.Owner with
        | ValueSome ownerId -> effectOwners.Add(ownerId) |> ignore
        | ValueNone -> ()

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

        // Create visual effect context once per scenario
        let vfxCtx: VisualEffectContext = {
          ParticleStore = stores.ParticleStore
          SkillStore = stores.SkillStore
          VisualEffects = visualEffects
          Positions = snapshot.Positions
          EffectOwners = effectOwners
        }

        for _, (projectileId, projectile) in projectiles do
          // 1. Ensure animation
          ensureProjectileAnimation stateWrite activeAnims projectileId

          // 2. Process projectile logic
          let evs =
            processProjectile stateWrite worldCtx dt projectileId projectile

          sysEvents.AddRange evs

          // 3. Flight visuals
          spawnFlightVisual vfxCtx projectileId projectile

          // 4. Impact visuals
          for impact in evs do
            spawnImpactVisual vfxCtx impact

      sysEvents
      |> Seq.iter(fun e ->
        core.EventBus.Publish(
          GameEvent.Lifecycle(LifecycleEvent.ProjectileImpacted e)
        ))

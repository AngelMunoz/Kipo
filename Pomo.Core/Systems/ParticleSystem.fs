namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive

open FSharp.UMX

open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Particles
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Units
open Pomo.Core.Environment
open Pomo.Core.Systems.Systems

module ParticleSystem =

  /// Result type for shape-based spawn calculations
  [<Struct>]
  type SpawnResult = {
    Direction: Vector3
    SpawnOffset: Vector3
    SpeedOverride: float32 voption
  }

  /// Generate a random unit vector on a sphere
  let inline randomSphereDirection(rng: Random) =
    let mutable dir = Vector3.UnitY
    let mutable valid = false

    while not valid do
      let v =
        Vector3(
          float32(rng.NextDouble() * 2.0 - 1.0),
          float32(rng.NextDouble() * 2.0 - 1.0),
          float32(rng.NextDouble() * 2.0 - 1.0)
        )

      let lenSq = v.LengthSquared()

      if lenSq > 0.001f && lenSq <= 1.0f then
        dir <- Vector3.Normalize(v)
        valid <- true

    dir

  /// Generate random offset on a disk (uniform distribution)
  let inline randomDiskOffset (rng: Random) (radius: float32) =
    let spawnDist = float32(Math.Sqrt(rng.NextDouble())) * radius
    let spawnAngle = float32(rng.NextDouble() * 2.0 * Math.PI)

    Vector3(
      spawnDist * MathF.Cos(spawnAngle),
      0.0f,
      spawnDist * MathF.Sin(spawnAngle)
    )

  // ============================================================================
  // EmissionMode Helpers
  // ============================================================================

  /// Compute spawn distance based on emission mode
  let inline computeSpawnDistance
    (mode: EmissionMode)
    (length: float32)
    (rng: Random)
    : float32 =
    match mode with
    | Uniform -> float32(Math.Sqrt(rng.NextDouble())) * length
    // Distance factor for Outward mode: near origin (0-85%)
    | Outward -> float32(rng.NextDouble() * 0.85) * length
    // Distance factor for Inward mode: near edge (85-100%)
    | Inward -> length * (0.85f + float32(rng.NextDouble() * 0.15))
    // Distance factor for EdgeOnly mode: at edge (95-100%)
    | EdgeOnly -> length * (0.95f + float32(rng.NextDouble() * 0.05))

  /// Direction for Outward mode: normalize spawn offset
  let inline emissionDirectionOutward(spawnOffset: Vector3) =
    if spawnOffset.LengthSquared() > 0.001f then
      Vector3.Normalize spawnOffset
    else
      Vector3.UnitY

  /// Direction for Inward mode: negative of normalized spawn offset
  let inline emissionDirectionInward(spawnOffset: Vector3) =
    if spawnOffset.LengthSquared() > 0.001f then
      -Vector3.Normalize spawnOffset
    else
      -Vector3.UnitY

  /// Compute particle direction based on emission mode and spawn offset
  let inline computeEmissionDirection
    (mode: EmissionMode)
    (spawnOffset: Vector3)
    (rng: Random)
    : Vector3 =
    match mode with
    | Uniform -> randomSphereDirection rng
    | Outward -> emissionDirectionOutward spawnOffset
    | Inward -> emissionDirectionInward spawnOffset
    | EdgeOnly -> randomSphereDirection rng // Scatter at edges

  // ============================================================================
  // Shape Spawn Functions
  // ============================================================================

  /// Spawn logic for Point shape
  let inline spawnPointShape(rng: Random) =
    let theta = rng.NextDouble() * 2.0 * Math.PI
    let cosPhi = rng.NextDouble() * 2.0 - 1.0
    let sinPhi = Math.Sqrt(1.0 - cosPhi * cosPhi)
    let x = sinPhi * Math.Cos(theta)
    let y = cosPhi
    let z = sinPhi * Math.Sin(theta)
    let dir = Vector3(float32 x, float32 y, float32 z)

    {
      Direction = dir
      SpawnOffset = Vector3.Zero
      SpeedOverride = ValueNone
    }

  /// Spawn logic for Sphere/Circle shape with EmissionMode
  let inline spawnSphereShape
    (rng: Random)
    (radius: float32)
    (mode: EmissionMode)
    =
    let spawnOffset = randomDiskOffset rng radius
    let dir = computeEmissionDirection mode spawnOffset rng

    {
      Direction = dir
      SpawnOffset = spawnOffset
      SpeedOverride = ValueNone
    }

  /// Spawn logic for Line/Rectangle shape
  let inline spawnLineShape (rng: Random) (width: float32) (length: float32) =
    let dir = Vector3.UnitY
    let alongLength = float32(rng.NextDouble()) * length
    let acrossWidth = float32(rng.NextDouble() - 0.5) * width
    let spawnOffset = Vector3(acrossWidth, alongLength, 0.0f)

    {
      Direction = dir
      SpawnOffset = spawnOffset
      SpeedOverride = ValueNone
    }

  /// Spawn logic for Cone shape with EmissionMode
  let inline spawnConeShape
    (rng: Random)
    (angle: float32)
    (length: float32)
    (mode: EmissionMode)
    =
    let halfAngleRad = MathHelper.ToRadians(angle / 2.0f)
    let coneAngle = float32(Math.Sqrt(rng.NextDouble())) * halfAngleRad
    let rotAroundAxis = float32(rng.NextDouble() * 2.0 * Math.PI)

    // Spawn distance determines WHERE particle starts
    let distFromOrigin = computeSpawnDistance mode length rng

    let xOffset =
      distFromOrigin * MathF.Sin(coneAngle) * MathF.Cos(rotAroundAxis)

    let zOffset =
      distFromOrigin * MathF.Sin(coneAngle) * MathF.Sin(rotAroundAxis)

    let yOffset = distFromOrigin * MathF.Cos(coneAngle)

    let spawnOffset = Vector3(xOffset, yOffset, zOffset)

    // For Outward mode, compute direction using FULL length so particles
    // spread to fill the entire cone, not just the spawn region
    let dir =
      match mode with
      | Outward ->
        // Compute target point at full cone length with same angle
        let targetX = length * MathF.Sin(coneAngle) * MathF.Cos(rotAroundAxis)
        let targetZ = length * MathF.Sin(coneAngle) * MathF.Sin(rotAroundAxis)
        let targetY = length * MathF.Cos(coneAngle)
        let targetOffset = Vector3(targetX, targetY, targetZ)
        emissionDirectionOutward targetOffset
      | _ -> computeEmissionDirection mode spawnOffset rng

    {
      Direction = dir
      SpawnOffset = spawnOffset
      SpeedOverride = ValueNone
    }



  let updateParticle (dt: float32) (p: Particle) =
    let p = Particle.withLife (p.Life - dt) p

    if p.Life > 0.0f then
      let newPos = p.Position + p.Velocity * dt
      Particle.withPosition newPos p
    else
      p

  let updateEmitter
    (dt: float32)
    (rng: Random)
    (emitter: ActiveEmitter)
    (worldPos: Vector3)
    (ownerVelocity: Vector3)
    (spawningEnabled: bool)
    (ownerRotation: Quaternion)
    (overrides: EffectOverrides)
    =
    // Helper to spawn a single particle
    let spawnParticle() =
      let config = emitter.Config.Particle

      let lifetime =
        let struct (min, max) = config.Lifetime
        min + (float32(rng.NextDouble()) * (max - min))

      let speed =
        let struct (min, max) = config.Speed
        min + (float32(rng.NextDouble()) * (max - min))

      // Direction and spawn offset based on shape (Local Space)
      // Resolve effective EmissionMode: override takes precedence over config
      let effectiveMode =
        match overrides.EmissionMode with
        | ValueSome m -> m
        | ValueNone -> emitter.Config.EmissionMode

      let spawnResult =
        match emitter.Config.Shape with
        | EmitterShape.Point -> spawnPointShape rng
        | EmitterShape.Sphere configRadius ->
          // Check for skill-driven area override
          match overrides.Area with
          | ValueSome(SkillArea.Circle(skillRadius, _)) ->
            spawnSphereShape rng skillRadius effectiveMode
          | ValueSome(SkillArea.Line(width, length, _)) ->
            spawnLineShape rng width length
          | _ -> spawnSphereShape rng configRadius effectiveMode

        | EmitterShape.Cone(configAngle, configRadius) ->
          // Check for skill-driven area override
          let angle, length =
            match overrides.Area with
            | ValueSome(SkillArea.Cone(skillAngle, skillLength, _)) ->
              skillAngle, skillLength
            | ValueSome(SkillArea.AdaptiveCone(skillLength, _)) ->
              configAngle, skillLength
            | _ -> configAngle, configRadius * 10.0f

          spawnConeShape rng angle length effectiveMode

      let dirLocal = spawnResult.Direction
      let spawnOffsetLocal = spawnResult.SpawnOffset
      let speedOverride = spawnResult.SpeedOverride

      // Use speed override if available, otherwise config speed
      let finalSpeed =
        match speedOverride with
        | ValueSome s -> s * (0.5f + float32(rng.NextDouble()) * 1.0f) // Add 50-150% variation
        | ValueNone -> speed

      // Apply Rotations
      let configRotEuler = emitter.Config.EmissionRotation

      let configRot =
        Quaternion.CreateFromYawPitchRoll(
          MathHelper.ToRadians(configRotEuler.Y),
          MathHelper.ToRadians(configRotEuler.X),
          MathHelper.ToRadians(configRotEuler.Z)
        )

      let baseRot =
        match overrides.Rotation with
        | ValueSome r -> r
        | ValueNone -> ownerRotation

      let totalRot = baseRot * configRot

      let dir = Vector3.Transform(dirLocal, totalRot)
      let spawnOffset = Vector3.Transform(spawnOffsetLocal, totalRot)

      // Rotate LocalOffset by Owner Rotation (it stays attached to the mesh)
      let localOffsetRotated =
        Vector3.Transform(emitter.Config.LocalOffset, ownerRotation)

      // Initial Velocity
      let baseVelocity = dir * finalSpeed

      // Inherit Velocity
      let inheritedVelocity = ownerVelocity * emitter.Config.InheritVelocity

      // Random Velocity
      let randomVel =
        let rx = (rng.NextDouble() * 2.0 - 1.0) |> float32
        let ry = (rng.NextDouble() * 2.0 - 1.0) |> float32
        let rz = (rng.NextDouble() * 2.0 - 1.0) |> float32
        config.RandomVelocity * Vector3(rx, ry, rz)

      let velocity = baseVelocity + inheritedVelocity + randomVel

      // Position logic based on Space
      let finalSpawnPos =
        match emitter.Config.SimulationSpace with
        | World -> worldPos + localOffsetRotated + spawnOffset
        | Local -> localOffsetRotated + spawnOffset

      let p = {
        Position = finalSpawnPos
        Velocity = velocity
        Size = config.SizeStart
        Color = config.ColorStart
        Life = lifetime
        MaxLife = lifetime
      }

      emitter.Particles.Add(p)

    // Spawn new particles
    if spawningEnabled then
      // Handle Burst (One-time)
      if not emitter.BurstDone.Value && emitter.Config.Burst > 0 then
        for _ in 1 .. emitter.Config.Burst do
          spawnParticle()

        emitter.BurstDone.Value <- true

      // Handle Continuous Rate
      if emitter.Config.Rate > 0 then
        let rate = 1.0f / float32 emitter.Config.Rate
        emitter.Accumulator.Value <- emitter.Accumulator.Value + dt

        while emitter.Accumulator.Value > rate do
          emitter.Accumulator.Value <- emitter.Accumulator.Value - rate
          spawnParticle()

    // Update existing
    let particles = emitter.Particles
    let mutable i = 0

    while i < particles.Count do
      let p = particles.[i]

      // Use immutable update pattern
      let p = updateParticle dt p

      if p.Life <= 0.0f then
        particles.RemoveAt(i)
      else
        // Update kinematics (Gravity)
        let newVelY = p.Velocity.Y - (emitter.Config.Particle.Gravity * dt)

        let mutable finalPos = p.Position
        let mutable finalVelY = newVelY
        let mutable finalVelX = p.Velocity.X
        let mutable finalVelZ = p.Velocity.Z

        // Apply Air Drag
        if emitter.Config.Particle.Drag > 0.0f then
          let dragFactor =
            MathHelper.Clamp(
              1.0f - (emitter.Config.Particle.Drag * dt),
              0.0f,
              1.0f
            )

          finalVelX <- finalVelX * dragFactor
          finalVelZ <- finalVelZ * dragFactor

        // Floor Collision
        // Check if Simulation Space affects this logic?
        // Ideally floor is at Y=0 relative to World.
        // If Local Space, Y is relative to Owner. Owner is usually at Y=0 World (if grounded).
        // So treating Y=0 (or FloorHeight) as floor usually works for grounded emitters.
        // For flying emitters, local space floor might be weird, but let's stick to simple floor logic.

        // Get effective floor height from config (default 0.0)
        let floorY = emitter.Config.FloorHeight

        if finalPos.Y < floorY then
          finalPos <- Vector3(finalPos.X, floorY, finalPos.Z)
          finalVelY <- 0.0f // Stop falling

          // Apply Ground Friction (Stop sliding)
          // Hardcoded strong friction for now
          finalVelX <- finalVelX * 0.1f
          finalVelZ <- finalVelZ * 0.1f

        let p = {
          p with
              Position = finalPos
              Velocity = Vector3(finalVelX, finalVelY, finalVelZ)
        }

        // Update props (Lerp)
        let t = 1.0f - (p.Life / p.MaxLife)

        let newSize =
          MathHelper.Lerp(
            emitter.Config.Particle.SizeStart,
            emitter.Config.Particle.SizeEnd,
            t
          )

        let newColor =
          Color.Lerp(
            emitter.Config.Particle.ColorStart,
            emitter.Config.Particle.ColorEnd,
            t
          )

        let p = p |> Particle.withSize newSize |> Particle.withColor newColor

        particles.[i] <- p
        i <- i + 1

  /// Context for particle system update operations
  type ParticleUpdateContext = {
    Dt: float32
    Rng: Random
    Effects: ResizeArray<Particles.ActiveEffect>
    EffectsById:
      System.Collections.Generic.Dictionary<string, Particles.ActiveEffect>
    ActiveEffectVisuals:
      System.Collections.Generic.Dictionary<Guid<EffectId>, string>
    ParticleStore: Stores.ParticleStore
    Positions: HashMap<Guid<EntityId>, Vector2>
    Velocities: HashMap<Guid<EntityId>, Vector2>
    Rotations: HashMap<Guid<EntityId>, float32>
    GameplayEffects: HashMap<Guid<EntityId>, IndexList<Skill.ActiveEffect>>
  }

  /// Sync gameplay effects to visual effects - spawns new visuals for new effects
  let inline syncGameplayEffectsToVisuals
    (ctx: ParticleUpdateContext)
    (currentFrameEffectIds: System.Collections.Generic.HashSet<Guid<EffectId>>)
    =
    ctx.GameplayEffects
    |> HashMap.iter(fun entityId effectList ->
      for gameplayEffect in effectList do
        match gameplayEffect.SourceEffect.Visuals.VfxId with
        | ValueSome vfxId ->
          currentFrameEffectIds.Add(gameplayEffect.Id) |> ignore

          if not(ctx.ActiveEffectVisuals.ContainsKey(gameplayEffect.Id)) then
            match ctx.ParticleStore.tryFind vfxId with
            | ValueSome emitterConfigs ->
              let particleEffectId = Guid.NewGuid().ToString()

              let emitters =
                emitterConfigs
                |> List.map(fun config -> {
                  Config = config
                  Particles = ResizeArray<Particle>()
                  Accumulator = ref 0.0f
                  BurstDone = ref false
                })

              let newEffect: Particles.ActiveEffect = {
                Id = particleEffectId
                Emitters = emitters
                Position = ref Vector3.Zero
                Rotation = ref Quaternion.Identity
                Scale = ref Vector3.One
                IsAlive = ref true
                Owner = ValueSome entityId
                Overrides = EffectOverrides.empty
              }

              ctx.Effects.Add(newEffect)
              ctx.EffectsById.Add(particleEffectId, newEffect)
              ctx.ActiveEffectVisuals.Add(gameplayEffect.Id, particleEffectId)
            | ValueNone -> ()
        | ValueNone -> ())

  /// Cleanup expired effects - marks effects for removal and cleans up tracking dictionaries
  let inline cleanupExpiredEffects
    (ctx: ParticleUpdateContext)
    (currentFrameEffectIds: System.Collections.Generic.HashSet<Guid<EffectId>>)
    =
    let toRemove = ResizeArray<Guid<EffectId>>()

    for kvp in ctx.ActiveEffectVisuals do
      if not(currentFrameEffectIds.Contains(kvp.Key)) then
        let particleEffectId = kvp.Value

        match ctx.EffectsById.TryGetValue(particleEffectId) with
        | true, e -> e.IsAlive.Value <- false
        | false, _ -> ()

        toRemove.Add(kvp.Key)

    for key in toRemove do
      match ctx.ActiveEffectVisuals.TryGetValue(key) with
      | true, particleEffectId ->
        ctx.EffectsById.Remove(particleEffectId) |> ignore
      | false, _ -> ()

      ctx.ActiveEffectVisuals.Remove(key) |> ignore

  /// Update a single visual effect - syncs owner state and updates emitters
  let inline updateSingleEffect
    (ctx: ParticleUpdateContext)
    (effect: Particles.ActiveEffect)
    : bool = // Returns true if effect should be removed
    let mutable ownerVelocity = Vector3.Zero
    let mutable ownerRotation = Quaternion.Identity

    match effect.Owner with
    | ValueSome ownerId ->
      match ctx.Positions.TryFindV ownerId with
      | ValueSome pos ->
        effect.Position.Value <- Vector3(pos.X, 0.0f, pos.Y)

        match ctx.Velocities.TryFindV ownerId with
        | ValueSome vel -> ownerVelocity <- Vector3(vel.X, 0.0f, vel.Y)
        | ValueNone -> ()

        match ctx.Rotations.TryFindV ownerId with
        | ValueSome rot ->
          ownerRotation <-
            Quaternion.CreateFromAxisAngle(
              Vector3.Up,
              -rot + MathHelper.PiOver2
            )
        | ValueNone -> ()

      | ValueNone -> effect.IsAlive.Value <- false
    | ValueNone -> ()

    let shouldSpawn = effect.IsAlive.Value
    let mutable anyParticlesAlive = false

    for emitter in effect.Emitters do
      updateEmitter
        ctx.Dt
        ctx.Rng
        emitter
        effect.Position.Value
        ownerVelocity
        shouldSpawn
        ownerRotation
        effect.Overrides

      if emitter.Particles.Count > 0 then
        anyParticlesAlive <- true

    not shouldSpawn && not anyParticlesAlive

  type ParticleSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices
    let particleStore = stores.ParticleStore

    // Map Gameplay Effect ID -> Particle Effect ID
    let activeEffectVisuals =
      System.Collections.Generic.Dictionary<Guid<EffectId>, string>()

    let effectsById =
      System.Collections.Generic.Dictionary<string, Particles.ActiveEffect>()

    override this.Update(gameTime) =
      let dt =
        core.World.Time
        |> AVal.map(_.Delta.TotalSeconds >> float32)
        |> AVal.force

      let effects = core.World.VisualEffects

      // Create update context
      let ctx: ParticleUpdateContext = {
        Dt = dt
        Rng = core.World.Rng
        Effects = effects
        EffectsById = effectsById
        ActiveEffectVisuals = activeEffectVisuals
        ParticleStore = particleStore
        Positions = core.World.Positions |> AMap.force
        Velocities = core.World.Velocities |> AMap.force
        Rotations = core.World.Rotations |> AMap.force
        GameplayEffects = core.World.ActiveEffects |> AMap.force
      }

      // 1. Sync gameplay effects to visuals
      let currentFrameEffectIds =
        System.Collections.Generic.HashSet<Guid<EffectId>>()

      syncGameplayEffectsToVisuals ctx currentFrameEffectIds

      // 2. Cleanup expired effects
      cleanupExpiredEffects ctx currentFrameEffectIds

      // 3. Update visual effects
      for i = effects.Count - 1 downto 0 do
        let effect = effects.[i]
        let shouldRemove = updateSingleEffect ctx effect

        if shouldRemove then
          effects.RemoveAt(i)

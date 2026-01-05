namespace Pomo.Core.Systems

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive

open FSharp.UMX

open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Particles
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Units
open Pomo.Core.Environment
open Pomo.Core.Projections
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

  // ─────────────────────────────────────────────────────────────────────────
  // Shared spawn helpers for both billboard and mesh particles
  // ─────────────────────────────────────────────────────────────────────────

  /// Computes spawn result based on emitter shape and skill area overrides
  let inline computeShapeSpawnResult
    (rng: Random)
    (shape: EmitterShape)
    (overrides: EffectOverrides)
    (effectiveMode: EmissionMode)
    : SpawnResult =
    match shape with
    | EmitterShape.Point -> spawnPointShape rng
    | EmitterShape.Sphere configRadius ->
      match overrides.Area with
      | ValueSome(SkillArea.Circle(skillRadius, _)) ->
        spawnSphereShape rng skillRadius effectiveMode
      | ValueSome(SkillArea.Line(width, length, _)) ->
        spawnLineShape rng width length
      | _ -> spawnSphereShape rng configRadius effectiveMode
    | EmitterShape.Cone(configAngle, configRadius) ->
      let angle, length =
        match overrides.Area with
        | ValueSome(SkillArea.Cone(skillAngle, skillLength, _)) ->
          skillAngle, skillLength
        | ValueSome(SkillArea.AdaptiveCone(skillLength, _)) ->
          configAngle, skillLength
        | _ -> configAngle, configRadius * 10.0f

      spawnConeShape rng angle length effectiveMode
    | EmitterShape.Line(configWidth, configLength) ->
      let width, length =
        match overrides.Area with
        | ValueSome(SkillArea.Line(skillWidth, skillLength, _)) ->
          skillWidth, skillLength
        | _ -> configWidth, configLength

      spawnLineShape rng width length

  /// Context for spawning a particle (shared between billboard and mesh)
  [<Struct>]
  type SpawnContext = {
    FinalSpeed: float32
    Direction: Vector3
    SpawnOffset: Vector3
    LocalOffsetRotated: Vector3
    Velocity: Vector3
    FinalPosition: Vector3
    Lifetime: float32
  }

  /// Computes spawn context from emitter config and overrides
  let inline computeSpawnContext
    (rng: Random)
    (config: EmitterConfig)
    (particleConfig: ParticleConfig)
    (worldPos: Vector3)
    (ownerVelocity: Vector3)
    (ownerRotation: Quaternion)
    (overrides: EffectOverrides)
    : SpawnContext =

    let struct (lifetimeMin, lifetimeMax) = particleConfig.Lifetime

    let lifetime =
      lifetimeMin + (float32(rng.NextDouble()) * (lifetimeMax - lifetimeMin))

    let struct (speedMin, speedMax) = particleConfig.Speed
    let speed = speedMin + (float32(rng.NextDouble()) * (speedMax - speedMin))

    let effectiveMode =
      match overrides.EmissionMode with
      | ValueSome m -> m
      | ValueNone -> config.EmissionMode

    let spawnResult =
      computeShapeSpawnResult rng config.Shape overrides effectiveMode

    let finalSpeed =
      match spawnResult.SpeedOverride with
      | ValueSome s -> s * (0.5f + float32(rng.NextDouble()) * 1.0f)
      | ValueNone -> speed

    // Apply rotation
    let configRotEuler = config.EmissionRotation

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
    let dir = Vector3.Transform(spawnResult.Direction, totalRot)
    let spawnOffset = Vector3.Transform(spawnResult.SpawnOffset, totalRot)

    let localOffsetRotated =
      Vector3.Transform(config.LocalOffset, ownerRotation)

    // Compute velocity
    let baseVelocity = dir * finalSpeed
    let inheritedVelocity = ownerVelocity * config.InheritVelocity

    let randomVel =
      let rx = (rng.NextDouble() * 2.0 - 1.0) |> float32
      let ry = (rng.NextDouble() * 2.0 - 1.0) |> float32
      let rz = (rng.NextDouble() * 2.0 - 1.0) |> float32
      particleConfig.RandomVelocity * Vector3(rx, ry, rz)

    let velocity = baseVelocity + inheritedVelocity + randomVel

    // Final position
    let finalPosition =
      match config.SimulationSpace with
      | World -> worldPos + localOffsetRotated + spawnOffset
      | Local -> localOffsetRotated + spawnOffset

    {
      FinalSpeed = finalSpeed
      Direction = dir
      SpawnOffset = spawnOffset
      LocalOffsetRotated = localOffsetRotated
      Velocity = velocity
      FinalPosition = finalPosition
      Lifetime = lifetime
    }

  /// Applies physics to a particle (gravity, drag, floor collision)
  /// Returns struct (newPosition, newVelocity)
  let inline applyParticlePhysics
    (dt: float32)
    (gravity: float32)
    (drag: float32)
    (floorY: float32)
    (pos: Vector3)
    (vel: Vector3)
    : struct (Vector3 * Vector3) =
    // Gravity
    let newVelY = vel.Y - (gravity * dt)
    let mutable finalVel = Vector3(vel.X, newVelY, vel.Z)

    // Drag
    if drag > 0.0f then
      let dragFactor = MathHelper.Clamp(1.0f - (drag * dt), 0.0f, 1.0f)
      finalVel <- finalVel * dragFactor

    // Floor collision
    let mutable finalPos = pos

    if finalPos.Y < floorY then
      finalPos <- Vector3(finalPos.X, floorY, finalPos.Z)
      finalVel <- Vector3(finalVel.X * 0.1f, 0.0f, finalVel.Z * 0.1f)

    struct (finalPos, finalVel)

  /// Handles burst/rate spawning logic
  let inline processSpawning
    (dt: float32)
    (spawningEnabled: bool)
    (burst: int)
    (rate: int)
    (burstDone: bool ref)
    (accumulator: float32 ref)
    (spawnOne: unit -> unit)
    =
    if spawningEnabled then
      // Burst
      if not burstDone.Value && burst > 0 then
        for _ in 1..burst do
          spawnOne()

        burstDone.Value <- true

      // Rate
      if rate > 0 then
        let rateInterval = 1.0f / float32 rate
        accumulator.Value <- accumulator.Value + dt

        while accumulator.Value > rateInterval do
          accumulator.Value <- accumulator.Value - rateInterval
          spawnOne()

  let updateParticle (dt: float32) (p: Particle) =
    let p = Particle.withLife (p.Life - dt) p

    if p.Life > 0.0f then
      let newPos = p.Position + p.Velocity * dt
      Particle.withPosition newPos p
    else
      p

  /// Updates a mesh particle with 3D rotation physics
  /// Angular velocity is integrated into the rotation quaternion for tumbling effects
  let updateMeshParticle (dt: float32) (p: MeshParticle) =
    let newLife = p.Life - dt

    if newLife > 0.0f then
      // Update position with velocity
      let newPos = p.Position + p.Velocity * dt

      // Integrate angular velocity into rotation
      // Create a quaternion from the angular velocity (axis-angle representation)
      let angVelMag = p.AngularVelocity.Length()

      let newRotation =
        if angVelMag > 0.0001f then
          let axis = Vector3.Normalize p.AngularVelocity
          let angle = angVelMag * dt
          let deltaRot = Quaternion.CreateFromAxisAngle(axis, angle)
          Quaternion.Normalize(deltaRot * p.Rotation)
        else
          p.Rotation

      {
        p with
            Position = newPos
            Rotation = newRotation
            Life = newLife
      }
    else
      { p with Life = newLife }

  let updateEmitter
    (dt: float32)
    (rng: Random)
    (emitter: VisualEmitter)
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
        computeShapeSpawnResult rng emitter.Config.Shape overrides effectiveMode

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

  /// Updates a mesh emitter including spawning and updating mesh particles
  let updateMeshEmitter
    (dt: float32)
    (rng: Random)
    (emitter: VisualMeshEmitter)
    (worldPos: Vector3)
    (ownerVelocity: Vector3)
    (spawningEnabled: bool)
    (ownerRotation: Quaternion)
    (overrides: EffectOverrides)
    =
    let config = emitter.Config

    // Spawn helper - uses shared computeSpawnContext
    let spawnMeshParticle() =
      let ctx =
        computeSpawnContext
          rng
          config
          config.Particle
          worldPos
          ownerVelocity
          ownerRotation
          overrides

      // Rotation based on MeshRotationMode
      let struct (initialRotation, angularVelocity) =
        match config.MeshRotation with
        | Particles.MeshRotationMode.Fixed ->
          // Use EmissionRotation as static mesh orientation
          let rot = config.EmissionRotation

          let baseRot =
            Quaternion.CreateFromYawPitchRoll(
              MathHelper.ToRadians(rot.Y),
              MathHelper.ToRadians(rot.X),
              MathHelper.ToRadians(rot.Z)
            )

          struct (baseRot, Vector3.Zero)
        | Particles.MeshRotationMode.Tumbling ->
          // Random initial + angular velocity for tumbling debris
          let randomRot =
            Quaternion.CreateFromYawPitchRoll(
              float32(rng.NextDouble()) * MathHelper.TwoPi,
              float32(rng.NextDouble()) * MathHelper.TwoPi,
              float32(rng.NextDouble()) * MathHelper.TwoPi
            )

          let angVelScale = 3.0f

          let angVel =
            Vector3(
              (float32(rng.NextDouble()) * 2.0f - 1.0f) * angVelScale,
              (float32(rng.NextDouble()) * 2.0f - 1.0f) * angVelScale,
              (float32(rng.NextDouble()) * 2.0f - 1.0f) * angVelScale
            )

          struct (randomRot, angVel)
        | Particles.MeshRotationMode.RandomStatic ->
          // Random initial, no spinning (settled debris)
          let randomRot =
            Quaternion.CreateFromYawPitchRoll(
              float32(rng.NextDouble()) * MathHelper.TwoPi,
              float32(rng.NextDouble()) * MathHelper.TwoPi,
              float32(rng.NextDouble()) * MathHelper.TwoPi
            )

          struct (randomRot, Vector3.Zero)

      emitter.Particles.Add {
        Position = ctx.FinalPosition
        Velocity = ctx.Velocity
        Rotation = initialRotation
        AngularVelocity = angularVelocity
        Scale = config.Particle.SizeStart
        Life = ctx.Lifetime
        MaxLife = ctx.Lifetime
      }

    // Use shared spawning logic
    processSpawning
      dt
      spawningEnabled
      config.Burst
      config.Rate
      emitter.BurstDone
      emitter.Accumulator
      spawnMeshParticle

    // Update existing mesh particles
    let particles = emitter.Particles
    let mutable i = 0

    while i < particles.Count do
      let p = updateMeshParticle dt particles.[i]

      if p.Life <= 0.0f then
        particles.RemoveAt(i)
      else
        // Use shared physics
        let struct (finalPos, finalVel) =
          applyParticlePhysics
            dt
            config.Particle.Gravity
            config.Particle.Drag
            config.FloorHeight
            p.Position
            p.Velocity

        // Scale lerp
        let t = 1.0f - (p.Life / p.MaxLife)

        let newScale =
          MathHelper.Lerp(config.Particle.SizeStart, config.Particle.SizeEnd, t)

        particles.[i] <- {
          p with
              Position = finalPos
              Velocity = finalVel
              Scale = newScale
        }

        i <- i + 1

  /// Context for particle system update operations
  type ParticleUpdateContext = {
    Dt: float32
    Rng: Random
    Effects: ResizeArray<Particles.VisualEffect>
    EffectsById:
      System.Collections.Generic.Dictionary<string, Particles.VisualEffect>
    ActiveEffectVisuals:
      System.Collections.Generic.Dictionary<Guid<EffectId>, string>
    ParticleStore: Stores.ParticleStore
    // For syncing gameplay effects to visuals (direct from world.ActiveEffects)
    ActiveEffects: HashMap<Guid<EntityId>, Skill.ActiveEffect IndexList>
    // For transform updates (not stable - raw world data)
    Positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>
    Velocities: IReadOnlyDictionary<Guid<EntityId>, Vector3>
    Rotations: IReadOnlyDictionary<Guid<EntityId>, float32>
  }

  /// Sync gameplay effects to visual effects - spawns new visuals for new effects
  let inline syncGameplayEffectsToVisuals
    (ctx: ParticleUpdateContext)
    (currentFrameEffectIds: System.Collections.Generic.HashSet<Guid<EffectId>>)
    =
    ctx.ActiveEffects
    |> HashMap.iter(fun entityId effects ->
      for gameplayEffect in effects do
        match gameplayEffect.SourceEffect.Visuals.VfxId with
        | ValueSome vfxId ->
          currentFrameEffectIds.Add gameplayEffect.Id |> ignore

          if not(ctx.ActiveEffectVisuals.ContainsKey gameplayEffect.Id) then
            match ctx.ParticleStore.tryFind vfxId with
            | ValueSome emitterConfigs ->
              let particleEffectId = Guid.NewGuid().ToString()

              let struct (billboardEmitters, meshEmitters) =
                splitEmittersByRenderMode emitterConfigs

              let newEffect: Particles.VisualEffect = {
                Id = particleEffectId
                Emitters = billboardEmitters
                MeshEmitters = meshEmitters
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
    (effect: Particles.VisualEffect)
    : bool =
    let mutable ownerVelocity = Vector3.Zero
    let mutable ownerRotation = Quaternion.Identity

    match effect.Owner with
    | ValueSome ownerId ->
      // Use raw Positions/Velocities/Rotations (works for all entity types)
      match ctx.Positions.TryFindV ownerId with
      | ValueSome worldPos ->
        effect.Position.Value <- Vector3(worldPos.X, worldPos.Y, worldPos.Z)

        let vel =
          ctx.Velocities.TryFindV ownerId
          |> ValueOption.defaultValue Vector3.Zero

        ownerVelocity <- vel

        let rot =
          ctx.Rotations.TryFindV ownerId |> ValueOption.defaultValue 0.0f

        ownerRotation <-
          Quaternion.CreateFromAxisAngle(Vector3.Up, -rot + MathHelper.PiOver2)

        // Update effect rotation to match owner facing (for local-space particles)
        effect.Rotation.Value <- ownerRotation

      | ValueNone ->
        // Owner doesn't exist - kill the effect
        effect.IsAlive.Value <- false
    | ValueNone -> ()
    // Ownerless effects (combat VFX) stay alive - removal is handled below

    let shouldSpawn = effect.IsAlive.Value
    let mutable anyParticlesAlive = false

    // Update billboard emitters
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

    // Update mesh emitters
    for meshEmitter in effect.MeshEmitters do
      updateMeshEmitter
        ctx.Dt
        ctx.Rng
        meshEmitter
        effect.Position.Value
        ownerVelocity
        shouldSpawn
        ownerRotation
        effect.Overrides

      if meshEmitter.Particles.Count > 0 then
        anyParticlesAlive <- true

    // Determine if effect should be removed
    match effect.Owner with
    | ValueSome _ ->
      // Owned effects: remove when not spawning AND no particles
      not shouldSpawn && not anyParticlesAlive
    | ValueNone ->
      // Ownerless effects (combat VFX): remove when all spawning is done AND no particles
      // This handles cleanup without touching IsAlive (which would affect visual appearance)
      let billboardSpawningDone =
        effect.Emitters
        |> Array.forall(fun emitter ->
          let burstDone = emitter.BurstDone.Value || emitter.Config.Burst = 0
          let noContinuousRate = emitter.Config.Rate = 0
          burstDone && noContinuousRate)

      let meshSpawningDone =
        effect.MeshEmitters
        |> Array.forall(fun emitter ->
          let burstDone = emitter.BurstDone.Value || emitter.Config.Burst = 0
          let noContinuousRate = emitter.Config.Rate = 0
          burstDone && noContinuousRate)

      billboardSpawningDone && meshSpawningDone && not anyParticlesAlive

  type ParticleSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices
    let particleStore = stores.ParticleStore

    // Map Gameplay Effect ID -> Particle Effect ID
    let activeEffectVisuals =
      System.Collections.Generic.Dictionary<Guid<EffectId>, string>()

    let effectsById =
      System.Collections.Generic.Dictionary<string, Particles.VisualEffect>()

    let dt = core.World.Time |> AVal.map(_.Delta.TotalSeconds >> float32)

    override this.Update(gameTime) =
      let dt = dt |> AVal.force

      let effects = core.World.VisualEffects

      // Active effects from world (stable - entities with effects)
      let activeEffects = core.World.ActiveEffects |> AMap.force
      // Raw transforms (not stable - for position updates)
      let positions = core.World.Positions
      let velocities = core.World.Velocities
      let rotations = core.World.Rotations

      let ctx: ParticleUpdateContext = {
        Dt = dt
        Rng = core.World.Rng
        Effects = effects
        EffectsById = effectsById
        ActiveEffectVisuals = activeEffectVisuals
        ParticleStore = particleStore
        ActiveEffects = activeEffects
        Positions = positions
        Velocities = velocities
        Rotations = rotations
      }

      // Detect if effects list was externally cleared (e.g., on map change)
      // and sync our tracking dictionaries
      if effects.Count = 0 && effectsById.Count > 0 then
        effectsById.Clear()
        activeEffectVisuals.Clear()

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

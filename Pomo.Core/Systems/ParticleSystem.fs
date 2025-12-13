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

  let updateParticle (dt: float32) (p: Particle) =
    let p = Particle.withLife (p.Life - dt) p

    if p.Life > 0.0f then
      let newPos = p.Position + p.Velocity * dt
      // No logic to change velocity yet (gravity handled in emit update currently, let's move it here?)
      // Ideally ParticleSystem should handle all physics.
      // But for now, just pos update.
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
      // Returns: (direction, spawnOffset, optionalSpeedOverride)
      let struct (dirLocal, spawnOffsetLocal, speedOverride) =
        match emitter.Config.Shape with
        | EmitterShape.Point ->
          // Uniform sphere direction
          let theta = rng.NextDouble() * 2.0 * Math.PI
          let cosPhi = rng.NextDouble() * 2.0 - 1.0
          let sinPhi = Math.Sqrt(1.0 - cosPhi * cosPhi)
          let x = sinPhi * Math.Cos(theta)
          let y = cosPhi
          let z = sinPhi * Math.Sin(theta)
          let dir = Vector3(float32 x, float32 y, float32 z)
          struct (dir, Vector3.Zero, ValueNone)
        | EmitterShape.Sphere configRadius ->
          // Check for skill-driven area override
          match overrides.Area with
          | ValueSome(SkillArea.Circle(skillRadius, _)) ->
            // Circle: spawn uniformly across disk
            // Random direction
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

            // Spawn offset: uniform disk distribution
            let spawnDist = float32(Math.Sqrt(rng.NextDouble())) * skillRadius
            let spawnAngle = float32(rng.NextDouble() * 2.0 * Math.PI)

            let spawnOffset =
              Vector3(
                spawnDist * MathF.Cos(spawnAngle),
                0.0f,
                spawnDist * MathF.Sin(spawnAngle)
              )

            struct (dir, spawnOffset, ValueNone)

          | ValueSome(SkillArea.Line(width, length, _)) ->
            // Line/Rectangle: spawn uniformly across the rectangular area
            // Direction is upward (will be rotated by effect rotation)
            let dir = Vector3.UnitY

            // Random position along length (0 to length) and width (-width/2 to width/2)
            let alongLength = float32(rng.NextDouble()) * length
            let acrossWidth = float32(rng.NextDouble() - 0.5) * width

            // Spawn along the forward direction (Z in local space before rotation)
            // Width is X, Length is Z
            let spawnOffset = Vector3(acrossWidth, 0.0f, alongLength)

            struct (dir, spawnOffset, ValueNone)

          | _ ->
            // Default: use config radius
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

            let spawnDist = float32(Math.Sqrt(rng.NextDouble())) * configRadius
            let spawnAngle = float32(rng.NextDouble() * 2.0 * Math.PI)

            let spawnOffset =
              Vector3(
                spawnDist * MathF.Cos(spawnAngle),
                0.0f,
                spawnDist * MathF.Sin(spawnAngle)
              )

            struct (dir, spawnOffset, ValueNone)
        | EmitterShape.Cone(configAngle, radius) ->
          // Check for skill-driven angle override
          let angle, lengthOpt =
            match overrides.Area with
            | ValueSome(SkillArea.Cone(skillAngle, skillLength, _)) ->
              skillAngle, ValueSome skillLength
            | ValueSome(SkillArea.AdaptiveCone(skillLength, _)) ->
              configAngle, ValueSome skillLength // Use config angle for adaptive
            | _ -> configAngle, ValueNone

          // Calculate speed override from length if available
          // Speed = Length / AverageLifetime to ensure particles reach the end
          let speedOvr =
            match lengthOpt with
            | ValueSome len ->
              let struct (minLife, maxLife) = config.Lifetime
              let avgLifetime = (minLife + maxLife) / 2.0f
              // Multiply by 1.2 to ensure particles reach slightly beyond
              ValueSome(len / avgLifetime * 1.2f)
            | ValueNone -> ValueNone

          // Use polar coordinates for uniform cone distribution
          let halfAngleRad = MathHelper.ToRadians(angle / 2.0f)
          // Random angle around the cone axis
          let phi = rng.NextDouble() * 2.0 * Math.PI
          // Random offset from center (sqrt for uniform disk distribution)
          let r = Math.Sqrt(rng.NextDouble()) * Math.Sin(float halfAngleRad)
          let x = r * Math.Cos(phi)
          let z = r * Math.Sin(phi)
          // Base Cone points UP (Y), with spread in XZ
          let dir = Vector3(float32 x, 1.0f, float32 z) |> Vector3.Normalize
          struct (dir, Vector3.Zero, speedOvr)

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

  type ParticleSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices
    let particleStore = stores.ParticleStore

    // Map Gameplay Effect ID -> Particle Effect ID
    let activeEffectVisuals =
      System.Collections.Generic.Dictionary<Guid<EffectId>, string>()

    override this.Update(gameTime) =
      let dt = float32 gameTime.ElapsedGameTime.TotalSeconds
      let effects = core.World.VisualEffects
      let positions = core.World.Positions |> AMap.force
      let velocities = core.World.Velocities |> AMap.force
      let rotations = core.World.Rotations |> AMap.force
      let gameplayEffects = core.World.ActiveEffects |> AMap.force

      // --- Sync Gameplay Effects to Visuals ---
      let currentFrameEffectIds =
        System.Collections.Generic.HashSet<Guid<EffectId>>()

      gameplayEffects
      |> HashMap.iter(fun entityId effectList ->
        for gameplayEffect in effectList do
          // Check if effect has visuals
          match gameplayEffect.SourceEffect.Visuals.VfxId with
          | ValueSome vfxId ->
            currentFrameEffectIds.Add(gameplayEffect.Id) |> ignore

            if not(activeEffectVisuals.ContainsKey(gameplayEffect.Id)) then
              // Spawn new visual effect
              match particleStore.tryFind vfxId with
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

                effects.Add(newEffect)
                activeEffectVisuals.Add(gameplayEffect.Id, particleEffectId)
              | ValueNone -> () // VfxId not found in ParticleStore
          | ValueNone -> ())

      // Cleanup expired effects
      let toRemove = ResizeArray<Guid<EffectId>>()

      for kvp in activeEffectVisuals do
        if not(currentFrameEffectIds.Contains(kvp.Key)) then
          // Effect expired in gameplay, stop spawning visual
          let particleEffectId = kvp.Value

          match effects |> Seq.tryFind(fun e -> e.Id = particleEffectId) with
          | Some e -> e.IsAlive.Value <- false
          | None -> ()

          toRemove.Add(kvp.Key)

      for key in toRemove do
        activeEffectVisuals.Remove(key) |> ignore

      // --- Update Visual Effects ---

      for i = effects.Count - 1 downto 0 do
        let effect = effects.[i]

        // Track Owner Velocity for inheritance
        let mutable ownerVelocity = Vector3.Zero
        let mutable ownerRotation = Quaternion.Identity

        // Sync with owner
        match effect.Owner with
        | ValueSome ownerId ->
          match positions.TryFindV ownerId with
          | ValueSome pos ->
            // Map Entity (X, Y) to Particle (X, Z). Particle Y is Altitude (Up).
            effect.Position.Value <- Vector3(pos.X, 0.0f, pos.Y)

            // Get velocity if available
            match velocities.TryFindV ownerId with
            | ValueSome vel -> ownerVelocity <- Vector3(vel.X, 0.0f, vel.Y)
            | ValueNone -> ()

            // Get rotation if available (Yaw)
            match rotations.TryFindV ownerId with
            | ValueSome rot ->
              ownerRotation <-
                Quaternion.CreateFromAxisAngle(
                  Vector3.Up,
                  -rot + MathHelper.PiOver2
                ) // Negative rot for standard math?
            | ValueNone -> ()

          | ValueNone -> effect.IsAlive.Value <- false
        | ValueNone -> ()

        let shouldSpawn = effect.IsAlive.Value
        let mutable anyParticlesAlive = false

        for emitter in effect.Emitters do
          updateEmitter
            dt
            core.World.Rng
            emitter
            effect.Position.Value
            ownerVelocity
            shouldSpawn
            ownerRotation
            effect.Overrides

          if emitter.Particles.Count > 0 then
            anyParticlesAlive <- true

        if not shouldSpawn && not anyParticlesAlive then
          effects.RemoveAt(i)

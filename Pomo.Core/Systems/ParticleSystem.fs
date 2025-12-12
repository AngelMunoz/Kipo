namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Particles
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
    (spawningEnabled: bool)
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

      // Direction and spawn offset based on shape
      let struct (dir, spawnOffset) =
        match emitter.Config.Shape with
        | Point ->
          // Random direction, spawn at center
          let theta = rng.NextDouble() * 2.0 * Math.PI
          let phi = rng.NextDouble() * Math.PI
          let x = Math.Sin(phi) * Math.Cos(theta)
          let y = Math.Cos(phi)
          let z = Math.Sin(phi) * Math.Sin(theta)
          struct (Vector3(float32 x, float32 y, float32 z), Vector3.Zero)
        | Sphere r ->
          // Spherical direction, spawn on sphere surface at radius
          let theta = rng.NextDouble() * 2.0 * Math.PI
          let phi = rng.NextDouble() * Math.PI
          let x = Math.Sin(phi) * Math.Cos(theta)
          let y = Math.Cos(phi)
          let z = Math.Sin(phi) * Math.Sin(theta)
          let dir = Vector3(float32 x, float32 y, float32 z)
          // Spawn ON the sphere surface, move OUTWARD
          struct (dir, dir * r)
        | Cone(angle, radius) ->
          let rad = MathHelper.ToRadians(angle)
          let x = (rng.NextDouble() * 2.0 - 1.0) * Math.Sin(float rad)
          let z = (rng.NextDouble() * 2.0 - 1.0) * Math.Sin(float rad)
          let dir = Vector3(float32 x, 1.0f, float32 z) |> Vector3.Normalize
          struct (dir, Vector3.Zero)

      let velocity = dir * speed

      let p = {
        Position = worldPos + emitter.Config.LocalOffset + spawnOffset
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

        let p =
          Particle.withVelocity (Vector3(p.Velocity.X, newVelY, p.Velocity.Z)) p

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

    override this.Update(gameTime) =
      let dt = float32 gameTime.ElapsedGameTime.TotalSeconds
      let effects = core.World.VisualEffects
      let positions = core.World.Positions |> AMap.force

      for i = effects.Count - 1 downto 0 do
        let effect = effects.[i]

        // Sync with owner
        match effect.Owner with
        | ValueSome ownerId ->
          match positions.TryFindV ownerId with
          | ValueSome pos ->
            // Map Entity (X, Y) to Particle (X, Z). Particle Y is Altitude (Up).
            effect.Position.Value <- Vector3(pos.X, 0.0f, pos.Y)
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
            shouldSpawn

          if emitter.Particles.Count > 0 then
            anyParticlesAlive <- true

        if not shouldSpawn && not anyParticlesAlive then
          effects.RemoveAt(i)

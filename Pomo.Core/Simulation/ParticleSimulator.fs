namespace Pomo.Core.Simulation

open System
open Microsoft.Xna.Framework
open Pomo.Core.Domain.Particles

[<Struct>]
type SimulatedParticle = {
  LocalOffset: Vector3
  Velocity: Vector3
  Color: Color
  Size: float32
  Life: float32
}

type VisualEffectState = {
  Id: string
  Particles: SimulatedParticle[]
  Accumulator: float32
  IsAlive: bool
}

module ParticleSimulator =

  /// Updates a single particle's state based on physics and config
  let inline private updateParticle
    (dt: float32)
    (gravity: float32)
    (drag: float32)
    (p: SimulatedParticle)
    : SimulatedParticle =

    let newLife = p.Life - dt

    if newLife <= 0.0f then
      { p with Life = newLife }
    else
      // Apply forces
      let velocityWithGravity = p.Velocity + Vector3.Down * gravity * dt
      let velocityWithDrag = velocityWithGravity * (1.0f - drag * dt)

      // Update position
      let newPos = p.LocalOffset + velocityWithDrag * dt

      {
        p with
            LocalOffset = newPos
            Velocity = velocityWithDrag
            Life = newLife
      }

  /// Pure update function for the entire effect state
  let update
    (dt: float32)
    (config: ParticleConfig)
    (state: VisualEffectState)
    : VisualEffectState =
    if not state.IsAlive then
      state
    else
      let updatedParticles =
        state.Particles
        |> Array.Parallel.map(updateParticle dt config.Gravity config.Drag)

      let aliveCount =
        updatedParticles |> Array.sumBy(fun p -> if p.Life > 0.0f then 1 else 0)

      // If all particles are dead and we assume no new emission (for this simple step), mark dead
      // Note: Real system needs to handle emission accumulation too, but that might be separate
      let isAlive = aliveCount > 0

      {
        state with
            Particles = updatedParticles
            IsAlive = isAlive
      }

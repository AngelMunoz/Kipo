namespace Pomo.Core.Simulation

open System
open Microsoft.Xna.Framework

[<Struct>]
type SimulatedOrbital = {
  LogicPosition: Vector2
  Altitude: float32
  Angle: float32
  Radius: float32
  Speed: float32
}

type OrbitalState = { Orbitals: SimulatedOrbital[] }

module OrbitalSimulator =

  let inline private updateOrbital
    (dt: float32)
    (center: Vector2)
    (orbital: SimulatedOrbital)
    : SimulatedOrbital =
    let newAngle = orbital.Angle + orbital.Speed * dt

    let offset =
      Vector2(MathF.Cos newAngle, MathF.Sin newAngle) * orbital.Radius

    {
      orbital with
          LogicPosition = center + offset
          Angle = newAngle
    }

  let update
    (dt: float32)
    (center: Vector2)
    (state: OrbitalState)
    : OrbitalState =
    let updatedOrbitals = state.Orbitals |> Array.map(updateOrbital dt center)

    { Orbitals = updatedOrbitals }

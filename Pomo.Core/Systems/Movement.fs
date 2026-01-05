namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain.Events
open Pomo.Core.Systems.Systems


module Movement =

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type MovementSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Gameplay gameplay) = env.GameplayServices
    let (Core core) = env.CoreServices
    let stateWrite = core.StateWrite

    override val Kind = Movement with get

    override this.Update _ =
      let scenarios = core.World.Scenarios |> AMap.force

      for scenarioId, _ in scenarios do
        let snapshot =
          gameplay.Projections.ComputeMovement3DSnapshot(scenarioId)

        for KeyValue(id, newPosition) in snapshot.Positions do
          stateWrite.UpdatePosition(id, newPosition)

        for KeyValue(id, newRotation) in snapshot.Rotations do
          stateWrite.UpdateRotation(id, newRotation)

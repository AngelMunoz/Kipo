namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Core.Constants
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Algorithms
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
      let liveProjectiles = core.World.LiveProjectiles |> AMap.force

      for scenarioId, scenario in scenarios do
        let snapshot =
          gameplay.Projections.ComputeMovement3DSnapshot(scenarioId)

        for KeyValue(id, proposedPos) in snapshot.Positions do
          // Projectiles handle their own physics - pass through unchanged
          let isProjectile = liveProjectiles |> HashMap.containsKey id

          let finalPos =
            if isProjectile then
              // Projectile: no grounding, preserve Y
              proposedPos
            else
              // Ground entity: apply block collision with grounding
              match scenario.BlockMap with
              | ValueSome blockMap ->
                // Get the committed position to calculate proper collision
                let startPos =
                  core.World.Positions
                  |> Dictionary.tryFindV id
                  |> ValueOption.defaultValue proposedPos

                let velocity =
                  core.World.Velocities
                  |> Dictionary.tryFindV id
                  |> ValueOption.defaultValue Vector3.Zero

                if velocity <> Vector3.Zero then
                  let dt =
                    core.World.Time
                    |> AVal.force
                    |> fun t -> float32 t.Delta.TotalSeconds

                  // Extract XZ for block collision (ground movement)
                  let velocity2D = Vector2(velocity.X, velocity.Z)

                  BlockCollision.applyCollision
                    blockMap
                    startPos
                    velocity2D
                    dt
                    BlockMap.CellSize
                else
                  proposedPos
              | ValueNone -> proposedPos

          stateWrite.UpdatePosition(id, finalPos)

        for KeyValue(id, newRotation) in snapshot.Rotations do
          stateWrite.UpdateRotation(id, newRotation)

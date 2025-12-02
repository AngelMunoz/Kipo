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

    override val Kind = Movement with get

    override this.Update _ =
      let scenarios = core.World.Scenarios |> AMap.force

      for (scenarioId, _) in scenarios do
        let movements =
          gameplay.Projections.ComputeMovementSnapshot(scenarioId).Positions

        for id, newPosition in movements do
          core.EventBus.Publish(
            Physics(PositionChanged struct (id, newPosition))
          )

        let rotations =
          gameplay.Projections.ComputeMovementSnapshot(scenarioId).Rotations

        for id, newRotation in rotations do
          core.EventBus.Publish(
            Physics(RotationChanged struct (id, newRotation))
          )

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
  open Pomo.Core.Domain
  open Pomo.Core.Domain.Skill

  let calculateFinalPosition
    (world: World.World)
    (blockMap: BlockMapDefinition)
    (id: Guid<Units.EntityId>)
    (proposedPos: WorldPosition)
    (dt: float32)
    : WorldPosition =
    // Get the committed position to calculate proper collision
    let startPos =
      world.Positions
      |> Dictionary.tryFindV id
      |> ValueOption.defaultValue proposedPos

    let velocity =
      world.Velocities
      |> Dictionary.tryFindV id
      |> ValueOption.defaultValue Vector3.Zero

    if velocity <> Vector3.Zero then
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

  let private shouldRefreshEffect
    (totalTime: TimeSpan)
    (duration: Duration)
    (activeEffect: ActiveEffect)
    =
    match duration with
    | Duration.Permanent
    | Duration.PermanentLoop _ -> false // Infinite duration effects don't need to be re-applied
    | Duration.Instant -> true
    | Duration.Timed d
    | Duration.Loop(_, d) ->
      let elapsed = totalTime - activeEffect.StartTime
      let remaining = d - elapsed
      remaining.TotalSeconds < 0.5

  let tryApplyBlockEffect
    (core: CoreServices)
    (activeEffects: HashMap<Guid<Units.EntityId>, IndexList<ActiveEffect>>)
    (id: Guid<Units.EntityId>)
    (finalPos: WorldPosition)
    (blockMap: BlockMapDefinition)
    =
    let cell = BlockMap.worldPositionToCell finalPos

    // Check current cell (e.g. inside liquid/gas) or below (standing on floor)
    let effect =
      BlockMap.getBlockEffect cell blockMap
      |> ValueOption.orElseWith(fun () ->
        let below = { cell with Y = cell.Y - 1 }
        BlockMap.getBlockEffect below blockMap)

    effect
    |> ValueOption.iter(fun e ->
      let shouldApply =
        activeEffects
        |> HashMap.tryFindV id
        |> ValueOption.bind(fun existingEffects ->
          existingEffects
          |> IndexList.tryFind(fun _ ae -> ae.SourceEffect.Name = e.Name)
          |> ValueOption.ofOption)
        |> ValueOption.map(fun activeEffect ->
          let totalTime =
            core.World.Time |> AVal.map _.TotalGameTime |> AVal.force

          shouldRefreshEffect totalTime e.Duration activeEffect)
        |> ValueOption.defaultValue true

      if shouldApply then
        let intent: SystemCommunications.EffectApplicationIntent = {
          SourceEntity = id
          TargetEntity = id
          Effect = e
        }

        core.EventBus.Publish(
          GameEvent.Intent(IntentEvent.EffectApplication intent)
        ))

  type MovementSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Gameplay gameplay) = env.GameplayServices
    let (Core core) = env.CoreServices
    let stateWrite = core.StateWrite

    override val Kind = Movement with get

    override this.Update _ =
      let scenarios = core.World.Scenarios |> AMap.force
      let liveProjectiles = core.World.LiveProjectiles |> AMap.force
      let activeEffects = core.World.ActiveEffects |> AMap.force

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
                let dt =
                  core.World.Time
                  |> AVal.force
                  |> fun t -> float32 t.Delta.TotalSeconds

                calculateFinalPosition core.World blockMap id proposedPos dt
              | ValueNone -> proposedPos

          stateWrite.UpdatePosition(id, finalPos)

          // Check for block effects
          scenario.BlockMap
          |> ValueOption.iter(
            tryApplyBlockEffect core activeEffects id finalPos
          )

        for KeyValue(id, newRotation) in snapshot.Rotations do
          stateWrite.UpdateRotation(id, newRotation)

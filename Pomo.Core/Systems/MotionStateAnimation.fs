namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Animation
open Pomo.Core.Environment
open Pomo.Core.Domain.World
open Pomo.Core.Projections
open Pomo.Core.Stores

module AnimationStateLogic =

  let private RUN_THRESHOLD = 10.0f

  let private createAnimationsFromBindings(clipIds: string[]) =
    clipIds
    |> Array.map(fun clipId -> {
      ClipId = clipId
      Time = TimeSpan.Zero
      Speed = 1.0f
    })

  let private hasAnyOfClips (clipIds: string[]) (animations: AnimationState[]) =
    animations
    |> Array.exists(fun anim -> clipIds |> Array.contains anim.ClipId)

  /// Determines what animation action to take based on movement state.
  /// Returns: Some true = start run animation, Some false = stop animation, None = no change
  let determineAnimationAction
    (currentVelocity: Vector2)
    (currentActiveAnimations: AnimationState[])
    (runClipIds: string[] voption)
    : (bool * AnimationState[] voption) voption =

    match runClipIds with
    | ValueNone -> ValueNone
    | ValueSome clips when clips.Length = 0 -> ValueNone
    | ValueSome clips ->
      let speed = currentVelocity.Length()
      let isMoving = speed > RUN_THRESHOLD
      let isRunAnimActive = hasAnyOfClips clips currentActiveAnimations

      if isMoving && not isRunAnimActive then
        let runAnims = createAnimationsFromBindings clips
        ValueSome(true, ValueSome runAnims) // Start run animation
      elif not isMoving && isRunAnimActive then
        ValueSome(false, ValueNone) // Stop animation
      else
        ValueNone // No change

open Pomo.Core.Environment.Patterns

type MotionStateAnimationSystem(game: Game, env: PomoEnvironment) =
  inherit GameComponent(game)

  let (Core core) = env.CoreServices
  let (Stores stores) = env.StoreServices
  let stateWrite = core.StateWrite

  override this.Update(gameTime) =
    // Force world data directly - no reactive projection
    let velocities = core.World.Velocities
    let activeAnimations = core.World.ActiveAnimations
    let resources = core.World.Resources |> AMap.force
    let modelConfigIds = core.World.ModelConfigId |> AMap.force

    for entityId, configId in modelConfigIds do
      // Filter: only alive entities
      let isAlive =
        resources
        |> HashMap.tryFind entityId
        |> Option.map(fun r -> r.Status = Entity.Status.Alive)
        |> Option.defaultValue false

      if isAlive then
        // Get velocity and animations
        let velocity =
          velocities
          |> Dictionary.tryFindV entityId
          |> ValueOption.defaultValue Vector2.Zero

        let currentAnims =
          activeAnimations
          |> Dictionary.tryFindV entityId
          |> ValueOption.defaultValue Array.empty

        // Resolve RunClipIds from ModelStore
        let runClipIds =
          stores.ModelStore.tryFind configId
          |> ValueOption.bind(fun cfg ->
            cfg.AnimationBindings |> HashMap.tryFindV "Run")

        match
          AnimationStateLogic.determineAnimationAction
            velocity
            currentAnims
            runClipIds
        with
        | ValueSome(true, ValueSome runAnims) ->
          stateWrite.UpdateActiveAnimations(entityId, runAnims)
        | ValueSome(false, _) -> stateWrite.RemoveAnimationState(entityId)
        | _ -> ()

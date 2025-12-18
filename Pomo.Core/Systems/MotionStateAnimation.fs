namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Animation
open Pomo.Core.Environment
open Pomo.Core.Domain.World
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
    |> IndexList.ofArray

  let private hasAnyOfClips
    (clipIds: string[])
    (animations: AnimationState IndexList)
    =
    animations
    |> IndexList.exists(fun _ anim -> clipIds |> Array.contains anim.ClipId)

  let determineAnimationChange
    (entityId: Guid<EntityId>)
    (currentVelocity: Vector2)
    (currentActiveAnimations: AnimationState IndexList)
    (runClipIds: string[] voption)
    : StateChangeEvent voption =

    match runClipIds with
    | ValueNone -> ValueNone
    | ValueSome clips when clips.Length = 0 -> ValueNone
    | ValueSome clips ->
      let speed = currentVelocity.Length()
      let isMoving = speed > RUN_THRESHOLD
      let isRunAnimActive = hasAnyOfClips clips currentActiveAnimations

      if isMoving && not isRunAnimActive then
        let runAnims = createAnimationsFromBindings clips

        ValueSome(
          Animation(ActiveAnimationsChanged struct (entityId, runAnims))
        )
      elif not isMoving && isRunAnimActive then
        ValueSome(Animation(AnimationStateRemoved entityId))
      else
        ValueNone

open Pomo.Core.Environment.Patterns

type MotionStateAnimationSystem(game: Game, env: PomoEnvironment) =
  inherit GameComponent(game)

  let (Core core) = env.CoreServices
  let (Stores stores) = env.StoreServices

  override this.Update(gameTime) =
    // Force world data directly - no reactive projection
    let velocities = core.World.Velocities |> AMap.force
    let activeAnimations = core.World.ActiveAnimations |> AMap.force
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
          |> HashMap.tryFind entityId
          |> Option.defaultValue Vector2.Zero

        let currentAnims =
          activeAnimations
          |> HashMap.tryFind entityId
          |> Option.defaultValue IndexList.empty

        // Resolve RunClipIds from ModelStore
        let runClipIds =
          stores.ModelStore.tryFind configId
          |> ValueOption.bind(fun cfg ->
            cfg.AnimationBindings |> HashMap.tryFindV "Run")

        match
          AnimationStateLogic.determineAnimationChange
            entityId
            velocity
            currentAnims
            runClipIds
        with
        | ValueSome event -> core.EventBus.Publish(GameEvent.State event)
        | ValueNone -> ()

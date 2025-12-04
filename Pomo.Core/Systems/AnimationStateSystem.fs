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

// This module contains the core logic for deciding which animation should play
// based on game state (e.g., movement speed).
module AnimationStateLogic =

  let private RUN_THRESHOLD = 10.0f // Speed threshold to trigger run animation
  let private RUN_ANIMATION_CLIP_ID = "Test_Windmill" // Placeholder run animation clip ID

  // Determines if an animation state change event should be published
  // Returns a ValueSome event if a change is needed, otherwise ValueNone
  let determineAnimationChange
    (entityId: Guid<EntityId>)
    (currentVelocity: Vector2)
    (currentActiveAnimations: AnimationState IndexList) // All animations currently active on the entity
    : StateChangeEvent voption =

    let speed = currentVelocity.Length()
    let isMoving = speed > RUN_THRESHOLD

    // Check if the "run" animation is currently active
    let isRunAnimActive =
      currentActiveAnimations
      |> IndexList.exists(fun _ (anim: AnimationState) ->
        anim.ClipId = RUN_ANIMATION_CLIP_ID)

    if isMoving && not isRunAnimActive then
      // Entity is moving but run animation is not active: Start run animation
      let runAnimState = {
        ClipId = RUN_ANIMATION_CLIP_ID
        Time = TimeSpan.Zero
        Speed = 1.0f
      }
      // For simplicity, we replace all animations with just the run animation.
      // A more complex system would handle blending/layering.
      ValueSome(
        Animation(
          ActiveAnimationsChanged
            struct (entityId, IndexList.single runAnimState)
        )
      )
    elif not isMoving && isRunAnimActive then
      // Entity is stationary but run animation is active: Stop run animation
      // For simplicity, we remove all animations, effectively transitioning to an implicit "idle"
      ValueSome(Animation(AnimationStateRemoved entityId))
    else
      // No change needed
      ValueNone

open Pomo.Core.Environment.Patterns

type AnimationStateSystem(game: Game, env: PomoEnvironment) =
  inherit GameComponent(game)

  let (Core core) = env.CoreServices
  let (Gameplay gameplay) = env.GameplayServices

  override this.Update(gameTime) =
    let velocities = core.World.Velocities |> AMap.force
    let activeAnimations = core.World.ActiveAnimations |> AMap.force
    let liveEntities = gameplay.Projections.LiveEntities |> ASet.force

    // Process each live entity
    for entityId in liveEntities do
      let velocity =
        velocities
        |> HashMap.tryFindV entityId
        |> ValueOption.defaultValue Vector2.Zero

      let currentAnims =
        activeAnimations
        |> HashMap.tryFindV entityId
        |> ValueOption.defaultValue IndexList.empty // Default to empty list if no animations

      match
        AnimationStateLogic.determineAnimationChange
          entityId
          velocity
          currentAnims
      with
      | ValueSome event -> core.EventBus.Publish(event)
      | ValueNone -> () // No animation state change needed

namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Core.Domain
open Pomo.Core.Domain.Animation
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Entity
open Pomo.Core.Environment
open Pomo.Core.Stores
open Pomo.Core.Domain.World

module AnimationSystemLogic =

  /// Helper to find the two relevant keyframes and interpolation amount for a given time.
  /// Assumes keyframes are sorted by time.
  [<TailCall>]
  let rec private findKeyframesForTime
    (keyframes: Keyframe[])
    (time: TimeSpan)
    (idx: int)
    : struct (Keyframe * Keyframe * float32) =
    if idx >= keyframes.Length - 1 then
      let last = keyframes.[keyframes.Length - 1]
      struct (last, last, 0.0f) // If past last keyframe, use the last one
    else
      let k1 = keyframes.[idx]
      let k2 = keyframes.[idx + 1]

      if time >= k1.Time && time <= k2.Time then
        let total = (k2.Time - k1.Time).TotalSeconds
        let current = (time - k1.Time).TotalSeconds
        let amount = if total > 0.0001 then float32(current / total) else 0.0f
        struct (k1, k2, amount)
      else
        findKeyframesForTime keyframes time (idx + 1)

  /// Calculates the interpolated rotation and position for a given track at a specific time.
  let evaluateTrack
    (track: Track)
    (time: TimeSpan)
    (duration: TimeSpan)
    (loop: bool)
    : struct (Quaternion * Vector3) =
    if track.Keyframes.Length = 0 then
      struct (Quaternion.Identity, Vector3.Zero)
    else
      // Handle time wrapping
      let t =
        if loop && duration > TimeSpan.Zero then
          TimeSpan.FromTicks(time.Ticks % duration.Ticks)
        else if
          // Clamp time to duration if not looping
          time > duration
        then
          duration
        else
          time

      let struct (k1, k2, amount) = findKeyframesForTime track.Keyframes t 0

      let rotation = Quaternion.Slerp(k1.Rotation, k2.Rotation, amount)
      let position = Vector3.Lerp(k1.Position, k2.Position, amount)

      struct (rotation, position)

  /// Advances an animation state and returns the updated state or ValueNone if finished.
  let private updateAnimationState
    (animState: AnimationState)
    (clip: AnimationClip)
    (gameTimeDelta: TimeSpan)
    : AnimationState voption =
    let newTime = animState.Time + gameTimeDelta * float animState.Speed

    if not clip.IsLooping && newTime >= clip.Duration then
      ValueNone // Animation finished and not looping
    else
      ValueSome { animState with Time = newTime }

  /// Processes all active animations for a single entity, returning updated animations and its pose.
  let processEntityAnimations
    (activeAnims: AnimationState IndexList)
    (animationStore: AnimationStore)
    (gameTimeDelta: TimeSpan)
    : (AnimationState IndexList * HashMap<string, Matrix>) voption =

    let mutable updatedAnims = IndexList.empty
    let mutable entityPose = HashMap.empty<string, Matrix>
    let mutable hasActiveAnims = false

    for animState in activeAnims do
      match animationStore.tryFind animState.ClipId with
      | ValueSome clip ->
        match updateAnimationState animState clip gameTimeDelta with
        | ValueSome newAnimState ->
          // Calculate pose for this clip
          for track in clip.Tracks do
            let struct (rotation, position) =
              evaluateTrack track newAnimState.Time clip.Duration clip.IsLooping

            let matrix =
              Matrix.CreateFromQuaternion(rotation)
              * Matrix.CreateTranslation(position)

            // For now, simple overwrite (last animation in list wins if tracks overlap)
            entityPose <- HashMap.add track.NodeName matrix entityPose

          updatedAnims <- IndexList.add newAnimState updatedAnims
          hasActiveAnims <- true
        | ValueNone -> () // Animation finished and removed
      | ValueNone -> () // Clip not found, animation removed

    if hasActiveAnims then
      ValueSome(updatedAnims, entityPose)
    else
      ValueNone

type AnimationSystem(game: Game, env: PomoEnvironment) =
  inherit GameComponent(game)

  let (Core core) = env.CoreServices
  let (Stores stores) = env.StoreServices

  override _.Update _ =
    // Get the game time delta from the World's time source
    let gameTimeDelta = core.World.Time |> AVal.map _.Delta |> AVal.force
    let currentAnims = core.World.ActiveAnimations |> AMap.force

    // Collect updates and removals to apply
    let updates =
      ResizeArray<
        struct (Guid<EntityId> *
        AnimationState IndexList *
        HashMap<string, Matrix>)
       >()

    let removals = ResizeArray<Guid<EntityId>>()

    // Iterate over snapshot
    for entityId, anims in currentAnims do
      match
        AnimationSystemLogic.processEntityAnimations
          anims
          stores.AnimationStore
          gameTimeDelta
      with
      | ValueSome(newAnims, newPose) ->
        updates.Add((entityId, newAnims, newPose))
      | ValueNone -> removals.Add entityId

    // Publish events
    for entityId, newAnims, newPose in updates do
      core.EventBus.Publish(
        Animation(ActiveAnimationsChanged struct (entityId, newAnims))
      )

      core.EventBus.Publish(Animation(PoseChanged struct (entityId, newPose)))

    for entityId in removals do
      core.EventBus.Publish(Animation(AnimationStateRemoved entityId))

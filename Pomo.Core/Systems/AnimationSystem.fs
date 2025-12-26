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
open Pomo.Core

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

  /// Processes all active animations for a single entity, mutating the pose dictionary in-place.
  /// Updates the animations array in-place if count matches, otherwise returns new array.
  /// Returns: struct (updated:bool, newArrayNeeded:AnimationState[] voption)
  let processEntityAnimationsInPlace
    (activeAnims: AnimationState[])
    (animationStore: AnimationStore)
    (poseDict: System.Collections.Generic.Dictionary<string, Matrix>)
    (gameTimeDelta: TimeSpan)
    : struct (bool * AnimationState[] voption) =

    poseDict.Clear()
    let mutable writeIndex = 0

    for i = 0 to activeAnims.Length - 1 do
      let animState = activeAnims[i]

      match animationStore.tryFind animState.ClipId with
      | ValueSome clip ->
        match updateAnimationState animState clip gameTimeDelta with
        | ValueSome newAnimState ->
          for track in clip.Tracks do
            let struct (rotation, position) =
              evaluateTrack track newAnimState.Time clip.Duration clip.IsLooping

            let matrix =
              Matrix.CreateFromQuaternion(rotation)
              * Matrix.CreateTranslation(position)

            poseDict[track.NodeName] <- matrix

          // Write back to array at current write position
          activeAnims[writeIndex] <- newAnimState
          writeIndex <- writeIndex + 1
        | ValueNone -> ()
      | ValueNone -> ()

    if writeIndex = 0 then
      // All animations finished
      struct (false, ValueNone)
    elif writeIndex = activeAnims.Length then
      // Same count - array was updated in-place, no new array needed
      struct (true, ValueNone)
    else
      // Fewer animations - need to return a right-sized array
      let newArray = Array.zeroCreate writeIndex
      System.Array.Copy(activeAnims, newArray, writeIndex)
      struct (true, ValueSome newArray)

type AnimationSystem(game: Game, env: PomoEnvironment) =
  inherit GameComponent(game)

  let (Core core) = env.CoreServices
  let (Stores stores) = env.StoreServices
  let stateWrite = core.StateWrite

  override _.Update _ =
    let gameTimeDelta = core.World.Time |> AVal.map _.Delta |> AVal.force
    let currentAnims = core.World.ActiveAnimations
    let poses = core.World.Poses

    let animUpdates = ResizeArray<struct (Guid<EntityId> * AnimationState[])>()

    let newPoses =
      ResizeArray<
        struct (Guid<EntityId> *
        System.Collections.Generic.Dictionary<string, Matrix>)
       >()

    let removals = ResizeArray<Guid<EntityId>>()

    for KeyValue(entityId, anims) in currentAnims do
      let poseDict =
        match poses |> Dictionary.tryFindV entityId with
        | ValueSome existing -> existing
        | ValueNone ->
          let newDict = System.Collections.Generic.Dictionary<string, Matrix>()
          newPoses.Add(struct (entityId, newDict))
          newDict

      let struct (hasActiveAnims, newArrayOpt) =
        AnimationSystemLogic.processEntityAnimationsInPlace
          anims
          stores.AnimationStore
          poseDict
          gameTimeDelta

      if hasActiveAnims then
        match newArrayOpt with
        | ValueSome newArray -> animUpdates.Add(struct (entityId, newArray))
        | ValueNone -> () // Array was updated in-place, no storage update needed
      else
        removals.Add entityId

    // Only update storage for entities that needed new arrays
    for struct (entityId, newAnims) in animUpdates do
      stateWrite.UpdateActiveAnimations(entityId, newAnims)

    for struct (entityId, poseDict) in newPoses do
      stateWrite.UpdatePose(entityId, poseDict)

    for entityId in removals do
      stateWrite.RemoveAnimationState(entityId)

namespace Pomo.Core.UI

open System

module HUDAnimation =

  module Easing =

    let inline linear(t: float32) = t

    let inline easeOutQuad(t: float32) = 1.0f - (1.0f - t) * (1.0f - t)

    let inline easeOutCubic(t: float32) =
      let inv = 1.0f - t
      1.0f - inv * inv * inv

    let inline easeInQuad(t: float32) = t * t

    let inline easeInOutQuad(t: float32) =
      if t < 0.5f then
        2.0f * t * t
      else
        1.0f - (-2.0f * t + 2.0f) * (-2.0f * t + 2.0f) / 2.0f

    let inline easeOutElastic(t: float32) =
      if t = 0.0f then
        0.0f
      elif t = 1.0f then
        1.0f
      else
        let c4 = (2.0f * MathF.PI) / 3.0f
        MathF.Pow(2.0f, -10.0f * t) * MathF.Sin((t * 10.0f - 0.75f) * c4) + 1.0f

    let inline easeOutBounce(t: float32) =
      let n1 = 7.5625f
      let d1 = 2.75f

      if t < 1.0f / d1 then
        n1 * t * t
      elif t < 2.0f / d1 then
        let t' = t - 1.5f / d1
        n1 * t' * t' + 0.75f
      elif t < 2.5f / d1 then
        let t' = t - 2.25f / d1
        n1 * t' * t' + 0.9375f
      else
        let t' = t - 2.625f / d1
        n1 * t' * t' + 0.984375f


  module Lerp =

    let inline lerpF (start: float32) (target: float32) (t: float32) =
      start + (target - start) * t

    let inline lerpI (start: int) (target: int) (t: float32) =
      start + int(float32(target - start) * t)

    let inline smoothDamp
      (current: float32)
      (target: float32)
      (smoothTime: float32)
      (deltaTime: float32)
      =
      if smoothTime <= 0.0f then
        target
      else
        let omega = 2.0f / smoothTime
        let x = omega * deltaTime
        let exp = 1.0f / (1.0f + x + 0.48f * x * x + 0.235f * x * x * x)
        current + (target - current) * (1.0f - exp)

    let inline moveToward
      (current: float32)
      (target: float32)
      (maxDelta: float32)
      =
      let diff = target - current

      if MathF.Abs(diff) <= maxDelta then
        target
      else
        current + float32(MathF.Sign(diff)) * maxDelta


  [<Struct>]
  type PulseState = { Phase: float32; Intensity: float32 }

  module Pulse =
    let inline create() = { Phase = 0.0f; Intensity = 0.0f }

    let inline update
      (frequency: float32)
      (deltaTime: float32)
      (state: PulseState)
      =
      let newPhase = (state.Phase + frequency * deltaTime) % 1.0f
      let intensity = (MathF.Sin(newPhase * MathF.PI * 2.0f) + 1.0f) / 2.0f

      {
        Phase = newPhase
        Intensity = intensity
      }

    let inline flash(progress: float32) = 1.0f - progress

    let inline oscillate (progress: float32) (frequency: float32) =
      (MathF.Sin(progress * frequency * MathF.PI * 2.0f) + 1.0f) / 2.0f


  [<Struct>]
  type TweenState = {
    StartValue: float32
    TargetValue: float32
    CurrentValue: float32
    Elapsed: float32
    Duration: float32
  }

  module Tween =
    let inline create
      (startValue: float32)
      (targetValue: float32)
      (duration: float32)
      =
      {
        StartValue = startValue
        TargetValue = targetValue
        CurrentValue = startValue
        Elapsed = 0.0f
        Duration = duration
      }

    let inline update
      (deltaTime: float32)
      (easingFn: float32 -> float32)
      (state: TweenState)
      =
      let newElapsed = MathF.Min(state.Elapsed + deltaTime, state.Duration)

      let t =
        if state.Duration > 0.0f then
          newElapsed / state.Duration
        else
          1.0f

      let easedT = easingFn t

      let newValue =
        state.StartValue + (state.TargetValue - state.StartValue) * easedT

      {
        state with
            Elapsed = newElapsed
            CurrentValue = newValue
      }

    let inline isComplete(state: TweenState) = state.Elapsed >= state.Duration

    let inline retarget
      (newTarget: float32)
      (duration: float32)
      (state: TweenState)
      =
      {
        StartValue = state.CurrentValue
        TargetValue = newTarget
        CurrentValue = state.CurrentValue
        Elapsed = 0.0f
        Duration = duration
      }

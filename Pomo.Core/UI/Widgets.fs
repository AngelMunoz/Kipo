namespace Pomo.Core.UI

open System
open Microsoft.Xna.Framework
open Myra.Graphics2D
open Myra.Graphics2D.UI
open FSharp.Data.Adaptive
open Pomo.Core.Domain.World
open Pomo.Core.UI.HUDAnimation


/// Controls when a ResourceBar pulses
[<Struct>]
type PulseMode =
  | NoPulse
  | PulseOnLow
  | AlwaysPulse

type ResourceBar() =
  inherit Widget()

  // Internal animation state
  let mutable visualValue = 0.0f
  let mutable lastTime = TimeSpan.Zero
  let mutable pulse = Pulse.create()

  // Bindable properties
  member val CurrentValue = 0.0f with get, set
  member val MaxValue = 100.0f with get, set
  member val ColorFill = Color.Red with get, set
  member val ColorLow = Color.Red with get, set
  member val ColorBackground = Color.Black with get, set
  member val LowThreshold = 0.25f with get, set
  member val PulseSpeed = 2.0f with get, set
  member val SmoothSpeed = 0.15f with get, set
  member val PulseMode = PulseOnLow with get, set

  member val WorldTime: Time =
    {
      Delta = TimeSpan.Zero
      TotalGameTime = TimeSpan.Zero
      Previous = TimeSpan.Zero
    } with get, set


  override this.InternalRender(context) =
    let bounds = this.ActualBounds
    let now = this.WorldTime.TotalGameTime

    let dt =
      if lastTime = TimeSpan.Zero then
        0.016f
      else
        float32 (now - lastTime).TotalSeconds |> min 0.1f

    lastTime <- now

    // Smooth animation toward target
    visualValue <-
      Lerp.smoothDamp visualValue this.CurrentValue this.SmoothSpeed dt

    // Background
    context.FillRectangle(bounds, this.ColorBackground)

    // Fill bar
    let fillPct = MathHelper.Clamp(visualValue / this.MaxValue, 0.0f, 1.0f)
    let fillWidth = int(float32 bounds.Width * fillPct)

    if fillWidth > 0 then
      let fillRect = Rectangle(bounds.X, bounds.Y, fillWidth, bounds.Height)
      let isLow = fillPct < this.LowThreshold

      let shouldPulse =
        match this.PulseMode with
        | NoPulse -> false
        | PulseOnLow -> isLow
        | AlwaysPulse -> true

      let finalColor =
        if shouldPulse then
          pulse <- Pulse.update this.PulseSpeed dt pulse
          Color.Lerp(this.ColorFill, this.ColorLow, pulse.Intensity)
        else
          this.ColorFill

      context.FillRectangle(fillRect, finalColor)


module ResourceBar =
  let create() = ResourceBar()

  let health() =
    ResourceBar(
      ColorFill = Color.Green,
      ColorLow = Color.Red,
      ColorBackground = Color.DarkGray
    )

  let mana() =
    ResourceBar(
      ColorFill = Color.Blue,
      ColorLow = Color.Blue,
      ColorBackground = Color.DarkGray
    )


type ActionSlot() =
  inherit Widget()

  // Properties
  member val CooldownEndTime = TimeSpan.Zero with get, set
  member val CooldownColor = Color(0, 0, 0, 200) with get, set
  member val BgColor = Color.DarkSlateGray with get, set

  member val WorldTime: Time =
    {
      Delta = TimeSpan.Zero
      TotalGameTime = TimeSpan.Zero
      Previous = TimeSpan.Zero
    } with get, set

  override this.InternalRender(context) =
    let bounds = this.ActualBounds
    let time = this.WorldTime.TotalGameTime

    // Background
    context.FillRectangle(bounds, this.BgColor)

    // Cooldown overlay (vertical sweep from bottom)
    if this.CooldownEndTime > time then
      let remaining = (this.CooldownEndTime - time).TotalSeconds
      // Assuming 10s default max for visual scaling for now, maybe expose this?
      let cdPct = MathHelper.Clamp(float32 remaining / 10.0f, 0.0f, 1.0f)
      let overlayHeight = int(float32 bounds.Height * cdPct)

      if overlayHeight > 0 then
        let overlayRect =
          Rectangle(
            bounds.X,
            bounds.Y + bounds.Height - overlayHeight,
            bounds.Width,
            overlayHeight
          )

        context.FillRectangle(overlayRect, this.CooldownColor)

    // Border
    let borderRect = Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height)
    context.DrawRectangle(borderRect, Color.Gray, 1.0f)


type StatusEffectWidget() =
  inherit Widget()

  let mutable lastTime = TimeSpan.Zero
  let mutable pulse = Pulse.create()

  // Properties
  member val CooldownEndTime = TimeSpan.Zero with get, set
  member val TotalDurationSeconds = 0.0f with get, set
  member val Kind = Pomo.Core.Domain.Skill.EffectKind.Buff with get, set
  member val ColorBuff = Color.Green with get, set
  member val ColorDebuff = Color.Red with get, set
  member val ColorDot = Color.Orange with get, set
  member val CooldownColor = Color(0, 0, 0, 200) with get, set

  member val WorldTime: Time =
    {
      Delta = TimeSpan.Zero
      TotalGameTime = TimeSpan.Zero
      Previous = TimeSpan.Zero
    } with get, set

  override this.InternalRender(context) =
    let bounds = this.ActualBounds
    let now = this.WorldTime.TotalGameTime

    let dt =
      if lastTime = TimeSpan.Zero then
        0.016f
      else
        float32 (now - lastTime).TotalSeconds |> min 0.1f

    lastTime <- now

    // Background
    context.FillRectangle(bounds, Color(20, 20, 20, 200))

    // Duration sweep
    if this.CooldownEndTime > now && this.TotalDurationSeconds > 0.0f then
      let remaining = (this.CooldownEndTime - now).TotalSeconds

      let pct =
        MathHelper.Clamp(
          float32 remaining / this.TotalDurationSeconds,
          0.0f,
          1.0f
        )

      let overlayHeight = int(float32 bounds.Height * pct)

      if overlayHeight > 0 then
        let overlayRect =
          Rectangle(
            bounds.X,
            bounds.Y + bounds.Height - overlayHeight,
            bounds.Width,
            overlayHeight
          )

        context.FillRectangle(overlayRect, this.CooldownColor)

    // Kind-based border
    let baseBorderColor =
      match this.Kind with
      | Pomo.Core.Domain.Skill.EffectKind.Buff -> this.ColorBuff
      | Pomo.Core.Domain.Skill.EffectKind.Debuff
      | Pomo.Core.Domain.Skill.EffectKind.Stun
      | Pomo.Core.Domain.Skill.EffectKind.Silence
      | Pomo.Core.Domain.Skill.EffectKind.Taunt -> this.ColorDebuff
      | Pomo.Core.Domain.Skill.EffectKind.DamageOverTime ->
        pulse <- Pulse.update 3.0f dt pulse
        Color.Lerp(this.ColorDot, Color.Yellow, pulse.Intensity)
      | _ -> Color.Gray

    // Flash when < 5s remaining (distinct from DoT pulse)
    let isExpiringSoon =
      this.CooldownEndTime > now
      && (this.CooldownEndTime - now).TotalSeconds < 5.0

    let borderColor =
      if isExpiringSoon then
        let flashT = float32(now.TotalSeconds * 6.0) // 6Hz flash for more urgency
        let flashIntensity = (sin flashT |> abs)
        Color.Lerp(baseBorderColor, Color.White, flashIntensity * 0.7f)
      else
        baseBorderColor

    context.DrawRectangle(bounds, borderColor, 2.0f)


module StatusEffect =
  let create() = StatusEffectWidget()


module ActionSlot =
  let create() = ActionSlot()

namespace Pomo.Core.UI

open System
open Microsoft.Xna.Framework
open Myra.Graphics2D
open Myra.Graphics2D.UI
open FSharp.Data.Adaptive
open Pomo.Core.Domain.World
open Pomo.Core.UI.HUDAnimation

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

      let finalColor =
        if isLow then
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
  member val CooldownColor = Color(0, 0, 0, 160) with get, set
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


module ActionSlot =
  let create() = ActionSlot()

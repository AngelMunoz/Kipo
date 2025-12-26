namespace Pomo.Core.UI

open System
open Microsoft.Xna.Framework
open Myra.Graphics2D
open Myra.Graphics2D.UI
open System.Collections.Generic
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.Domain.World
open Pomo.Core.UI.HUDAnimation
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Entity


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

  // Make this widget click-through (display only)
  override _.HitTest(_: Point) = null


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
    let fillPct =
      if this.MaxValue > 0.0f then
        MathHelper.Clamp(visualValue / this.MaxValue, 0.0f, 1.0f)
      else
        0.0f

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


type ActionSlot() =
  inherit Widget()

  // Properties
  member val CooldownEndTime = TimeSpan.Zero with get, set
  member val CooldownDuration = 10.0f with get, set
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

      let cdPct =
        MathHelper.Clamp(float32 remaining / this.CooldownDuration, 0.0f, 1.0f)

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


/// Widget for item slots - evaluates GetUsesLeft thunk on each render for live count
type ItemSlot() =
  inherit Widget()

  member val CooldownEndTime = TimeSpan.Zero with get, set
  member val CooldownDuration = 10.0f with get, set
  member val CooldownColor = Color(0, 0, 0, 200) with get, set
  member val BgColor = Color.DarkSlateGray with get, set

  /// Reference to count label - updated each render with thunk result
  member val CountLabel: Label = null with get, set

  member val GetUsesLeft: unit -> int voption =
    (fun () -> ValueNone) with get, set

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

    // Cooldown overlay
    if this.CooldownEndTime > time then
      let remaining = (this.CooldownEndTime - time).TotalSeconds

      let cdPct =
        MathHelper.Clamp(float32 remaining / this.CooldownDuration, 0.0f, 1.0f)

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
    context.DrawRectangle(bounds, Color.Gray, 1.0f)

    // Evaluate thunk and update count label directly (live update each render)
    if not(isNull this.CountLabel) then
      let newText =
        match this.GetUsesLeft() with
        | ValueSome count -> string count
        | ValueNone -> ""

      if this.CountLabel.Text <> newText then
        this.CountLabel.Text <- newText


module ItemSlot =
  let create() = ItemSlot()


type CombatIndicator() =
  inherit Widget()

  let mutable lastTime = TimeSpan.Zero
  let mutable visualAlpha = 0.0f

  member val IsInCombat = false with get, set
  member val Color = Color.Red with get, set
  member val FadeSpeed = 0.2f with get, set

  member val WorldTime: Time =
    {
      Delta = TimeSpan.Zero
      TotalGameTime = TimeSpan.Zero
      Previous = TimeSpan.Zero
    } with get, set

  // Make this widget click-through by never accepting hits
  override _.HitTest(_: Point) = null

  override this.InternalRender(context) =
    let bounds = this.ActualBounds
    let now = this.WorldTime.TotalGameTime

    let dt =
      if lastTime = TimeSpan.Zero then
        0.016f
      else
        float32 (now - lastTime).TotalSeconds |> min 0.1f

    lastTime <- now

    let targetAlpha = if this.IsInCombat then 1.0f else 0.0f
    visualAlpha <- Lerp.smoothDamp visualAlpha targetAlpha this.FadeSpeed dt

    if visualAlpha > 0.01f then
      let color = this.Color * (visualAlpha * 0.4f)
      let thickness = 10

      // Draw edge glow (simulated with 4 rectangles)
      // Top
      context.FillRectangle(
        Rectangle(bounds.X, bounds.Y, bounds.Width, thickness),
        color
      )

      // Bottom
      context.FillRectangle(
        Rectangle(
          bounds.X,
          bounds.Y + bounds.Height - thickness,
          bounds.Width,
          thickness
        ),
        color
      )

      // Left
      context.FillRectangle(
        Rectangle(bounds.X, bounds.Y, thickness, bounds.Height),
        color
      )

      // Right
      context.FillRectangle(
        Rectangle(
          bounds.X + bounds.Width - thickness,
          bounds.Y,
          thickness,
          bounds.Height
        ),
        color
      )


module CombatIndicator =
  let create() = CombatIndicator()


type MiniMap() =
  inherit Widget()

  // Properties
  member val Map: MapDefinition option = None with get, set
  member val PlayerId: Guid<EntityId> = Guid.Empty |> UMX.tag with get, set

  member val Positions: IReadOnlyDictionary<Guid<EntityId>, Vector2> =
    Dictionary() with get, set

  member val Factions: HashMap<Guid<EntityId>, HashSet<Faction>> =
    HashMap.empty with get, set

  member val Zoom = 0.05f with get, set

  /// View bounds for frustum culling (left, right, top, bottom) in world coords
  /// When set, only entities within these bounds (plus margin) will be rendered
  member val ViewBounds: struct (float32 * float32 * float32 * float32) voption =
    ValueNone with get, set

  /// Margin to expand view bounds (fraction, e.g. 0.3 = 30% expansion)
  member val CullMargin = 0.5f with get, set

  override this.InternalRender(context) =
    let bounds = this.ActualBounds
    context.FillRectangle(bounds, Color(0, 0, 0, 180))

    match this.Map with
    | Some _ ->
      let playerPos =
        match this.Positions |> Dictionary.tryFindV this.PlayerId with
        | ValueSome pos -> pos
        | ValueNone -> Vector2.Zero

      let mapCenter =
        Vector2(
          float32 bounds.X + float32 bounds.Width / 2.0f,
          float32 bounds.Y + float32 bounds.Height / 2.0f
        )

      // Pre-filter with frustum culling if bounds are available
      let inline isInViewBounds(pos: Vector2) =
        match this.ViewBounds with
        | ValueSome struct (left, right, top, bottom) ->
          let marginX = (right - left) * this.CullMargin
          let marginY = (bottom - top) * this.CullMargin

          pos.X >= left - marginX
          && pos.X <= right + marginX
          && pos.Y >= top - marginY
          && pos.Y <= bottom + marginY
        | ValueNone -> true

      for KeyValue(entityId, worldPos) in this.Positions do
        // Skip entities outside view bounds
        if isInViewBounds worldPos then
          let relativePos = (worldPos - playerPos) * this.Zoom
          let drawPos = mapCenter + relativePos

          if bounds.Contains(int drawPos.X, int drawPos.Y) then
            let color =
              if entityId = this.PlayerId then
                Color.Green
              else
                match this.Factions.TryFindV entityId with
                | ValueSome f ->
                  if HashSet.contains Enemy f then Color.Red
                  elif HashSet.contains Ally f then Color.Blue
                  else Color.Gray
                | ValueNone -> Color.Gray

            let size = if entityId = this.PlayerId then 4 else 3

            context.FillRectangle(
              Rectangle(
                int drawPos.X - size / 2,
                int drawPos.Y - size / 2,
                size,
                size
              ),
              color
            )
    | None -> ()


module MiniMap =
  let create() = MiniMap()


type EquipmentSlot() =
  inherit Widget()

  member val BgColor = Color.DarkSlateGray with get, set

  override this.InternalRender(context) =
    let bounds = this.ActualBounds
    context.FillRectangle(bounds, this.BgColor)
    context.DrawRectangle(bounds, Color.Gray, 1.0f)


module EquipmentSlot =
  let create() = EquipmentSlot()

namespace Pomo.Core.UI

open System
open System.Runtime.CompilerServices
open System.Reactive.Disposables
open Microsoft.Xna.Framework
open Myra.Graphics2D
open Myra.Graphics2D.UI
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Core


module WidgetSubs =
  let private store = ConditionalWeakTable<Widget, CompositeDisposable>()

  let get(w: Widget) =
    match store.TryGetValue(w) with
    | true, cd -> cd
    | false, _ ->
      let cd = new CompositeDisposable()
      store.Add(w, cd)
      cd

  let dispose(w: Widget) =
    match store.TryGetValue(w) with
    | true, cd ->
      cd.Dispose()
      store.Remove(w) |> ignore
    | false, _ -> ()

module W =
  open System.Collections.Generic
  open Pomo.Core.Domain.Units
  open Pomo.Core.Domain.Entity
  open Myra.Graphics2D.Brushes

  let inline background<'T when 'T: (member set_Background: Color -> unit)>
    (value: Color)
    (w: 'T)
    =
    w.set_Background(value)
    w

  let inline width<'T when 'T: (member set_Width: Nullable<int> -> unit)>
    (value: int)
    (w: 'T)
    =
    w.set_Width(Nullable value)
    w

  let inline height<'T when 'T: (member set_Height: Nullable<int> -> unit)>
    (value: int)
    (w: 'T)
    =
    w.set_Height(Nullable value)
    w

  let inline size<'T
    when 'T: (member set_Width: Nullable<int> -> unit)
    and 'T: (member set_Height: Nullable<int> -> unit)>
    (width: int)
    (height: int)
    (w: 'T)
    : 'T =
    w.set_Width(Nullable width)
    w.set_Height(Nullable height)
    w

  let inline hAlign<'T
    when 'T: (member set_HorizontalAlignment: HorizontalAlignment -> unit)>
    (align: HorizontalAlignment)
    (w: 'T)
    : 'T =
    w.set_HorizontalAlignment align
    w

  let inline vAlign<'T
    when 'T: (member set_VerticalAlignment: VerticalAlignment -> unit)>
    (align: VerticalAlignment)
    (w: 'T)
    : 'T =
    w.set_VerticalAlignment align
    w

  let inline enabled<'T when 'T :> Widget> (value: bool) (w: 'T) : 'T =
    (w :> Widget).Enabled <- value
    w

  let inline margin<'T when 'T: (member set_Margin: Thickness -> unit)>
    (value: int)
    (w: 'T)
    : 'T =
    w.set_Margin(Thickness(value))
    w

  let inline margin4<'T when 'T: (member set_Margin: Thickness -> unit)>
    (left: int)
    (top: int)
    (right: int)
    (bottom: int)
    (w: 'T)
    : 'T =
    w.set_Margin(Thickness(left, top, right, bottom))
    w

  let inline padding<'T when 'T: (member set_Padding: Thickness -> unit)>
    (value: int)
    (w: 'T)
    : 'T =
    w.set_Padding(Thickness(value))
    w

  let inline left<'T when 'T: (member set_Left: int -> unit)>
    (value: int)
    (w: 'T)
    : 'T =
    w.set_Left(value)
    w

  let inline top<'T when 'T: (member set_Top: int -> unit)>
    (value: int)
    (w: 'T)
    : 'T =
    w.set_Top(value)
    w

  let inline id<'T when 'T: (member set_Id: string -> unit)>
    (value: string)
    (w: 'T)
    : 'T =
    w.set_Id(value)
    w

  let inline tag<'T when 'T: (member set_Tag: obj -> unit)>
    (value: obj)
    (w: 'T)
    =
    w.set_Tag(value)
    w

  let inline opacity<'T when 'T: (member set_Opacity: float32 -> unit)>
    (value: float32)
    (w: 'T)
    =
    w.set_Opacity(value)
    w

  let inline text<'T when 'T: (member set_Text: string -> unit)>
    (value: string)
    (w: 'T)
    =
    w.set_Text(value)
    w

  let inline textColor<'T when 'T: (member set_TextColor: Color -> unit)>
    (color: Color)
    (w: 'T)
    =
    w.set_TextColor(color)
    w

  let inline spacing<'T when 'T: (member set_Spacing: int -> unit)>
    (value: int)
    (w: 'T)
    =
    w.set_Spacing(value)
    w

  // Type-specific children helpers (loses concrete type - use at end of pipeline)
  let inline childrenP (widgets: Widget seq) (panel: Panel) =
    for widget in widgets do
      panel.Widgets.Add(widget)

    panel

  let inline childrenH (widgets: Widget seq) (panel: HorizontalStackPanel) =
    for widget in widgets do
      panel.Widgets.Add(widget)

    panel

  let inline childrenV (widgets: Widget seq) (panel: VerticalStackPanel) =
    for widget in widgets do
      panel.Widgets.Add(widget)

    panel

  let inline cooldownEndTime<'T
    when 'T: (member set_CooldownEndTime: TimeSpan -> unit)>
    (value: TimeSpan)
    (w: 'T)
    =
    w.set_CooldownEndTime(value)
    w

  let inline mapPositions<'T
    when 'T :> Widget
    and 'T: (member set_Positions:
      IReadOnlyDictionary<Guid<EntityId>, WorldPosition> -> unit)>
    (positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>)
    (w: 'T)
    =
    w.set_Positions(positions)
    w

  // Reactive bindings (subscriptions auto-stored via WidgetSubs)
  let inline bindText<'T
    when 'T :> Widget and 'T: (member set_Text: string -> unit)>
    (aval: aval<string>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_Text(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindTextColor<'T
    when 'T :> Widget and 'T: (member set_TextColor: Color -> unit)>
    (aval: aval<Color>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_TextColor(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindColor<'T
    when 'T :> Widget and 'T: (member set_Color: Color -> unit)>
    (aval: aval<Color>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_Color(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindOpacity<'T
    when 'T :> Widget and 'T: (member set_Opacity: float32 -> unit)>
    (aval: aval<float32>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_Opacity(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindCurrentValue<'T
    when 'T :> Widget and 'T: (member set_CurrentValue: float32 -> unit)>
    (aval: aval<float32>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_CurrentValue(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindMaxValue<'T
    when 'T :> Widget and 'T: (member set_MaxValue: float32 -> unit)>
    (aval: aval<float32>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_MaxValue(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindColorFill<'T
    when 'T :> Widget and 'T: (member set_ColorFill: Color -> unit)>
    (aval: aval<Color>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_ColorFill(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindColorBackground<'T
    when 'T :> Widget and 'T: (member set_ColorBackground: Color -> unit)>
    (aval: aval<Color>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_ColorBackground(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindBackgroundBrush<'T
    when 'T :> Widget and 'T: (member set_Background: IBrush -> unit)>
    (aval: aval<IBrush>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_Background(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindBackground<'T
    when 'T :> Widget and 'T: (member set_Background: IBrush -> unit)>
    (aval: aval<Color>)
    (w: 'T)
    =
    w |> bindBackgroundBrush(aval |> AVal.map(fun c -> SolidBrush c :> IBrush))

  let inline bindCooldownEndTime<'T
    when 'T :> Widget and 'T: (member set_CooldownEndTime: TimeSpan -> unit)>
    (aval: aval<TimeSpan>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_CooldownEndTime(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindCooldownColor<'T
    when 'T :> Widget and 'T: (member set_CooldownColor: Color -> unit)>
    (aval: aval<Color>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_CooldownColor(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindCooldownDuration<'T
    when 'T :> Widget and 'T: (member set_CooldownDuration: float32 -> unit)>
    (aval: aval<float32>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_CooldownDuration(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindBgColor<'T
    when 'T :> Widget and 'T: (member set_BgColor: Color -> unit)>
    (aval: aval<Color>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_BgColor(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindKind<'T
    when 'T :> Widget
    and 'T: (member set_Kind: Pomo.Core.Domain.Skill.EffectKind -> unit)>
    (aval: aval<Pomo.Core.Domain.Skill.EffectKind>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_Kind(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindTotalDurationSeconds<'T
    when 'T :> Widget and 'T: (member set_TotalDurationSeconds: float32 -> unit)>
    (aval: aval<float32>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_TotalDurationSeconds(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindWorldTime<'T
    when 'T :> Widget and 'T: (member set_WorldTime: Time -> unit)>
    (worldTime: Time aval)
    (w: 'T)
    =
    let sub = worldTime.AddWeakCallback(fun v -> w.set_WorldTime(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindColorBuff<'T
    when 'T :> Widget and 'T: (member set_ColorBuff: Color -> unit)>
    (aval: aval<Color>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_ColorBuff(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindColorDebuff<'T
    when 'T :> Widget and 'T: (member set_ColorDebuff: Color -> unit)>
    (aval: aval<Color>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_ColorDebuff(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindColorDot<'T
    when 'T :> Widget and 'T: (member set_ColorDot: Color -> unit)>
    (aval: aval<Color>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_ColorDot(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindIsInCombat<'T
    when 'T :> Widget and 'T: (member set_IsInCombat: bool -> unit)>
    (aval: aval<bool>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_IsInCombat(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindMap<'T
    when 'T :> Widget
    and 'T: (member set_Map: Pomo.Core.Domain.Map.MapDefinition option -> unit)>
    (aval: aval<Pomo.Core.Domain.Map.MapDefinition option>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_Map(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline playerId<'T
    when 'T :> Widget and 'T: (member set_PlayerId: Guid<EntityId> -> unit)>
    (value: Guid<EntityId>)
    (w: 'T)
    =
    w.set_PlayerId(value)
    w



  let inline bindMapFactions<'T
    when 'T :> Widget
    and 'T: (member set_Factions:
      HashMap<Guid<EntityId>, FSharp.Data.Adaptive.HashSet<Faction>> -> unit)>
    (aval: aval<HashMap<Guid<EntityId>, FSharp.Data.Adaptive.HashSet<Faction>>>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_Factions(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindViewBounds<'T
    when 'T :> Widget
    and 'T: (member set_ViewBounds:
      struct (float32 * float32 * float32 * float32) voption -> unit)>
    (aval: aval<struct (float32 * float32 * float32 * float32) voption>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_ViewBounds(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline totalDurationSeconds<'T
    when 'T: (member set_TotalDurationSeconds: float32 -> unit)>
    (value: float32)
    (w: 'T)
    =
    w.set_TotalDurationSeconds(value)
    w

  let inline effectKind<'T
    when 'T: (member set_Kind: Pomo.Core.Domain.Skill.EffectKind -> unit)>
    (value: Pomo.Core.Domain.Skill.EffectKind)
    (w: 'T)
    =
    w.set_Kind(value)
    w

  let inline isInCombat<'T when 'T: (member set_IsInCombat: bool -> unit)>
    (value: bool)
    (w: 'T)
    =
    w.set_IsInCombat(value)
    w

  let inline colorFill<'T when 'T: (member set_ColorFill: Color -> unit)>
    (value: Color)
    (w: 'T)
    =
    w.set_ColorFill(value)
    w

  let inline colorBackground<'T
    when 'T: (member set_ColorBackground: Color -> unit)>
    (value: Color)
    (w: 'T)
    =
    w.set_ColorBackground(value)
    w

  let inline maxValue<'T when 'T: (member set_MaxValue: float32 -> unit)>
    (value: float32)
    (w: 'T)
    =
    w.set_MaxValue(value)
    w

  let inline smoothSpeed<'T when 'T: (member set_SmoothSpeed: float32 -> unit)>
    (value: float32)
    (w: 'T)
    =
    w.set_SmoothSpeed(value)
    w

  let inline pulseMode<'T when 'T: (member set_PulseMode: PulseMode -> unit)>
    (mode: PulseMode)
    (w: 'T)
    =
    w.set_PulseMode(mode)
    w

  let inline getUsesLeft<'T
    when 'T: (member set_GetUsesLeft: (unit -> int voption) -> unit)>
    (thunk: unit -> int voption)
    (w: 'T)
    =
    w.set_GetUsesLeft(thunk)
    w

  let inline bindGetUsesLeft<'T
    when 'T :> Widget
    and 'T: (member set_GetUsesLeft: (unit -> int voption) -> unit)>
    (thunkAVal: aval<unit -> int voption>)
    (w: 'T)
    =
    let sub = thunkAVal.AddWeakCallback(fun v -> w.set_GetUsesLeft(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline countLabel<'T when 'T: (member set_CountLabel: Label -> unit)>
    (label: Label)
    (w: 'T)
    =
    w.set_CountLabel(label)
    w

  let inline tooltip<'T when 'T :> Widget> (text: string) (w: 'T) =
    w.Tooltip <- text
    w

  let inline bindTooltip<'T
    when 'T :> Widget and 'T: (member set_Tooltip: string -> unit)>
    (textAVal: aval<string>)
    (w: 'T)
    =
    let sub = textAVal.AddWeakCallback(fun v -> w.set_Tooltip(v))
    WidgetSubs.get(w).Add(sub)
    w

module Label =
  let inline create(text: string) = Label(Text = text)

  let inline colored (text: string) (color: Color) =
    Label(Text = text, TextColor = color)


module Panel =
  open Myra.Graphics2D.Brushes
  let inline create() = Panel()

  let inline sized (width: int) (height: int) =
    Panel(Width = Nullable width, Height = Nullable height)

  let inline bindChildren<'W when 'W :> Panel>
    (childrenAVal: aval<Widget list>)
    (w: 'W)
    =
    let sub =
      childrenAVal.AddWeakCallback(fun children ->
        w.Widgets.Clear()

        for child in children do
          w.Widgets.Add(child))

    WidgetSubs.get(w).Add(sub)
    w

  let inline bindBackgroundBrush<'T
    when 'T :> Panel and 'T: (member set_Background: IBrush -> unit)>
    (aval: aval<IBrush>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_Background(v))
    WidgetSubs.get(w).Add(sub)
    w

  let inline bindBackground<'T
    when 'T :> Panel and 'T: (member set_Background: IBrush -> unit)>
    (aval: aval<Color>)
    (w: 'T)
    =
    w |> bindBackgroundBrush(aval |> AVal.map(fun c -> SolidBrush c :> IBrush))

module HStack =
  let inline create() = HorizontalStackPanel()
  let inline spaced(spacing: int) = HorizontalStackPanel(Spacing = spacing)

  let inline bindIndexListChildren<'T, 'W
    when 'T :> Widget and 'W :> HorizontalStackPanel>
    (childrenAVal: aval<'T IndexList>)
    (w: 'W)
    =
    let sub =
      childrenAVal.AddWeakCallback(fun (children: 'T IndexList) ->
        w.Widgets.Clear()

        for child in children do
          w.Widgets.Add(child))

    WidgetSubs.get(w).Add(sub)
    w

  let inline bindChildren<'W when 'W :> HorizontalStackPanel>
    (childrenAVal: aval<Widget list>)
    (w: 'W)
    =
    let sub =
      childrenAVal.AddWeakCallback(fun children ->
        w.Widgets.Clear()

        for child in children do
          w.Widgets.Add(child))

    WidgetSubs.get(w).Add(sub)
    w

module VStack =
  let inline create() = VerticalStackPanel()
  let inline spaced(spacing: int) = VerticalStackPanel(Spacing = spacing)

  let inline bindChildren<'W when 'W :> VerticalStackPanel>
    (childrenAVal: aval<Widget list>)
    (w: 'W)
    =
    let sub =
      childrenAVal.AddWeakCallback(fun children ->
        w.Widgets.Clear()

        for child in children do
          w.Widgets.Add(child))

    WidgetSubs.get(w).Add(sub)
    w


module Grid =
  let inline create() = Grid()

  let inline spaced (columnSpacing: int) (rowSpacing: int) =
    Grid(ColumnSpacing = columnSpacing, RowSpacing = rowSpacing)

  let inline columns (proportions: Proportion list) (grid: Grid) =
    for p in proportions do
      grid.ColumnsProportions.Add(p)

    grid

  let inline rows (proportions: Proportion list) (grid: Grid) =
    for p in proportions do
      grid.RowsProportions.Add(p)

    grid

  let inline autoColumns (count: int) (grid: Grid) =
    for _ in 1..count do
      grid.ColumnsProportions.Add(Proportion(ProportionType.Auto))

    grid

  let inline autoRows (count: int) (grid: Grid) =
    for _ in 1..count do
      grid.RowsProportions.Add(Proportion(ProportionType.Auto))

    grid


module Btn =
  let inline create(text: string) = Button(Content = Label(Text = text))

  let inline onClick (handler: unit -> unit) (btn: Button) =
    let sub = btn.Click.Subscribe(fun _ -> handler())
    WidgetSubs.get(btn).Add(sub)
    btn

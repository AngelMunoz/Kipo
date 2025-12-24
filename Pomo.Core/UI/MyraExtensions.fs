namespace Pomo.Core.UI

open System
open System.Runtime.CompilerServices
open System.Reactive.Disposables
open Microsoft.Xna.Framework
open Myra.Graphics2D
open Myra.Graphics2D.UI
open FSharp.Data.Adaptive
open Pomo.Core.Domain.World


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
    : 'T when 'T: (member set_HorizontalAlignment: HorizontalAlignment -> unit) =
    w.set_HorizontalAlignment align
    w

  let inline vAlign<'T
    when 'T: (member set_VerticalAlignment: VerticalAlignment -> unit)>
    (align: VerticalAlignment)
    (w: 'T)
    : 'T when 'T: (member set_VerticalAlignment: VerticalAlignment -> unit) =
    w.set_VerticalAlignment align
    w

  let inline margin<'T when 'T: (member set_Margin: Thickness -> unit)>
    (value: int)
    (w: 'T)
    : 'T when 'T: (member set_Margin: Thickness -> unit) =
    w.set_Margin(Thickness(value))
    w

  let inline margin4<'T when 'T: (member set_Margin: Thickness -> unit)>
    (left: int)
    (top: int)
    (right: int)
    (bottom: int)
    (w: 'T)
    : 'T when 'T: (member set_Margin: Thickness -> unit) =
    w.set_Margin(Thickness(left, top, right, bottom))
    w

  let inline padding<'T when 'T: (member set_Padding: Thickness -> unit)>
    (value: int)
    (w: 'T)
    : 'T when 'T: (member set_Padding: Thickness -> unit) =
    w.set_Padding(Thickness(value))
    w

  let inline left<'T when 'T: (member set_Left: int -> unit)>
    (value: int)
    (w: 'T)
    : 'T when 'T: (member set_Left: int -> unit) =
    w.set_Left(value)
    w

  let inline top<'T when 'T: (member set_Top: int -> unit)>
    (value: int)
    (w: 'T)
    : 'T when 'T: (member set_Top: int -> unit) =
    w.set_Top(value)
    w

  let inline id<'T when 'T: (member set_Id: string -> unit)>
    (value: string)
    (w: 'T)
    : 'T when 'T: (member set_Id: string -> unit) =
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

  let inline bindBgColor<'T
    when 'T :> Widget and 'T: (member set_BgColor: Color -> unit)>
    (aval: aval<Color>)
    (w: 'T)
    =
    let sub = aval.AddWeakCallback(fun v -> w.set_BgColor(v))
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


module Label =
  let inline create(text: string) = Label(Text = text)

  let inline colored (text: string) (color: Color) =
    Label(Text = text, TextColor = color)


module Panel =
  let inline create() = Panel()

  let inline sized (width: int) (height: int) =
    Panel(Width = Nullable width, Height = Nullable height)


module HStack =
  let inline create() = HorizontalStackPanel()
  let inline spaced(spacing: int) = HorizontalStackPanel(Spacing = spacing)


module VStack =
  let inline create() = VerticalStackPanel()
  let inline spaced(spacing: int) = VerticalStackPanel(Spacing = spacing)


module Btn =
  let inline create(text: string) = Button(Content = Label(Text = text))

  let inline onClick (handler: unit -> unit) (btn: Button) =
    btn.Click.Add(fun _ -> handler())
    btn

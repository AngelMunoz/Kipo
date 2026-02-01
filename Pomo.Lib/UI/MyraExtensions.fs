namespace Pomo.Lib.UI

open System
open System.Runtime.CompilerServices
open Microsoft.Xna.Framework
open Myra.Graphics2D
open Myra.Graphics2D.UI



module WidgetSubs =

  type Disposables() =

    let disposables = ResizeArray<IDisposable>()

    member this.Add(disposable: IDisposable) = disposables.Add(disposable)

    interface IDisposable with
      member this.Dispose() =
        for d in disposables do
          d.Dispose()

  let private store = ConditionalWeakTable<Widget, Disposables>()

  let get(w: Widget) =
    match store.TryGetValue(w) with
    | true, cd -> cd
    | false, _ ->
      let cd = new Disposables()
      store.Add(w, cd)
      cd

  let dispose(w: Widget) =
    match store.TryGetValue(w) with
    | true, cd ->
      (cd :> IDisposable).Dispose()
      store.Remove(w) |> ignore
    | false, _ -> ()

module W =
  open System.Collections.Generic
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

  let inline opacity<'T when 'T: (member set_Opacity: float32 -> unit)>
    (value: float32)
    (w: 'T)
    =
    w.set_Opacity(value)
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
    =
    w.set_Padding(Thickness(value))
    w

  let inline left<'T when 'T: (member set_Left: int -> unit)>
    (value: int)
    (w: 'T)
    =
    w.set_Left(value)
    w

  let inline top<'T when 'T: (member set_Top: int -> unit)>
    (value: int)
    (w: 'T)
    =
    w.set_Top(value)
    w

  let inline id<'T when 'T: (member set_Id: string -> unit)>
    (value: string)
    (w: 'T)
    =
    w.set_Id(value)
    w

  let inline tag<'T when 'T: (member set_Tag: obj -> unit)>
    (value: obj)
    (w: 'T)
    =
    w.set_Tag(value)
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

module Label =
  let inline create(text: string) = Label(Text = text)

  let inline colored (text: string) (color: Color) =
    Label(Text = text, TextColor = color)

module Panel =
  open Myra.Graphics2D.Brushes

  let inline create() = Panel()

  let inline sized (width: int) (height: int) =
    Panel(Width = Nullable width, Height = Nullable height)

module HStack =
  let inline create() = HorizontalStackPanel()

  let inline spaced(spacing: int) = HorizontalStackPanel(Spacing = spacing)

module VStack =
  let inline create() = VerticalStackPanel()

  let inline spaced(spacing: int) = VerticalStackPanel(Spacing = spacing)

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

  let inline empty() = Button()

  let inline content (widget: Widget) (btn: Button) =
    btn.Content <- widget
    btn

  let inline onClick (handler: unit -> unit) (btn: Button) =
    let sub = btn.Click.Subscribe(fun _ -> handler())
    WidgetSubs.get(btn).Add(sub)
    btn

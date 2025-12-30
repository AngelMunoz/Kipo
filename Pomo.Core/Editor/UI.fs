namespace Pomo.Core.Editor

open System
open Microsoft.Xna.Framework
open Myra.Graphics2D.UI
open FSharp.Data.Adaptive
open Pomo.Core.UI
open Pomo.Core.Domain
open Pomo.Core.Domain.Units

open Myra.Graphics2D.Brushes

module EditorUI =

  let build(state: EditorState) =
    let panel = Panel.create()

    let container =
      VStack.spaced 8
      |> W.hAlign HorizontalAlignment.Left
      |> W.vAlign VerticalAlignment.Bottom
      |> W.padding 20

    let title = Label.create "MAP EDITOR" |> W.textColor Color.Yellow

    let undoBtn =
      Btn.create "Undo (Ctrl+Z)"
      |> W.size 120 30
      |> Btn.onClick(fun () -> EditorState.undo state)

    let redoBtn =
      Btn.create "Redo (Ctrl+Y)"
      |> W.size 120 30
      |> Btn.onClick(fun () -> EditorState.redo state)

    let blockCount =
      Label.create "Blocks: 0"
      |> W.bindText(
        state.BlockMap |> AVal.map(fun m -> $"Blocks: {m.Blocks.Count}")
      )

    let blockInfo =
      Label.create "Block: None"
      |> W.bindText(
        (state.BlockMap, state.SelectedBlockType)
        ||> AVal.map2(fun map selectedId ->
          match selectedId with
          | ValueSome id ->
            match map.Palette.TryGetValue(id) with
            | true, bt -> $"Block: {bt.Name}"
            | _ -> "Block: Unknown"
          | ValueNone -> "Block: None")
      )

    let brushInfo =
      Label.create "Brush: Place"
      |> W.bindText(state.BrushMode |> AVal.map(fun m -> $"Brush: %A{m}"))

    let layerInfo =
      Label.create "Layer: 0"
      |> W.bindText(state.CurrentLayer |> AVal.map(fun l -> $"Layer: {l}"))

    let cameraInfo =
      Label.create "Camera: Isometric"
      |> W.bindText(state.CameraMode |> AVal.map(fun m -> $"Camera: %A{m}"))

    let palette =
      HStack.spaced 4
      |> W.hAlign HorizontalAlignment.Right
      |> W.vAlign VerticalAlignment.Bottom
      |> W.padding 20

    palette.Background <- SolidBrush(Color(0, 0, 0, 150))

    let paletteChildren =
      state.BlockMap
      |> AVal.map(fun map -> [
        for bt in map.Palette.Values do
          let btn =
            Btn.create bt.Name
            |> W.size 80 30
            |> Btn.onClick(fun () ->
              transact(fun () ->
                state.SelectedBlockType.Value <- ValueSome bt.Id))

          // Highlight selected
          let sub =
            state.SelectedBlockType.AddWeakCallback(fun selId ->
              let label = btn.Content :?> Label

              if selId = ValueSome bt.Id then
                label.TextColor <- Color.Yellow
              else
                label.TextColor <- Color.White)

          WidgetSubs.get(btn).Add(sub)

          btn :> Widget
      ])

    palette |> HStack.bindChildren paletteChildren |> ignore

    container
    |> W.childrenV [
      title
      undoBtn
      redoBtn
      blockInfo
      blockCount
      brushInfo
      layerInfo
      cameraInfo
    ]
    |> ignore

    panel.Widgets.Add(container)
    panel.Widgets.Add(palette)
    panel

  let createSystem
    (game: Game)
    (state: EditorState)
    (uiService: Pomo.Core.Environment.IUIService)
    : DrawableGameComponent =

    let mutable desktop: Desktop voption = ValueNone

    { new DrawableGameComponent(game, DrawOrder = 1000) with
        override _.LoadContent() =
          let root = build state
          desktop <- ValueSome(new Desktop(Root = root))

        override _.Update _ =
          desktop
          |> ValueOption.iter(fun d ->
            uiService.SetMouseOverUI d.IsMouseOverGUI)

        override _.Draw _ =
          desktop |> ValueOption.iter(fun d -> d.Render())
    }

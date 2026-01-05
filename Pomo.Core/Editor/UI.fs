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

  let build (state: EditorState) (camPos: aval<Vector3>) =
    let panel = Panel.create()

    let container =
      VStack.spaced 8
      |> W.hAlign HorizontalAlignment.Left
      |> W.vAlign VerticalAlignment.Bottom
      |> W.padding 20

    let title = Label.create "MAP EDITOR" |> W.textColor Color.Yellow
    let helpLabel = Label.create "Presss F1 for help" |> W.size 120 12

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

    let effectInfo =
      Label.create "Effect: None"
      |> W.bindText(
        (state.BlockMap, state.SelectedBlockType)
        ||> AVal.map2(fun map selectedId ->
          match selectedId with
          | ValueSome id ->
            match map.Palette.TryGetValue(id) with
            | true, bt ->
              match bt.Effect with
              | ValueSome e -> $"Effect: {e.Name}"
              | ValueNone -> "Effect: None"
            | _ -> "Effect: Unknown"
          | ValueNone -> "Effect: None")
      )

    let brushInfo =
      Label.create "Brush: Place"
      |> W.bindText(state.BrushMode |> AVal.map(fun m -> $"Brush: %A{m}"))

    let collisionLabel =
      Label.create ""
      |> W.bindText(
        state.CollisionEnabled
        |> AVal.map(fun enabled ->
          if enabled then "Collision: On" else "Collision: Off")
      )

    let collisionBtn =
      Btn.empty()
      |> Btn.content collisionLabel
      |> W.size 120 30
      |> Btn.onClick(fun () ->
        transact(fun () ->
          state.CollisionEnabled.Value <- not state.CollisionEnabled.Value))

    let effectNoneBtn =
      Btn.create "Effect: None"
      |> W.size 120 30
      |> Btn.onClick(fun () ->
        transact(fun () ->
          match state.SelectedBlockType.Value with
          | ValueSome archetypeId ->
            let map = state.BlockMap.Value

            let newId =
              Pomo.Core.Algorithms.BlockMap.getOrCreateEffectVariant
                map
                archetypeId
                ValueNone

            match newId with
            | ValueSome id -> state.SelectedBlockType.Value <- ValueSome id
            | ValueNone -> ()
          | ValueNone -> ()))

    let effectLavaBtn =
      Btn.create "Effect: Lava"
      |> W.size 120 30
      |> Btn.onClick(fun () ->
        transact(fun () ->
          match state.SelectedBlockType.Value with
          | ValueSome archetypeId ->
            let map = state.BlockMap.Value

            let newId =
              Pomo.Core.Algorithms.BlockMap.getOrCreateEffectVariant
                map
                archetypeId
                (ValueSome EditorEffectPresets.lava)

            match newId with
            | ValueSome id -> state.SelectedBlockType.Value <- ValueSome id
            | ValueNone -> ()

            // Force map update to refresh palette UI if needed
            state.BlockMap.Value <- { map with Version = map.Version + 1 }
          | ValueNone -> ()))

    let layerInfo =
      Label.create "Layer: 0"
      |> W.bindText(state.CurrentLayer |> AVal.map(fun l -> $"Layer: {l}"))

    let cameraInfo =
      Label.create "Camera: Isometric"
      |> W.bindText(state.CameraMode |> AVal.map(fun m -> $"Camera: %A{m}"))

    let cursorInfo =
      Label.create "Cursor: None"
      |> W.bindText(
        state.GridCursor
        |> AVal.map(fun c ->
          match c with
          | ValueSome cell -> $"Cursor: {cell.X}, {cell.Y}, {cell.Z}"
          | ValueNone -> "Cursor: None")
      )

    let cameraPosInfo =
      Label.create "Pos: 0, 0, 0"
      |> W.bindText(
        camPos |> AVal.map(fun p -> $"Pos: {p.X:F1}, {p.Y:F1}, {p.Z:F1}")
      )

    let palette =
      Panel.sized 420 240
      |> W.hAlign HorizontalAlignment.Right
      |> W.vAlign VerticalAlignment.Bottom
      |> W.padding 12

    palette.Background <- SolidBrush(Color(0, 0, 0, 150))

    let paletteWidget =
      state.BlockMap
      |> AVal.map(fun map ->
        let archetypes =
          map.Palette.Values
          |> Seq.filter(fun bt -> bt.Id = bt.ArchetypeId)
          |> Seq.sortBy(fun bt -> struct (bt.Category, bt.Name))
          |> Seq.toArray

        let columns = 3
        let rows = (archetypes.Length + columns - 1) / columns

        let grid =
          Grid.spaced 4 4 |> Grid.autoColumns columns |> Grid.autoRows rows

        for i = 0 to archetypes.Length - 1 do
          let bt = archetypes[i]

          let btn =
            Btn.create bt.Name
            |> W.size 120 30
            |> Btn.onClick(fun () ->
              transact(fun () ->
                state.SelectedBlockType.Value <- ValueSome bt.Id))

          let sub =
            state.SelectedBlockType.AddWeakCallback(fun selId ->
              let label = btn.Content :?> Label

              if selId = ValueSome bt.Id then
                label.TextColor <- Color.Yellow
              else
                label.TextColor <- Color.White)

          WidgetSubs.get(btn).Add(sub)

          Grid.SetColumn(btn, i % columns)
          Grid.SetRow(btn, i / columns)
          grid.Widgets.Add(btn)

        let scroll = ScrollViewer(Content = grid)
        scroll :> Widget)

    palette
    |> Panel.bindChildren(paletteWidget |> AVal.map List.singleton)
    |> ignore

    // Help Overlay
    let controls = [
      "Tab", "Toggle Camera Mode"
      "WASD", "Move Camera"
      "Scroll", "Zoom / Elevate"
      "R-Click", "Cam Rotate / Erase (Iso)"
      "M-Click", "Reset Camera"
      "L-Click", "Place Block"
      "1 / 2", "Brush Mode"
      "R", "Reset Rotation"
      "Q / E", "Rotate (Shift/Alt axis)"
      "PgUp/Dn", "Change Layer"
      "Ctrl+Z/Y", "Undo / Redo"
    ]

    let helpRows =
      controls
      |> List.map(fun (key, action) ->
        HStack.spaced 10
        |> W.childrenH [
          Label.create key
          |> W.textColor Color.Yellow
          |> W.width 100
          |> W.hAlign HorizontalAlignment.Right

          Label.create action
          |> W.textColor Color.White
          |> W.hAlign HorizontalAlignment.Left
        ]
        :> Widget)

    let helpList =
      VStack.spaced 5
      |> W.childrenV(
        (Label.create "--- EDITOR CONTROLS (F1) ---"
         |> W.hAlign HorizontalAlignment.Center
         |> W.textColor Color.Cyan
        :> Widget)
        :: helpRows
      )

    let helpContainer =
      Panel.create()
      |> W.hAlign HorizontalAlignment.Center
      |> W.vAlign VerticalAlignment.Center
      |> W.padding 20

    helpContainer.Background <- SolidBrush(Color(0, 0, 0, 220))
    helpContainer.Widgets.Add(helpList)

    // Bind visibility
    let helpSub =
      state.ShowHelp.AddCallback(fun show -> helpContainer.Visible <- show)

    WidgetSubs.get(helpContainer).Add(helpSub)

    container
    |> W.childrenV [
      title
      helpLabel
      undoBtn
      redoBtn
      collisionBtn
      blockInfo
      effectInfo
      effectNoneBtn
      effectLavaBtn
      blockCount
      brushInfo
      layerInfo
      cameraInfo
      cursorInfo
      cameraPosInfo
    ]
    |> ignore

    panel.Widgets.Add(container)
    panel.Widgets.Add(palette)
    panel.Widgets.Add(helpContainer)
    panel

  let createSystem
    (game: Game)
    (state: EditorState)
    (uiService: Pomo.Core.Environment.IUIService)
    (cam: MutableCamera)
    : DrawableGameComponent =

    let mutable desktop: Desktop voption = ValueNone
    let camPos = cval cam.Params.Position

    { new DrawableGameComponent(game, DrawOrder = 1000) with
        override _.LoadContent() =
          let root = build state camPos
          desktop <- ValueSome(new Desktop(Root = root))

        override _.Update _ =
          transact(fun () ->
            if camPos.Value <> cam.Params.Position then
              camPos.Value <- cam.Params.Position)

          desktop
          |> ValueOption.iter(fun d ->
            uiService.SetMouseOverUI d.IsMouseOverGUI)

        override _.Draw _ =
          desktop |> ValueOption.iter(fun d -> d.Render())
    }

namespace Pomo.Lib.UI

open Microsoft.Xna.Framework
open Myra.Graphics2D.UI
open Myra.Graphics2D.Brushes
open Pomo.Lib.UI
open Pomo.Lib.Editor
open FSharp.UMX

module EditorUI =
  let buildRoot(model: EditorModel) : Panel =
    let panel = Panel.create()

    let container =
      VStack.spaced 8
      |> W.hAlign HorizontalAlignment.Left
      |> W.vAlign VerticalAlignment.Bottom
      |> W.padding 20

    let title = Label.create "MAP EDITOR" |> W.textColor Color.Yellow
    let helpHint = Label.create "F1 for help" |> W.size 120 12

    let layerLabel =
      Label.create $"Layer: {model.Camera.CurrentLayer}" |> W.size 100 30

    let brushLabel = Label.create $"Brush: {model.Brush.Mode}" |> W.size 120 30

    let blockLabel =
      Label.create $"Block: {model.Brush.SelectedBlockId |> UMX.untag}"
      |> W.size 150 30

    let collisionLabel =
      Label.create(
        if model.Brush.CollisionEnabled then
          "Collision: On"
        else
          "Collision: Off"
      )
      |> W.size 120 30

    let collisionBtn =
      Btn.empty()
      |> Btn.content collisionLabel
      |> W.size 120 30
      |> Btn.onClick(fun () -> ())

    let container =
      container
      |> W.childrenV [
        title
        helpHint
        layerLabel
        brushLabel
        blockLabel
        collisionBtn
      ]

    panel.Widgets.Add(container)
    panel

  let buildHelp() : Panel =
    let controls = [
      "Tab", "Toggle Camera Mode"
      "WASD", "Move Camera"
      "Scroll", "Zoom"
      "Page Up/Down", "Change Layer"
      "Left Click", "Place Block"
      "Right Click", "Remove Block"
      "Q / E", "Rotate Brush"
      "C", "Toggle Collision"
      "1 / 2", "Brush Mode"
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
    helpContainer

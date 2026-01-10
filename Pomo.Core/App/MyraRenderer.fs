namespace Pomo.Core.App

open System
open Microsoft.Xna.Framework
open Myra.Graphics2D.UI
open FSharp.Data.Adaptive
open Mibo.Elmish
open Pomo.Core.Systems
open Pomo.Core.UI
open Pomo.Core.Editor

/// Myra UI renderer wrapped as Mibo IRenderer<AppModel>.
/// Renders UI overlay based on current scene.
module AppMyraRenderer =

  let private buildMenu(game: Game) =
    MainMenuUI.build game (GuiTriggered >> AppEvents.dispatch)

  let private buildEditorUI(session: EditorSession) =
    // Create reactive camera position for the editor UI
    let camPos = cval session.Camera.Params.Position

    // Build the full Editor UI with palette, tools, etc.
    let editorPanel = EditorUI.build session.State camPos

    // Add a back button overlay
    let backBtn =
      Btn.create "Back to Menu"
      |> W.width 140
      |> W.margin4 10 10 0 0
      |> W.hAlign HorizontalAlignment.Left
      |> W.vAlign VerticalAlignment.Top
      |> Btn.onClick(fun () ->
        AppEvents.dispatch(GuiTriggered GuiAction.BackToMainMenu))

    editorPanel.Widgets.Add(backBtn)

    // Return the panel and the camera position cval for updates
    struct (editorPanel, camPos)

  let private buildGameplayOverlay(mapKey: string) =
    let panel = Panel.create()

    let label = Label.create $"GAMEPLAY: {mapKey}"
    label.HorizontalAlignment <- HorizontalAlignment.Center
    label.VerticalAlignment <- VerticalAlignment.Center
    panel.Widgets.Add(label)

    let backBtn = Btn.create "Back to Menu"
    backBtn.HorizontalAlignment <- HorizontalAlignment.Left
    backBtn.VerticalAlignment <- VerticalAlignment.Top
    backBtn.Width <- Nullable 120

    backBtn.Click.Add(fun _ ->
      AppEvents.dispatch(GuiTriggered GuiAction.BackToMainMenu))

    panel.Widgets.Add(backBtn)
    panel

  /// Create an IRenderer<AppModel> that renders Myra UI overlay.
  let create(game: Game) : IRenderer<AppModel> =
    let mutable currentScene: SceneState voption = ValueNone
    let mutable desktop: Desktop voption = ValueNone
    let mutable editorCamPos: cval<Vector3> voption = ValueNone

    { new IRenderer<AppModel> with
        member _.Draw(ctx, model, gameTime) =
          // Update editor camera position if in editor mode
          match model.EditorSession, editorCamPos with
          | ValueSome session, ValueSome camPos ->
            if camPos.Value <> session.Camera.Params.Position then
              transact(fun () -> camPos.Value <- session.Camera.Params.Position)
          | _ -> ()

          // Only rebuild UI if scene changed
          if currentScene <> ValueSome model.CurrentScene then
            currentScene <- ValueSome model.CurrentScene

            let root =
              match model.CurrentScene, model.EditorSession with
              | MainMenu, _ ->
                editorCamPos <- ValueNone
                buildMenu game :> Widget
              | Gameplay mapKey, _ ->
                editorCamPos <- ValueNone
                buildGameplayOverlay mapKey :> Widget
              | Editor _, ValueSome session ->
                let struct (panel, camPos) = buildEditorUI session
                editorCamPos <- ValueSome camPos
                panel :> Widget
              | Editor _, ValueNone ->
                // Fallback - shouldn't happen
                editorCamPos <- ValueNone
                Panel.create() :> Widget

            match desktop with
            | ValueSome d -> d.Root <- root
            | ValueNone -> desktop <- ValueSome(new Desktop(Root = root))

          desktop |> ValueOption.iter(fun d -> d.Render())
    }

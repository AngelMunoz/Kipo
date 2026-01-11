namespace Pomo.Core.MiboApp

open Microsoft.Xna.Framework
open Myra.Graphics2D.UI
open Pomo.Core.Environment
open Pomo.Core.UI
open Pomo.Core.Editor
open Pomo.Core.Systems
open FSharp.Data.Adaptive



module UIService =
  let create(startInput: Pomo.Core.Environment.IUIService) =
    let mutable desktop: Desktop voption = ValueNone
    let mutable game: Game voption = ValueNone

    // Cache for editor state to avoid rebuilding UI every frame
    let mutable lastEditorLogic: Pomo.Core.Editor.EditorState voption =
      ValueNone

    // Active camera reference for per-frame updates
    let mutable activeCamera: Pomo.Core.Editor.MutableCamera voption = ValueNone

    let camPos: cval<Vector3 voption> = cval ValueNone

    let publishGuiAction(action: GuiAction) =
      match action with
      | GuiAction.BackToMainMenu -> printfn "Back to Main Menu triggered"
      | _ -> ()

    let buildMenu(g: Game) =
      MainMenuUI.build g publishGuiAction :> Widget

    { new Pomo.Core.MiboApp.IUIService with
        member _.Initialize(g) =
          game <- ValueSome g
          Myra.MyraEnvironment.Game <- g

        member _.Render() =
          desktop |> ValueOption.iter(fun d -> d.Render())

        member _.Update() =
          // Update camera position if we have an active camera
          match activeCamera with
          | ValueSome cam ->
            transact(fun () -> camPos.Value <- ValueSome cam.Params.Position)
          | ValueNone -> ()

          desktop
          |> ValueOption.iter(fun d ->
            startInput.SetMouseOverUI d.IsMouseOverGUI)

        member _.Rebuild scene =
          match game with
          | ValueSome g ->
            match scene with
            | MainMenu ->
              // Clean up editor state and active camera
              lastEditorLogic <- ValueNone
              activeCamera <- ValueNone
              transact(fun () -> camPos.Value <- ValueNone)

              let root = buildMenu g

              match desktop with
              | ValueSome d -> d.Root <- root
              | ValueNone -> desktop <- ValueSome(new Desktop(Root = root))

            | Editor state ->
              let logic = state.Logic
              activeCamera <- ValueSome state.Camera

              match lastEditorLogic with
              | ValueSome last when obj.ReferenceEquals(last, logic) ->
                // UI is already built and bound to this logic instance
                ()
              | _ ->
                // Rebuild required - logic instance implies new session/map
                lastEditorLogic <- ValueSome logic

                let root =
                  Pomo.Core.Editor.EditorUI.build
                    logic
                    (camPos |> AVal.map(ValueOption.defaultValue Vector3.Zero))

                match desktop with
                | ValueSome d -> d.Root <- root
                | ValueNone -> desktop <- ValueSome(new Desktop(Root = root))
          | ValueNone -> ()
    }

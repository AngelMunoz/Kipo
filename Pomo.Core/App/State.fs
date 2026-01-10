namespace Pomo.Core.App

open Microsoft.Xna.Framework
open Mibo.Elmish
open Pomo.Core
open Pomo.Core.Systems

module State =
  open Pomo.Core.Editor

  module MapLoader =
    open Pomo.Core.Algorithms

    let tryLoadOrEmpty(key: string) =
      let path = $"Content/CustomMaps/{key}.json"

      match BlockMapLoader.load BlockMapLoader.Resolvers.editor path with
      | Ok map -> map
      | Error e ->
        printfn $"Error loading Map: {path} - {e}"
        BlockMap.createEmpty key 16 8 16

  let init(game: Game) : struct (AppModel * Cmd<AppMsg>) =
    {
      CurrentScene = MainMenu
      EditorSession = ValueNone
    },
    Cmd.none

  let update (appServices: AppEnv) msg model : struct (AppModel * Cmd<AppMsg>) =
    match msg with
    | GuiTriggered action ->
      match action with
      | GuiAction.StartNewGame ->
        {
          model with
              CurrentScene = Gameplay "NewMap"
        },
        Cmd.none
      | GuiAction.OpenMapEditor ->
        let mapDef = MapLoader.tryLoadOrEmpty "NewMap"

        {
          CurrentScene = Editor ValueNone
          EditorSession =
            ValueSome {
              State = Editor.EditorState.create(mapDef)
              Camera = Editor.MutableCamera()
              InputContext = AppEditorInput.create()
            }
        },
        Cmd.none
      | GuiAction.BackToMainMenu ->
        {
          model with
              CurrentScene = MainMenu
              EditorSession = ValueNone
        },
        Cmd.none
      | GuiAction.ExitGame -> exit 0 model, Cmd.none

      | _ -> model, Cmd.none

    | Tick _ -> model, Cmd.none

    | FixedStep _ -> model, Cmd.none

  let subscribe (ctx: GameContext) (model: AppModel) =
    Active(
      SubId.ofString "ui_events",
      fun dispatch -> AppEvents.dispatchSubject.Subscribe(dispatch)
    )

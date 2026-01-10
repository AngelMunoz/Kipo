namespace Pomo.Core.App

open Microsoft.Xna.Framework
open Pomo.Core
open Pomo.Core.Systems
open Pomo.Core.Editor
open FSharp.Control.Reactive
open Pomo.Core.Domain.BlockMap

type SceneState =
  | MainMenu
  | Gameplay of mapKey: string
  | Editor of mapKey: string voption

type EditorSession = {
  State: EditorState
  Camera: MutableCamera
  InputContext: AppEditorInput.InputContext
}

type AppEnv = {
  Services: AppServices
  BlockMapLoader: string -> Result<BlockMapDefinition, string>
}

type AppModel = {
  CurrentScene: SceneState
  EditorSession: EditorSession voption
}

type AppMsg =
  | GuiTriggered of GuiAction
  | Tick of GameTime
  | FixedStep of float32

module AppEvents =
  let dispatchSubject = Subject<AppMsg>.broadcast
  let dispatch msg = dispatchSubject.OnNext msg

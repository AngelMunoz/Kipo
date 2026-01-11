namespace Pomo.Core.MiboApp

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Spatial
open Pomo.Core.Graphics
open Pomo.Core.Editor

type EditorState = {
  Logic: Pomo.Core.Editor.EditorState
  InputContext: EditorInput.EditorInputContext
  Camera: MutableCamera
}

type Scene =
  | MainMenu
  | Editor of EditorState

type AppModel = { CurrentScene: Scene }

type AppMsg =
  | Tick of GameTime
  | TransitionTo of Scene

type IUIService =
  abstract member Initialize: Game -> unit
  abstract member Render: unit -> unit
  abstract member Rebuild: Scene -> unit
  abstract member Update: unit -> unit

open Pomo.Core.Domain.UI

type CoreServices = {
  Stores: Pomo.Core.Environment.StoreServices
  Random: System.Random
  UIService: Pomo.Core.Environment.IUIService
  HUDService: IHUDService
}

type AppEnv = { Core: CoreServices; UI: IUIService }

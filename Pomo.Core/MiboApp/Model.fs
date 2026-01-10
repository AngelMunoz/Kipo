namespace Pomo.Core.MiboApp

open Microsoft.Xna.Framework

type Scene =
  | MainMenu
  | Editor of EditorState

type AppModel = { CurrentScene: Scene }

type AppMsg =
  | Tick of GameTime
  | TransitionTo of Scene

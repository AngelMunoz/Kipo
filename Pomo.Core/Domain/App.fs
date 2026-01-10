namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open Pomo.Core
open Pomo.Core.Domain.Scenes

module App =

    type Msg =
        | FixedStep of float32
        | Tick of GameTime
        | SceneChanged of Scene
        | EditorMsg of Pomo.Core.Editor.EditorAction

    type SceneModel =
        | MainMenu
        // This holds the mutable state for gameplay. 
        // We use the existing GameplayScene state via CompositionRoot logic or similar
        | Gameplay of Pomo.Core.Environment.PomoEnvironment
        | Editor of Pomo.Core.Editor.EditorState
        | Transitioning

    type Model = {
        Scope: GlobalScope
        CurrentScene: SceneModel
        // Shared state (like transitions) can go here
    }

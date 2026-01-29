namespace Pomo.Lib

open Microsoft.Xna.Framework
open Mibo.Elmish

type State() =
  member val EditorState: ValueOption<Editor.State> = ValueNone with get, set

  member val GameplayState: ValueOption<Gameplay.State> =
    ValueNone with get, set


[<Struct>]
type Message =
  | Tick of tick: GameTime
  | EditorMsg of emsg: Editor.Message
  | GameplayMsg of gmsg: Gameplay.Message

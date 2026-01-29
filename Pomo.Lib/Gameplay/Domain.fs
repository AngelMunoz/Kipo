namespace Pomo.Lib.Gameplay

open Microsoft.Xna.Framework

[<Struct>]
type State = { noop: unit }

[<Struct>]
type Message = Tick of GameTime

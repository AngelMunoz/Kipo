namespace Pomo.Lib.Gameplay

open Mibo.Elmish

module Entry =

  let init ctx : struct (State * Cmd<Message>) = { noop = () }, Cmd.none

  let update (msg: Message) (state: State) : struct (State * Cmd<Message>) =
    match msg with
    | Tick _ -> state, Cmd.none

  let view ctx state buffer : unit = ()

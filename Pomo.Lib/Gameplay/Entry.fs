namespace Pomo.Lib.Gameplay

open Mibo.Elmish
open Pomo.Lib
open Pomo.Lib.Services

module Entry =

  let init
    (env: #FileSystemCap & #AssetsCap)
    ctx
    : struct (State * Cmd<Message>) =
    // env is available here for any service-based initialization
    { noop = () }, Cmd.none

  let update
    (env: #FileSystemCap & #AssetsCap)
    (msg: Message)
    (state: State)
    : struct (State * Cmd<Message>) =
    match msg with
    | Tick _ -> state, Cmd.none

  let view
    (env: #FileSystemCap & #AssetsCap)
    ctx
    state
    buffer
    : unit =
    // env is available here for rendering services
    ()

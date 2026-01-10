namespace Pomo.Core.MiboApp

open Mibo.Elmish

module System =

  let inline dispatch
    ([<InlineIfLambda>] dispatcher: 'Snapshot -> Cmd<'Msg>)
    (struct (snap, cmds): struct ('Snapshot * Cmd<'Msg>))
    =
    System.pipe
      (fun snap ->
        let cmds2 = dispatcher snap
        struct (snap, Cmd.batch2(cmds, cmds2)))
      (snap, cmds)

  let inline dispatchWith
    ([<InlineIfLambda>] selectInput: 'Snapshot -> 'Input voption)
    ([<InlineIfLambda>] dispatcher: 'Input voption -> 'Snapshot -> Cmd<'Msg>)
    (struct (snap, cmds): struct ('Snapshot * Cmd<'Msg>))
    =
    System.pipe
      (fun snap ->
        let input = selectInput snap
        let cmds2 = dispatcher input snap
        struct (snap, Cmd.batch2(cmds, cmds2)))
      (snap, cmds)

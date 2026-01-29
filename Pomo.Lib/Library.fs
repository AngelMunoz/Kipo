namespace Pomo.Lib

open Mibo.Elmish
open Mibo.Rendering.Graphics3D
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Lib


[<Struct>]
type State = {
  EditorState: Editor.EditorModel voption
  GameplayState: Gameplay.State voption
  Env: AppEnv
}

type Message =
  | Tick of tick: GameTime
  | EditorMsg of emsg: Editor.EditorMsg
  | GameplayMsg of gmsg: Gameplay.Message

module internal Entry =

  let init ctx : struct (State * Cmd<Message>) =
    let env = AppEnv.create ctx
    let struct (es, cmd) = Editor.Entry.init env ctx

    struct ({
              EditorState = ValueSome es
              GameplayState = ValueNone
              Env = env
            },
            Cmd.map EditorMsg cmd)

  let update msg state : struct (State * Cmd<Message>) =
    match msg with
    | Tick gt ->
      // Forward ticks to active subsystems
      let cmd =
        match state.EditorState with
        | ValueSome _ -> Cmd.ofMsg(EditorMsg(Editor.EditorMsg.Tick gt))
        | ValueNone -> Cmd.none

      struct (state, cmd)

    | EditorMsg emsg ->
      match state.EditorState with
      | ValueSome es ->
        let struct (newEs, cmd) = Editor.Entry.update state.Env emsg es

        struct ({
                  state with
                      EditorState = ValueSome newEs
                },
                Cmd.map EditorMsg cmd)
      | ValueNone -> struct (state, Cmd.none)

    | GameplayMsg gmsg ->
      match state.GameplayState with
      | ValueSome gs ->
        let struct (newGs, cmd) = Gameplay.Entry.update state.Env gmsg gs

        struct ({
                  state with
                      GameplayState = ValueSome newGs
                },
                Cmd.map GameplayMsg cmd)
      | ValueNone -> struct (state, Cmd.none)

  let view ctx (state: State) buffer =
    state.EditorState
    |> ValueOption.iter(fun es -> Editor.Entry.view state.Env ctx es buffer)

module Program =

  let create() : Program<State, Message> =
    Program.mkProgram Entry.init Entry.update
    |> Program.withAssets
    |> Program.withInput
    |> Program.withTick Tick
    |> Program.withPipeline PipelineConfig.defaults Entry.view
    |> Program.withConfig(fun (game, graphicsDeviceManager) ->
      game.Content.RootDirectory <- "Content"
      graphicsDeviceManager.PreferredBackBufferWidth <- 1280
      graphicsDeviceManager.PreferredBackBufferHeight <- 720
      graphicsDeviceManager.GraphicsProfile <- GraphicsProfile.HiDef
      graphicsDeviceManager.HardwareModeSwitch <- false
      graphicsDeviceManager.SynchronizeWithVerticalRetrace <- true
      graphicsDeviceManager.ApplyChanges()

      graphicsDeviceManager.PreparingDeviceSettings.Add(fun args ->
        let pp = args.GraphicsDeviceInformation.PresentationParameters
        pp.BackBufferFormat <- SurfaceFormat.Color
        pp.DepthStencilFormat <- DepthFormat.Depth24Stencil8
        pp.MultiSampleCount <- 4))

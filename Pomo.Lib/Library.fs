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
    let env = EnvFactory.createEditor ctx

    Myra.MyraEnvironment.Game <- ctx.Game
    Myra.MyraEnvironment.EnableModalDarkening <- true

    let struct (es, cmd) = Editor.Entry.init env ctx

    {
      EditorState = ValueSome es
      GameplayState = ValueNone
      Env = env
    },
    Cmd.map EditorMsg cmd

  let update msg state : struct (State * Cmd<Message>) =
    match msg with
    | Tick gt ->
      // Forward ticks to active subsystems
      let eCmd =
        state.EditorState
        |> ValueOption.map(fun es ->
          Cmd.ofMsg(EditorMsg(Editor.EditorMsg.Tick gt)))
        |> ValueOption.defaultValue Cmd.none

      let gCmd =
        state.GameplayState
        |> ValueOption.map(fun gs -> Cmd.ofMsg(GameplayMsg(Gameplay.Tick gt)))
        |> ValueOption.defaultValue Cmd.none

      state, Cmd.batch2(eCmd, gCmd)

    | EditorMsg emsg ->
      state.EditorState
      |> ValueOption.map(fun es ->
        let struct (newEs, newCmd) = Editor.Entry.update state.Env emsg es

        struct ({
                  state with
                      EditorState = ValueSome newEs
                },
                Cmd.map EditorMsg newCmd))
      |> ValueOption.defaultValue(state, Cmd.none)


    | GameplayMsg gmsg ->
      state.GameplayState
      |> ValueOption.map(fun gs ->
        let struct (newGs, newCmd) = Gameplay.Entry.update state.Env gmsg gs

        struct ({
                  state with
                      GameplayState = ValueSome newGs
                },
                Cmd.map GameplayMsg newCmd))
      |> ValueOption.defaultValue(state, Cmd.none)

  let view ctx (state: State) buffer =
    state.EditorState
    |> ValueOption.iter(fun es -> Editor.Entry.view state.Env ctx es buffer)

    state.GameplayState
    |> ValueOption.iter(fun gs -> Gameplay.Entry.view state.Env ctx gs buffer)

  let subscribe ctx (state: State) : Sub<Message> =
    state.EditorState
    |> ValueOption.map(fun es ->
      Editor.Entry.subscribe ctx es |> Sub.map "editor" EditorMsg)
    |> ValueOption.defaultValue Sub.none


module Program =

  let create() : Program<State, Message> =
    Program.mkProgram Entry.init Entry.update
    |> Program.withAssets
    |> Program.withInput
    |> Program.withTick Tick
    |> Program.withSubscription Entry.subscribe
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

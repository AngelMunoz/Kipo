namespace Pomo.Lib


open Mibo.Elmish
open Mibo.Rendering.Graphics3D
open Microsoft.Xna.Framework.Graphics
open Pomo.Lib


module internal Entry =

  let init ctx : struct (State * Cmd<Message>) = State(), Cmd.none

  let update msg state : struct (State * Cmd<Message>) =
    match msg with
    | Tick gt -> state, Cmd.ofMsg(EditorMsg(Editor.Message.Tick gt))
    | EditorMsg emsg ->
      state.EditorState
      |> ValueOption.map(fun es ->
        let struct (es, cmd) = Editor.Entry.update emsg es
        state.EditorState <- ValueSome es
        struct (state, Cmd.map EditorMsg cmd))
      |> ValueOption.defaultValue(state, Cmd.none)
    | GameplayMsg gmsg ->
      state.GameplayState
      |> ValueOption.map(fun gs ->
        let struct (gs, cmd) = Gameplay.Entry.update gmsg gs
        state.GameplayState <- ValueSome gs
        struct (state, Cmd.map GameplayMsg cmd))
      |> ValueOption.defaultValue(state, Cmd.none)

  let view ctx (state: State) buffer =
    state.EditorState
    |> ValueOption.iter(fun es -> Editor.Entry.view ctx es buffer)



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

namespace Pomo.Lib.Editor

open Pomo.Lib.Editor.Domain

open Mibo
open Mibo.Elmish
open Mibo.Rendering.Graphics3D

module Entry =
  open Microsoft.Xna.Framework.Graphics

  let init ctx : struct (State * Cmd<Message>) = { noop = () }, Cmd.none

  let update (msg: Message) (state: State) : struct (State * Cmd<Message>) =
    match msg with
    | Tick _ -> state, Cmd.none

  let view ctx state buffer : unit = ()



  let create() : Program<State, Message> =
    Program.mkProgram init update
    |> Program.withAssets
    |> Program.withInput
    |> Program.withTick Tick
    |> Program.withPipeline PipelineConfig.defaults view
    |> Program.withConfig(fun (game, graphicsDeviceManager) ->
      game.Content.RootDirectory <- "Content"
      game.Window.AllowAltF4 <- true
      game.Window.AllowUserResizing <- true
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

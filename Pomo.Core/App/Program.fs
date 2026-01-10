namespace Pomo.Core.App

open Microsoft.Xna.Framework
open Mibo.Elmish
open Pomo.Core
open Pomo.Core.Domain.Core
open Pomo.Core.Systems

module Program =

  let create() =
    // Initialize pure services without needing the Game instance
    let services = AppServices.create()

    let wrappedInit ctx =
      // Setup Myra UI environment
      Myra.MyraEnvironment.Game <- ctx.Game
      State.init ctx.Game

    let wrappedUpdate msg model =
      let appEnv = {
        Services = services
        BlockMapLoader = BlockMapLoader.load BlockMapLoader.Resolvers.runtime
      }

      State.update appEnv msg model

    let program =
      Program.mkProgram wrappedInit wrappedUpdate
      // Enable input polling (makes IInput available via ctx.Game.Services)
      |> Program.withInput
      // UI (Myra) - draws last (on top)
      |> Program.withRenderer(fun g -> AppMyraRenderer.create g)
      // 3D editor content (clears screen, renders 3D, handles input)
      |> Program.withRenderer(fun g ->
        AppEditorRenderer.create g services Constants.BlockMap3DPixelsPerUnit)
      |> Program.withFixedStep {
        StepSeconds = 1.0f / 60.0f
        MaxStepsPerFrame = 5
        MaxFrameSeconds = ValueSome 0.25f
        Map = FixedStep
      }
      |> Program.withTick Tick
      |> Program.withSubscription State.subscribe
      |> Program.withConfig(fun (game, gdm) ->
        game.IsMouseVisible <- true
        game.Content.RootDirectory <- "Content"
        game.Window.AllowUserResizing <- true

        gdm.PreferredBackBufferWidth <- 1280
        gdm.PreferredBackBufferHeight <- 720
        gdm.PreferMultiSampling <- true

        Pomo.Core.Localization.LocalizationManager.SetCulture
          Pomo.Core.Localization.LocalizationManager.DefaultCultureCode)

    new ElmishGame<_, _>(program)

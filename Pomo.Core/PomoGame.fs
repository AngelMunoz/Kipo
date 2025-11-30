namespace Pomo.Core

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core.Localization
open Pomo.Core.Scenes
open Pomo.Core.Domain.Scenes
open Myra

type PomoGame() as this =
  inherit Game()

  let graphicsDeviceManager = new GraphicsDeviceManager(this)

  let playerId = %Guid.NewGuid()

  // 1. Create Global Scope
  let globalScope = CompositionRoot.createGlobalScope this

  // 2. Create Scene Manager
  let sceneManager =
    new SceneManager(
      this,
      CompositionRoot.sceneTransitionSubject,
      CompositionRoot.SceneFactory.sceneLoader this globalScope playerId
    )

  do
    base.IsMouseVisible <- true
    base.Content.RootDirectory <- "Content"
    base.Window.AllowUserResizing <- true
    graphicsDeviceManager.PreferredBackBufferWidth <- 1280
    graphicsDeviceManager.PreferredBackBufferHeight <- 720

    graphicsDeviceManager.SupportedOrientations <-
      DisplayOrientation.LandscapeLeft ||| DisplayOrientation.LandscapeRight

    // We still need to register GDM
    base.Services.AddService<GraphicsDeviceManager> graphicsDeviceManager

    // Register SceneManager
    base.Components.Add sceneManager

  override _.Initialize() =
    MyraEnvironment.Game <- this

    LocalizationManager.DefaultCultureCode |> LocalizationManager.SetCulture

    base.Initialize()
    // Initial Scene
    CompositionRoot.sceneTransitionSubject.OnNext MainMenu

  override _.Dispose(disposing: bool) = base.Dispose disposing

  override _.Update gameTime = base.Update gameTime

  override _.Draw gameTime =
    base.GraphicsDevice.Clear Color.Black
    base.Draw gameTime

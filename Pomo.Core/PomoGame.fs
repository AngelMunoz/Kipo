namespace Pomo.Core

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core.Localization
open Pomo.Core.Scenes
open Myra

type PomoGame() as this =
  inherit Game()

  let graphicsDeviceManager = new GraphicsDeviceManager(this)

  let playerId = %Guid.NewGuid()

  // 1. Create Global Scope
  let globalScope = CompositionRoot.createGlobalScope this
  // 2. Create Scene Manager
  let sceneManager = new SceneManager()
  let mutable coordinatorDisposable: IDisposable = Unchecked.defaultof<_>

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

  override _.Initialize() =
    MyraEnvironment.Game <- this

    LocalizationManager.DefaultCultureCode |> LocalizationManager.SetCulture

    base.Initialize()

  override _.LoadContent() =
    // 3. Start Scene Coordinator (which handles initial scene loading and transitions)
    let coordinatorSub =
      CompositionRoot.SceneCoordinator.start
        this
        globalScope
        sceneManager
        playerId

    coordinatorDisposable <- coordinatorSub

  override _.Dispose(disposing: bool) =
    if disposing then
      coordinatorDisposable.Dispose()

      (sceneManager :> IDisposable).Dispose()

    base.Dispose disposing

  override _.Update gameTime =
    sceneManager.Update gameTime

    base.Update gameTime

  override _.Draw gameTime =
    base.GraphicsDevice.Clear Color.Black

    sceneManager.Draw gameTime

    base.Draw gameTime

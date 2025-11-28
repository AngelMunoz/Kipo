namespace Pomo.Core

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open Pomo.Core.Localization
open Pomo.Core.Scenes
open Pomo.Core.Domain.Units
open Myra

type PomoGame() as this =
  inherit Game()

  let graphicsDeviceManager = new GraphicsDeviceManager(this)

  let playerId = %Guid.NewGuid()

  let mutable sceneManager: SceneManager voption = ValueNone
  let mutable globalScope: GlobalScope voption = ValueNone
  let mutable coordinatorDisposable: IDisposable voption = ValueNone

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

    // 1. Create Global Scope
    let scope = CompositionRoot.createGlobalScope this
    globalScope <- ValueSome scope

    // 2. Create Scene Manager
    let manager = new SceneManager(this)
    sceneManager <- ValueSome manager

    base.Initialize()

  override _.LoadContent() =
    match globalScope, sceneManager with
    | ValueSome scope, ValueSome manager ->
      // 3. Start Scene Coordinator (which handles initial scene loading and transitions)
      let coordinatorSub =
        CompositionRoot.SceneCoordinator.start this scope manager playerId

      coordinatorDisposable <- ValueSome coordinatorSub
    | _ -> () // Should not happen if Initialize ran correctly

  override _.Dispose(disposing: bool) =
    if disposing then
      coordinatorDisposable |> ValueOption.iter(fun d -> d.Dispose())

      sceneManager |> ValueOption.iter(fun m -> (m :> IDisposable).Dispose())

    base.Dispose disposing

  override _.Update gameTime =
    // Delegate to SceneManager
    sceneManager |> ValueOption.iter(fun m -> m.Update(gameTime))

    base.Update gameTime

  override _.Draw gameTime =
    base.GraphicsDevice.Clear Color.Black // Clear to black to detect if rendering fails

    // Delegate to SceneManager
    sceneManager |> ValueOption.iter(fun m -> m.Draw(gameTime))

    base.Draw gameTime

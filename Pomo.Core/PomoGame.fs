namespace Pomo.Core

open System
open System.Collections.Generic
open System.Globalization
open type System.Net.Mime.MediaTypeNames

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input

open FSharp.Data.Adaptive
open FSharp.UMX

open Pomo.Core.Localization
open Pomo.Core.Pombo

type PomoGame() as this =
  inherit Game()

  let graphicsDeviceManager = new GraphicsDeviceManager(this)

  let isMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()

  let isDesktop =
    OperatingSystem.IsWindows()
    || OperatingSystem.IsLinux()
    || OperatingSystem.IsMacOS()

  let eventBus = new Events.EventBus()

  // 1. Create both the mutable source of truth and the public read-only view.
  let mutableWorld, worldView = World.create()

  do
    base.Services.AddService(
      typeof<GraphicsDeviceManager>,
      graphicsDeviceManager
    )

    base.Content.RootDirectory <- "Content"

    graphicsDeviceManager.SupportedOrientations <-
      DisplayOrientation.LandscapeLeft ||| DisplayOrientation.LandscapeRight

    // 2. Register global services that all systems can safely access.
    base.Services.AddService<Events.EventBus>(eventBus)
    //    Only the READ-ONLY world view is registered, preventing accidental write access.
    base.Services.AddService<World.World>(worldView)

    // 3. Instantiate and add game components (systems).
    //    Inject the mutableWorld via constructor to ensure it remains private.
    let stateUpdater = new Systems.StateUpdateSystem(this, mutableWorld)
    //    Ensure the state updater runs *after* all other systems.
    stateUpdater.UpdateOrder <- 1000

    base.Components.Add(new Systems.MovementSystem(this))
    base.Components.Add(stateUpdater)

  override this.Initialize() =
    base.Initialize()

    LocalizationManager.DefaultCultureCode |> LocalizationManager.SetCulture


  override this.LoadContent() = base.LoadContent()

  // Load game content here
  // e.g., this.Content.Load<Texture2D>("textureName")

  override this.Update(gameTime) =

    let state =
      GamePad.GetState(PlayerIndex.One).Buttons.Back = ButtonState.Pressed
      || Keyboard.GetState().IsKeyDown(Keys.Escape)

    if state then
      this.Exit()

    else
      // Update game logic here
      // e.g., update game entities, handle input, etc.
      // This call now triggers MovementSystem (publishes events) and then
      // StateUpdateSystem (drains event queue and modifies state).
      base.Update(gameTime)


  override this.Draw(gameTime) =

    base.GraphicsDevice.Clear(Color.MonoGameOrange)
    // Draw game content here

    base.Draw(gameTime)

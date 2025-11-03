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
open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.EventBus
open Pomo.Core.Domain.Systems
open Pomo.Core.Domains
open Pomo.Core.Domains.StateUpdate
open Pomo.Core.Domains.Movement


type PomoGame() as this =
  inherit Game()

  let graphicsDeviceManager = new GraphicsDeviceManager(this)

  let isMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()

  let isDesktop =
    OperatingSystem.IsWindows()
    || OperatingSystem.IsLinux()
    || OperatingSystem.IsMacOS()

  let eventBus = new EventBus()

  // 1. Create both the mutable source of truth and the public read-only view.
  let struct (mutableWorld, worldView) = World.create()

  do
    base.Services.AddService<GraphicsDeviceManager> graphicsDeviceManager

    base.Content.RootDirectory <- "Content"

    graphicsDeviceManager.SupportedOrientations <-
      DisplayOrientation.LandscapeLeft ||| DisplayOrientation.LandscapeRight

    // 2. Register global services that all systems can safely access.
    base.Services.AddService<EventBus> eventBus
    //    Only the READ-ONLY world view is registered, preventing accidental write access.
    base.Services.AddService<World.World> worldView

    // 3. Instantiate and add game components (systems).
    base.Components.Add(new MovementSystem(this))
    base.Components.Add(new StateUpdateSystem(this, mutableWorld))

  override this.Initialize() =
    base.Initialize()

    LocalizationManager.DefaultCultureCode |> LocalizationManager.SetCulture


  override this.LoadContent() = base.LoadContent()

  // Load game content here
  // e.g., this.Content.Load<Texture2D>("textureName")

  override this.Update gameTime =
    // Update game logic here
    // e.g., update game entities, handle input, etc.
    // This call now triggers MovementSystem (publishes events) and then
    // StateUpdateSystem (drains event queue and modifies state).
    base.Update gameTime


  override this.Draw gameTime =
    base.GraphicsDevice.Clear Color.MonoGameOrange
    // Draw game content here
    base.Draw gameTime

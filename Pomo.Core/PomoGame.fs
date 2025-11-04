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
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.EventBus
open Pomo.Core.Domain.Systems
open Pomo.Core.Domains
open Pomo.Core.Domains.StateUpdate
open Pomo.Core.Domains.Movement
open Pomo.Core.Domains.Render
open Pomo.Core.Domains.RawInput
open Pomo.Core.Domains.InputMapping
open Pomo.Core.Domains.PlayerMovement
open Pomo.Core.Domains.QuickSlot


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
  let playerId = %Guid.NewGuid()

  do
    base.IsMouseVisible <- true
    base.Content.RootDirectory <- "Content"

    graphicsDeviceManager.SupportedOrientations <-
      DisplayOrientation.LandscapeLeft ||| DisplayOrientation.LandscapeRight

    base.Services.AddService<GraphicsDeviceManager> graphicsDeviceManager
    // 2. Register global services that all systems can safely access.
    base.Services.AddService<EventBus> eventBus
    //    Only the READ-ONLY world view is registered, preventing accidental write access.
    base.Services.AddService<World.World> worldView

    // 3. Instantiate and add game components (systems).
    base.Components.Add(new RawInputSystem(this, playerId))
    base.Components.Add(new InputMappingSystem(this, playerId))
    base.Components.Add(new PlayerMovementSystem(this, playerId))
    base.Components.Add(new QuickSlotSystem(this, playerId))
    base.Components.Add(new MovementSystem(this))
    base.Components.Add(new RenderSystem(this))
    base.Components.Add(new StateUpdateSystem(this, mutableWorld))


  override this.Initialize() =
    base.Initialize()

    LocalizationManager.DefaultCultureCode |> LocalizationManager.SetCulture

    let playerEntity: Pomo.Core.Domain.Entity.EntitySnapshot = {
      Id = playerId
      Position = Vector2(100.0f, 100.0f)
      Velocity = Vector2.Zero
    }

    eventBus.Publish(World.EntityCreated playerEntity)

    let inputMap = InputMapping.createDefaultInputMap()
    eventBus.Publish(World.InputMapChanged struct (playerId, inputMap))


  override this.LoadContent() = base.LoadContent()

  // Load game content here
  // e.g., this.Content.Load<Texture2D>("textureName")

  override this.Update gameTime =
    // Update game logic here
    // e.g., update game entities, handle input, etc.
    // This call now triggers MovementSystem (publishes events) and then
    // StateUpdateSystem (drains event queue and modifies state).
    transact(fun () -> mutableWorld.DeltaTime.Value <- gameTime.ElapsedGameTime)

    base.Update gameTime


  override this.Draw gameTime =
    base.GraphicsDevice.Clear Color.MonoGameOrange
    // Draw game content here
    base.Draw gameTime

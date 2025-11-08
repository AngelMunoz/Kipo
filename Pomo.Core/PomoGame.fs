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
open Pomo.Core.Domains
open Pomo.Core.Domains.StateUpdate
open Pomo.Core.Domains.Movement
open Pomo.Core.Domains.Render
open Pomo.Core.Domains.RawInput
open Pomo.Core.Domains.InputMapping
open Pomo.Core.Domains.PlayerMovement
open Pomo.Core.Domains.Targeting
open Pomo.Core.Systems
open Pomo.Core.Systems.AbilityActivation
open Pomo.Core.Systems.Combat
open Pomo.Core.Systems.Notification

type PomoGame() as this =
  inherit Game()

  let graphicsDeviceManager = new GraphicsDeviceManager(this)

  let isMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()

  let deserializer = Serialization.create()

  let isDesktop =
    OperatingSystem.IsWindows()
    || OperatingSystem.IsLinux()
    || OperatingSystem.IsMacOS()

  let playerId = %Guid.NewGuid()
  let enemyId = %Guid.NewGuid()

  let eventBus = new EventBus()

  // 1. Create both the mutable source of truth and the public read-only view.
  let struct (mutableWorld, worldView) = World.create Random.Shared
  let skillStore = Stores.Skill.create(JsonFileLoader.readSkills deserializer)
  let targetingService = Targeting.create(worldView, eventBus, skillStore)

  let actionHandler =
    ActionHandler.create(worldView, eventBus, targetingService, playerId)

  let movementService = Navigation.create(eventBus, playerId)

  do
    base.IsMouseVisible <- true
    base.Content.RootDirectory <- "Content"

    graphicsDeviceManager.SupportedOrientations <-
      DisplayOrientation.LandscapeLeft ||| DisplayOrientation.LandscapeRight

    base.Services.AddService<GraphicsDeviceManager> graphicsDeviceManager

    base.Services.AddService<Stores.SkillStore> skillStore
    // 2. Register global services that all systems can safely access.
    base.Services.AddService<EventBus> eventBus
    //    Only the READ-ONLY world view is registered, preventing accidental write access.
    base.Services.AddService<World.World> worldView

    base.Services.AddService<TargetingService> targetingService

    // 3. Instantiate and add game components (systems).
    base.Components.Add(new RawInputSystem(this, playerId))
    base.Components.Add(new InputMappingSystem(this, playerId))
    base.Components.Add(new PlayerMovementSystem(this, playerId))
    base.Components.Add(new AbilityActivationSystem(this, playerId))
    base.Components.Add(new CombatSystem(this))
    base.Components.Add(new ProjectileSystem(this))
    base.Components.Add(new MovementSystem(this))
    base.Components.Add(new NotificationSystem(this, eventBus))
    base.Components.Add(new RenderSystem(this, playerId))
    base.Components.Add(new StateUpdateSystem(this, mutableWorld))


  override this.Initialize() =
    base.Initialize()

    LocalizationManager.DefaultCultureCode |> LocalizationManager.SetCulture

    // Player Setup
    let playerEntity: Pomo.Core.Domain.Entity.EntitySnapshot = {
      Id = playerId
      Position = Vector2(100.0f, 100.0f)
      Velocity = Vector2.Zero
    }

    eventBus.Publish(World.EntityCreated playerEntity)

    let playerResources: Pomo.Core.Domain.Entity.Resource = {
      HP = 100
      MP = 50
      Status = Pomo.Core.Domain.Entity.Status.Alive
    }

    eventBus.Publish(World.ResourcesChanged struct (playerId, playerResources))

    let playerFactions = HashSet [ Pomo.Core.Domain.Entity.Faction.Player ]
    eventBus.Publish(World.FactionsChanged struct (playerId, playerFactions))

    let playerBaseStats: Pomo.Core.Domain.Entity.BaseStats = {
      Power = 10
      Magic = 5
      Sense = 7
      Charm = 8
    }

    eventBus.Publish(World.BaseStatsChanged struct (playerId, playerBaseStats))

    let inputMap = InputMapping.createDefaultInputMap()
    eventBus.Publish(World.InputMapChanged struct (playerId, inputMap))

    // Enemy Setup
    let enemyEntity: Pomo.Core.Domain.Entity.EntitySnapshot = {
      Id = enemyId
      Position = Vector2(300.0f, 100.0f)
      Velocity = Vector2.Zero
    }

    eventBus.Publish(World.EntityCreated enemyEntity)

    let enemyResources: Pomo.Core.Domain.Entity.Resource = {
      HP = 80
      MP = 0
      Status = Pomo.Core.Domain.Entity.Status.Alive
    }

    eventBus.Publish(World.ResourcesChanged struct (enemyId, enemyResources))

    let enemyFactions = HashSet [ Pomo.Core.Domain.Entity.Faction.Enemy ]
    eventBus.Publish(World.FactionsChanged struct (enemyId, enemyFactions))

    let enemyBaseStats: Pomo.Core.Domain.Entity.BaseStats = {
      Power = 5
      Magic = 0
      Sense = 5
      Charm = 5
    }

    eventBus.Publish(World.BaseStatsChanged struct (enemyId, enemyBaseStats))

    let quickSlots = [ UseSlot1, UMX.tag 1; UseSlot2, UMX.tag 2 ]

    eventBus.Publish(
      World.QuickSlotsChanged struct (playerId, HashMap.ofList quickSlots)
    )

    // Start listening to action events
    actionHandler.StartListening() |> ignore<IDisposable>
    movementService.StartListening() |> ignore<IDisposable>



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

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
open Pomo.Core.EventBus
open Pomo.Core.Domain
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Events
open Pomo.Core.Systems
open Pomo.Core.Systems.StateUpdate
open Pomo.Core.Systems.Movement
open Pomo.Core.Systems.Render
open Pomo.Core.Systems.RawInput
open Pomo.Core.Systems.InputMapping
open Pomo.Core.Systems.PlayerMovement
open Pomo.Core.Systems.Targeting
open Pomo.Core.Systems.Navigation
open Pomo.Core.Systems.AbilityActivation
open Pomo.Core.Systems.Combat
open Pomo.Core.Systems.Notification
open Pomo.Core.Systems.Projectile
open Pomo.Core.Systems.ActionHandler
open Pomo.Core.Systems.DebugRender
open Pomo.Core.Systems.Effects
open Pomo.Core.Systems.ResourceManager

type PomoGame() as this =
  inherit Game()

  let graphicsDeviceManager = new GraphicsDeviceManager(this)

  let subs = new System.Reactive.Disposables.CompositeDisposable()

  let deserializer = Serialization.create()

  let playerId = %Guid.NewGuid()
  let enemyId = %Guid.NewGuid()

  let eventBus = new EventBus()

  // 1. Create both the mutable source of truth and the public read-only view.
  let struct (mutableWorld, worldView) = World.create Random.Shared
  let skillStore = Stores.Skill.create(JsonFileLoader.readSkills deserializer)
  let targetingService = Targeting.create(worldView, eventBus, skillStore)

  let effectApplicationService =
    Effects.EffectApplication.create(worldView, eventBus)

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
    base.Components.Add(new ResourceManagerSystem(this))
    base.Components.Add(new ProjectileSystem(this))
    base.Components.Add(new MovementSystem(this))
    base.Components.Add(new NotificationSystem(this, eventBus))
    base.Components.Add(new EffectProcessingSystem(this))
    base.Components.Add(new RenderSystem(this, playerId))
    base.Components.Add(new DebugRenderSystem(this, playerId))
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

    eventBus.Publish(
      StateChangeEvent.EntityLifecycle(
        EntityLifecycleEvents.Created playerEntity
      )
    )

    let playerBaseStats: Pomo.Core.Domain.Entity.BaseStats = {
      Power = 10
      Magic = 40
      Sense = 20
      Charm = 30
    }
    
    let maxPlayerHP = playerBaseStats.Charm * 10
    let maxPlayerMP = playerBaseStats.Magic * 5

    let playerResources: Pomo.Core.Domain.Entity.Resource = {
      HP = maxPlayerHP
      MP = maxPlayerMP
      Status = Pomo.Core.Domain.Entity.Status.Alive
    }

    eventBus.Publish(
      StateChangeEvent.Combat(
        CombatEvents.ResourcesChanged struct (playerId, playerResources)
      )
    )

    let playerFactions = HashSet [ Pomo.Core.Domain.Entity.Faction.Player ]

    eventBus.Publish(
      StateChangeEvent.Combat(
        CombatEvents.FactionsChanged struct (playerId, playerFactions)
      )
    )

    eventBus.Publish(
      StateChangeEvent.Combat(
        CombatEvents.BaseStatsChanged struct (playerId, playerBaseStats)
      )
    )

    let inputMap = InputMapping.createDefaultInputMap()

    eventBus.Publish(
      StateChangeEvent.Input(InputEvents.MapChanged struct (playerId, inputMap))
    )

    // Enemy Setup
    let enemyEntity: Pomo.Core.Domain.Entity.EntitySnapshot = {
      Id = enemyId
      Position = Vector2(300.0f, 100.0f)
      Velocity = Vector2.Zero
    }

    eventBus.Publish(
      StateChangeEvent.EntityLifecycle(
        EntityLifecycleEvents.Created enemyEntity
      )
    )

    let enemyBaseStats: Pomo.Core.Domain.Entity.BaseStats = {
      Power = 2
      Magic = 2
      Sense = 2
      Charm = 100
    }

    let maxEnemyHP = enemyBaseStats.Charm * 10
    let maxEnemyMP = enemyBaseStats.Magic * 5

    let enemyResources: Pomo.Core.Domain.Entity.Resource = {
      HP = maxEnemyHP
      MP = maxEnemyMP
      Status = Pomo.Core.Domain.Entity.Status.Alive
    }

    eventBus.Publish(
      StateChangeEvent.Combat(
        CombatEvents.ResourcesChanged struct (enemyId, enemyResources)
      )
    )

    let enemyFactions = HashSet [ Pomo.Core.Domain.Entity.Faction.Enemy ]

    eventBus.Publish(
      StateChangeEvent.Combat(
        CombatEvents.FactionsChanged struct (enemyId, enemyFactions)
      )
    )

    eventBus.Publish(
      StateChangeEvent.Combat(
        CombatEvents.BaseStatsChanged struct (enemyId, enemyBaseStats)
      )
    )

    let quickSlots = [
      UseSlot1, UMX.tag 1
      UseSlot2, UMX.tag 2
      UseSlot3, UMX.tag 3
      UseSlot4, UMX.tag 6
      UseSlot5, UMX.tag 4
      UseSlot6, UMX.tag 5
    ]

    eventBus.Publish(
      StateChangeEvent.Input(
        InputEvents.QuickSlotsChanged
          struct (playerId, HashMap.ofList quickSlots)
      )
    )

    // Start listening to action events
    actionHandler.StartListening() |> subs.Add
    movementService.StartListening() |> subs.Add
    effectApplicationService.StartListening() |> subs.Add


  override _.Dispose(disposing: bool) =
    if disposing then
      subs.Dispose()

    base.Dispose disposing

  override _.LoadContent() = base.LoadContent()

  // Load game content here
  // e.g., this.Content.Load<Texture2D>("textureName")

  override _.Update gameTime =
    // Update game logic here
    // e.g., update game entities, handle input, etc.
    // This call now triggers MovementSystem (publishes events) and then
    // StateUpdateSystem (drains event queue and modifies state).
    transact(fun () ->
      let previous = mutableWorld.Time.Value.TotalGameTime

      mutableWorld.Time.Value <- {
        Delta = gameTime.ElapsedGameTime
        TotalGameTime = gameTime.TotalGameTime
        Previous = previous
      })

    base.Update gameTime


  override _.Draw gameTime =
    base.GraphicsDevice.Clear Color.MonoGameOrange
    // Draw game content here
    base.Draw gameTime

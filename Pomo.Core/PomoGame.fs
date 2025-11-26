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
open Pomo.Core.Systems.UnitMovement
open Pomo.Core.Systems.Combat
open Pomo.Core.Systems.Notification
open Pomo.Core.Systems.Projectile
open Pomo.Core.Systems.ActionHandler
open Pomo.Core.Systems.DebugRender
open Pomo.Core.Systems.Effects
open Pomo.Core.Systems.ResourceManager
open Pomo.Core.Systems.Inventory
open Pomo.Core.Systems.Equipment
open Pomo.Core.Systems.TerrainRenderSystem
open Pomo.Core.Domain.Units
open Pomo.Core.Stores
open Pomo.Core.Systems.Collision

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
open Pomo.Core.Environment
open Pomo.Core.Domain.Camera
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Map
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
open Pomo.Core.Systems.UnitMovement
open Pomo.Core.Systems.Combat
open Pomo.Core.Systems.Notification
open Pomo.Core.Systems.Projectile
open Pomo.Core.Systems.ActionHandler
open Pomo.Core.Systems.DebugRender
open Pomo.Core.Systems.Effects
open Pomo.Core.Systems.ResourceManager
open Pomo.Core.Systems.Inventory
open Pomo.Core.Systems.Equipment
open Pomo.Core.Systems.TerrainRenderSystem
open Pomo.Core.Systems.EntitySpawnerLogic
open Pomo.Core.Domain.Units
open Pomo.Core.Stores
open Pomo.Core.Systems.Collision

type PomoGame() as this =
  inherit Game()

  let graphicsDeviceManager = new GraphicsDeviceManager(this)

  let subs = new System.Reactive.Disposables.CompositeDisposable()

  let deserializer = Serialization.create()

  let playerId = %Guid.NewGuid()

  let eventBus = new EventBus()

  // 1. Create both the mutable source of truth and the public read-only view.
  let struct (mutableWorld, worldView) = World.create Random.Shared
  let skillStore = Stores.Skill.create(JsonFileLoader.readSkills deserializer)
  let itemStore = Stores.Item.create(JsonFileLoader.readItems deserializer)

  let aiArchetypeStore =
    Stores.AIArchetype.create(JsonFileLoader.readAIArchetypes deserializer)

  let mapStore =
    Stores.Map.create MapLoader.loadMap [ "Content/Maps/Proto.xml" ]


  let projections = Projections.create(itemStore, worldView)

  let targetingService = Targeting.create(eventBus, skillStore, projections)

  let effectApplicationService =
    Effects.EffectApplication.create(worldView, eventBus)

  let cameraSystem =
    CameraSystem.create(this, projections, Array.singleton playerId)

  let actionHandler =
    ActionHandler.create(
      worldView,
      eventBus,
      targetingService,
      projections,
      cameraSystem,
      playerId
    )

  let movementService =
    Navigation.create(eventBus, mapStore, "Proto1", worldView)

  let inventoryService = Inventory.create(eventBus, itemStore, worldView)
  let equipmentService = Equipment.create worldView eventBus


  do
    base.IsMouseVisible <- true
    base.Content.RootDirectory <- "Content"
    base.Window.AllowUserResizing <- true
    graphicsDeviceManager.PreferredBackBufferWidth <- 1280
    graphicsDeviceManager.PreferredBackBufferHeight <- 720

    graphicsDeviceManager.SupportedOrientations <-
      DisplayOrientation.LandscapeLeft ||| DisplayOrientation.LandscapeRight

    base.Services.AddService<GraphicsDeviceManager> graphicsDeviceManager

    base.Services.AddService<Projections.ProjectionService> projections

    base.Services.AddService<SkillStore> skillStore
    base.Services.AddService<ItemStore> itemStore
    base.Services.AddService<AIArchetypeStore> aiArchetypeStore
    base.Services.AddService<MapStore> mapStore
    base.Services.AddService<EventBus> eventBus

    base.Services.AddService<World.World> worldView

    base.Services.AddService<TargetingService> targetingService
    base.Services.AddService<CameraService> cameraSystem

    base.Components.Add(new RawInputSystem(this, playerId))
    base.Components.Add(new InputMappingSystem(this, playerId))
    base.Components.Add(new PlayerMovementSystem(this, playerId))
    base.Components.Add(new UnitMovementSystem(this, playerId))
    base.Components.Add(new AbilityActivationSystem(this, playerId))
    base.Components.Add(new CombatSystem(this))
    base.Components.Add(new ResourceManagerSystem(this))
    base.Components.Add(new ProjectileSystem(this))
    base.Components.Add(new CollisionSystem(this, "Proto1"))
    base.Components.Add(new MovementSystem(this))

    base.Components.Add(
      new NotificationSystem(this, eventBus, DrawOrder = Render.Layer.UI)
    )

    base.Components.Add(new EffectProcessingSystem(this))

    base.Components.Add(
      new EntitySpawnerSystem(this, aiArchetypeStore, itemStore)
    )

    base.Components.Add(
      new RenderOrchestratorSystem.RenderOrchestratorSystem(
        this,
        "Proto1",
        playerId,
        DrawOrder = Render.Layer.TerrainBase
      )
    )

    base.Components.Add(
      new DebugRenderSystem(
        this,
        playerId,
        "Proto1",
        DrawOrder = Render.Layer.Debug
      )
    )

    base.Components.Add(
      new AISystem(this, worldView, eventBus, skillStore, aiArchetypeStore)
    )

    base.Components.Add(new StateUpdateSystem(this, mutableWorld))


  override _.Initialize() =
    base.Initialize()

    LocalizationManager.DefaultCultureCode |> LocalizationManager.SetCulture

    // --- Map Based Spawning ---
    let mapKey = "Proto1"
    let mapDef = mapStore.find mapKey

    let mutable playerSpawned = false
    let mutable enemyCount = 0

    let maxEnemies =
      mapDef.Properties
      |> HashMap.tryFind "MaxEnemyEntities"
      |> Option.bind(fun v ->
        match System.Int32.TryParse v with
        | true, i -> Some i
        | _ -> None)
      |> Option.defaultValue 5



    // Find Object Groups
    for group in mapDef.ObjectGroups do
      for obj in group.Objects do
        match obj.Type with
        | ValueSome MapObjectType.Spawn ->
          // Check for Player Spawn
          let isPlayerSpawn =
            obj.Properties
            |> HashMap.tryFind "PlayerSpawn"
            |> Option.map(fun v -> v.ToLower() = "true")
            |> Option.defaultValue false

          if isPlayerSpawn && not playerSpawned then
            // Spawn Player 1 here
            let intent: SystemCommunications.SpawnEntityIntent = {
              EntityId = playerId
              Type = SystemCommunications.SpawnType.Player 0
              Position = Vector2(obj.X, obj.Y)
            }

            eventBus.Publish intent
            playerSpawned <- true

          // Check for AI Spawn
          elif not isPlayerSpawn && enemyCount < maxEnemies then
            let enemyId = Guid.NewGuid() |> UMX.tag
            // Determine archetype (alternate between 1 and 2)
            let archetypeId = if enemyCount % 2 = 0 then %1 else %2

            let intent: SystemCommunications.SpawnEntityIntent = {
              EntityId = enemyId
              Type = SystemCommunications.SpawnType.Enemy archetypeId
              Position = Vector2(obj.X, obj.Y)
            }

            eventBus.Publish intent
            enemyCount <- enemyCount + 1

        | _ -> ()

    // Start listening to action events
    actionHandler.StartListening() |> subs.Add
    movementService.StartListening() |> subs.Add
    targetingService.StartListening() |> subs.Add
    effectApplicationService.StartListening() |> subs.Add
    inventoryService.StartListening() |> subs.Add
    equipmentService.StartListening() |> subs.Add


  override _.Dispose(disposing: bool) =
    if disposing then
      subs.Dispose()

    base.Dispose disposing

  override _.Update gameTime =
    transact(fun () ->
      let previous = mutableWorld.Time.Value.TotalGameTime

      mutableWorld.Time.Value <- {
        Delta = gameTime.ElapsedGameTime
        TotalGameTime = gameTime.TotalGameTime
        Previous = previous
      })

    base.Update gameTime

  override _.Draw gameTime =
    base.GraphicsDevice.Clear Color.Peru
    base.Draw gameTime

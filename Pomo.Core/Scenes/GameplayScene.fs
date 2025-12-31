namespace Pomo.Core.Scenes

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open FSharp.Control.Reactive
open System.Reactive.Disposables
open Myra.Graphics2D.UI

open Pomo.Core
open Pomo.Core.EventBus
open Pomo.Core.Domain
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.Scenes
open Pomo.Core.Domain.UI
open Pomo.Core.Stores
open Pomo.Core.Environment
open Pomo.Core.Algorithms

// System Imports
open Pomo.Core.Systems
open Pomo.Core.Systems.Effects
open Pomo.Core.Systems.Movement
open Pomo.Core.Systems.RawInput
open Pomo.Core.Systems.InputMapping
open Pomo.Core.Systems.PlayerMovement
open Pomo.Core.Systems.AbilityActivation
open Pomo.Core.Systems.UnitMovement
open Pomo.Core.Systems.Combat
open Pomo.Core.Systems.Notification
open Pomo.Core.Systems.Projectile
open Pomo.Core.Systems.ResourceManager
open Pomo.Core.Systems.EntitySpawnerLogic
open Pomo.Core.Systems.StateWrite
open Pomo.Core.Systems.Collision

/// Creates and manages the Gameplay scene with all systems, services, and map lifecycle
module GameplayScene =

  /// Creates a complete gameplay scene with all systems and services
  let create
    (game: Game)
    (stores: StoreServices)
    (monoGame: MonoGameServices)
    (random: Random)
    (uiService: IUIService)
    (hudService: IHUDService)
    (sceneTransitionSubject: IObserver<Scene>)
    (playerId: Guid<EntityId>)
    (initialMapKey: string)
    (initialTargetSpawn: string voption)
    =
    // Mutable state for the GameplayScene instance
    let mutable currentMapKey: string voption = ValueNone

    // 1. Create World and Local EventBus
    let eventBus = new EventBus()
    let struct (mutableWorld, worldView) = World.create random
    let stateWriteService = StateWrite.create mutableWorld

    // 2. Create Gameplay Services
    let physicsCacheService = Projections.PhysicsCache.create worldView

    let projections =
      Projections.create(stores.ItemStore, worldView, physicsCacheService)

    let cameraService =
      CameraSystem.create(
        game,
        projections,
        worldView,
        Array.singleton playerId
      )

    let targetingService =
      Targeting.create(
        eventBus,
        stateWriteService,
        stores.SkillStore,
        projections
      )

    // 3. Create Listeners
    let effectApplication =
      EffectApplication.create(worldView, stateWriteService, eventBus)

    let actionHandler =
      ActionHandler.create(
        worldView,
        eventBus,
        stateWriteService,
        targetingService,
        projections,
        cameraService,
        playerId
      )

    let navigationService =
      Navigation.create(
        eventBus,
        stateWriteService,
        stores.MapStore,
        projections
      )

    let inventoryService =
      Inventory.create(eventBus, stores.ItemStore, worldView, stateWriteService)

    let equipmentService = Equipment.create worldView eventBus stateWriteService

    // 4. Construct PomoEnvironment (Local Scope)
    let pomoEnv =
      { new Pomo.Core.Environment.PomoEnvironment with
          member _.CoreServices =
            { new CoreServices with
                member _.EventBus = eventBus
                member _.World = worldView
                member _.StateWrite = stateWriteService
                member _.Random = random
                member _.UIService = uiService
                member _.HUDService = hudService
            }

          member _.GameplayServices =
            { new GameplayServices with
                member _.Projections = projections
                member _.TargetingService = targetingService
                member _.CameraService = cameraService
            }

          member _.ListenerServices =
            { new ListenerServices with
                member _.EffectApplication = effectApplication
                member _.ActionHandler = actionHandler
                member _.NavigationService = navigationService
                member _.InventoryService = inventoryService
                member _.EquipmentService = equipmentService
            }

          member _.MonoGameServices = monoGame
          member _.StoreServices = stores
      }

    // Systems that are always present (order matters!)
    let baseComponents = new ResizeArray<IGameComponent>()
    baseComponents.Add(new RawInputSystem(game, pomoEnv, playerId))
    baseComponents.Add(new InputMappingSystem(game, pomoEnv, playerId))
    baseComponents.Add(new PlayerMovementSystem(game, pomoEnv, playerId))
    baseComponents.Add(new UnitMovementSystem(game, pomoEnv, playerId))
    baseComponents.Add(new AbilityActivationSystem(game, pomoEnv, playerId))
    baseComponents.Add(new CombatSystem(game, pomoEnv))
    baseComponents.Add(new ResourceManagerSystem(game, pomoEnv))
    baseComponents.Add(new ProjectileSystem(game, pomoEnv))
    baseComponents.Add(new OrbitalSystem.OrbitalSystem(game, pomoEnv))

    baseComponents.Add(new MovementSystem(game, pomoEnv))
    baseComponents.Add(new CollisionSystem(game, pomoEnv))

    baseComponents.Add(new NotificationSystem(game, pomoEnv))
    baseComponents.Add(UIController.create game pomoEnv playerId)

    baseComponents.Add(new EffectProcessingSystem(game, pomoEnv))
    baseComponents.Add(new EntitySpawnerSystem(game, pomoEnv))
    baseComponents.Add(new AISystem(game, pomoEnv))
    baseComponents.Add(new AnimationSystem(game, pomoEnv))
    baseComponents.Add(new ParticleSystem.ParticleSystem(game, pomoEnv))
    baseComponents.Add(new MotionStateAnimationSystem(game, pomoEnv))

    let cursorService = CursorSystem.create game

    let hoverFeedbackSystem =
      HoverFeedback.create
        game
        cameraService
        cursorService
        targetingService
        projections
        worldView
        playerId

    baseComponents.Add(hoverFeedbackSystem)

    // Map Dependent Systems (Renderers)
    let mapDependentComponents = new ResizeArray<IGameComponent>()

    let clearCurrentMapData() =
      stateWriteService.RemoveEntity(playerId)

    // Initialize WorldMapService
    let worldMapService =
      new Pomo.Core.Systems.WorldMapService.WorldMapService()

    worldMapService.Initialize()

    let spawnEntitiesForMap
      (mapDef: Map.MapDefinition)
      (spawnPlayerId: Guid<EntityId>)
      (scenarioId: Guid<ScenarioId>)
      (targetSpawn: string voption)
      =
      let maxEnemies = MapSpawning.getMaxEnemies mapDef

      let mapEntityGroupStore =
        MapSpawning.tryLoadMapEntityGroupStore mapDef.Key

      // Generate NavGrid for spawn validation
      let navGrid =
        Algorithms.Pathfinding.Grid.generate
          mapDef
          Domain.Core.Constants.Navigation.GridCellSize
          Domain.Core.Constants.Navigation.EntitySize

      let candidates = MapSpawning.extractSpawnCandidates mapDef navGrid random

      let playerPos = MapSpawning.findPlayerSpawnPosition targetSpawn candidates

      // Spawn player
      let playerIntent: SystemCommunications.SpawnEntityIntent = {
        EntityId = spawnPlayerId
        ScenarioId = scenarioId
        Type = SystemCommunications.SpawnType.Player 0
        Position = playerPos
      }

      eventBus.Publish(GameEvent.Spawn(SpawningEvent.SpawnEntity playerIntent))

      // Spawn enemies using MapSpawning module
      let spawnCtx: MapSpawning.SpawnContext = {
        Random = random
        MapEntityGroupStore = mapEntityGroupStore
        EntityStore = stores.AIEntityStore
        ScenarioId = scenarioId
        MaxEnemies = maxEnemies
      }

      let publisher: MapSpawning.SpawnEventPublisher = {
        RegisterZones =
          fun zones ->
            eventBus.Publish(GameEvent.Spawn(SpawningEvent.RegisterZones zones))
        SpawnEntity =
          fun entity ->
            eventBus.Publish(GameEvent.Spawn(SpawningEvent.SpawnEntity entity))
      }

      MapSpawning.spawnEnemiesForScenario spawnCtx candidates publisher

    let loadMap (newMapKey: string) (targetSpawn: string voption) =
      // Show loading overlay immediately
      hudService.ShowLoadingOverlay()

      let startTime = System.Diagnostics.Stopwatch.StartNew()

      mapDependentComponents.Clear()
      clearCurrentMapData()

      let mapDef = stores.MapStore.find newMapKey

      // Create Scenario
      let scenarioId = Guid.NewGuid() |> UMX.tag<ScenarioId>

      let scenario: World.Scenario = {
        Id = scenarioId
        Map = ValueSome mapDef
        BlockMap = ValueNone
      }

      mutableWorld.Scenarios[scenarioId] <- scenario

      let renderOrchestrator =
        RenderOrchestrator.create(
          game,
          pomoEnv,
          Rendering.TileMap mapDef,
          playerId,
          Render.Layer.TerrainBase
        )

      mapDependentComponents.Add(renderOrchestrator)

      spawnEntitiesForMap mapDef playerId scenarioId targetSpawn

      let elapsed = startTime.Elapsed
      let minDuration = AssetPreloader.Constants.MinLoadingOverlayDuration

      if elapsed < minDuration then
        System.Threading.Thread.Sleep(minDuration - elapsed)

      hudService.HideLoadingOverlay()

    // UI for Gameplay (HUD)
    let mutable hudDesktop: Desktop voption = ValueNone

    let publishHudGuiAction(action: GuiAction) =
      match action with
      | GuiAction.BackToMainMenu -> sceneTransitionSubject.OnNext MainMenu
      | GuiAction.ToggleCharacterSheet ->
        hudService.TogglePanelVisible HUDPanelId.CharacterSheet
      | GuiAction.ToggleEquipment ->
        hudService.TogglePanelVisible HUDPanelId.EquipmentPanel
      | GuiAction.StartNewGame
      | GuiAction.OpenSettings
      | GuiAction.OpenMapEditor
      | GuiAction.ExitGame -> ()

    // 6. Setup Listeners (Subs)
    let subs = new CompositeDisposable()

    // Bridge EventBus to SceneTransitionSubject
    subs.Add(
      eventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Scene(SceneEvent.Transition t) -> Some t
        | _ -> None)
      |> Observable.subscribe(fun event ->
        sceneTransitionSubject.OnNext event.Scene)
    )

    // Initialize Listeners
    subs.Add(actionHandler.StartListening())
    subs.Add(navigationService.StartListening())
    subs.Add(targetingService.StartListening())
    subs.Add(effectApplication.StartListening())
    subs.Add(inventoryService.StartListening())
    subs.Add(equipmentService.StartListening())

    // Load initial map
    loadMap initialMapKey initialTargetSpawn

    // Handle Portal Travel
    subs.Add(
      eventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Intent(IntentEvent.Portal intent) -> Some intent
        | _ -> None)
      |> Observable.subscribe(fun event ->
        if event.EntityId = playerId then
          hudService.ShowLoadingOverlay()

          sceneTransitionSubject.OnNext(
            Gameplay(event.TargetMap, ValueSome event.TargetSpawn)
          ))
    )

    // Create ad-hoc components for World Update and HUD
    let worldUpdateComponent =
      { new GameComponent(game) with
          override _.Update(gameTime) =
            transact(fun () ->
              let previous = mutableWorld.Time.Value.TotalGameTime

              mutableWorld.Time.Value <- {
                Delta = gameTime.ElapsedGameTime
                TotalGameTime = gameTime.TotalGameTime
                Previous = previous
              })

            // Flush ring buffer events to subscribers
            eventBus.FlushToObservable()

            hudDesktop
            |> ValueOption.iter(fun d ->
              uiService.SetMouseOverUI d.IsMouseOverGUI)
      }

    let hudDrawComponent =
      { new DrawableGameComponent(game, DrawOrder = Render.Layer.UI) with
          override _.LoadContent() =
            let root =
              Pomo.Core.Systems.GameplayUI.build
                game
                pomoEnv
                playerId
                publishHudGuiAction

            hudDesktop <- ValueSome(new Desktop(Root = root))

          override _.Draw(gameTime) =
            hudDesktop |> ValueOption.iter(fun d -> d.Render())
      }

    // Flush all queued state writes at the end of the frame
    let stateWriteFlushComponent =
      { new GameComponent(game, UpdateOrder = 1000) with
          override _.Update(_) =
            stateWriteService.FlushWrites()
            physicsCacheService.RefreshAllCaches()
      }

    baseComponents.Add(worldUpdateComponent)
    baseComponents.Add(hudDrawComponent)
    baseComponents.Add(stateWriteFlushComponent)

    let allComponents = [ yield! baseComponents; yield! mapDependentComponents ]

    let disposable =
      { new IDisposable with
          member _.Dispose() =
            subs.Dispose()
            // Dispose EventBus to release ring buffer
            (eventBus :> IDisposable).Dispose()
            // Dispose StateWriteService to return pooled array
            stateWriteService.Dispose()
            hudDesktop |> ValueOption.iter(fun d -> d.Dispose())
            // Cleanup map dependent components
            for c in mapDependentComponents do
              match c with
              | :? IDisposable as d -> d.Dispose()
              | _ -> ()

            mapDependentComponents.Clear()
      }

    struct (allComponents, disposable)

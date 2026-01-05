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
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Scenes
open Pomo.Core.Domain.UI
open Pomo.Core.Stores
open Pomo.Core.Environment
open Pomo.Core.Algorithms
open Pomo.Core.Domain.Core.Constants

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
/// Uses BlockMap for 3D world rendering and Navigation3D for pathfinding
module GameplayScene =

  let inline private clamp (minV: float32) (maxV: float32) (v: float32) =
    if v < minV then minV
    elif v > maxV then maxV
    else v

  let private clampToMapBounds (map: BlockMapDefinition) (pos: WorldPosition) =
    let maxX = float32 map.Width * BlockMap.CellSize
    let maxY = float32 map.Height * BlockMap.CellSize
    let maxZ = float32 map.Depth * BlockMap.CellSize

    {
      X = clamp 0.0f maxX pos.X
      Y = clamp 0.0f maxY pos.Y
      Z = clamp 0.0f maxZ pos.Z
    }

  /// Get BlockMap for a scenario (used by Navigation3D)
  let private createBlockMapProvider
    (scenarioBlockMaps: Dictionary<Guid<ScenarioId>, BlockMapDefinition>)
    : Guid<ScenarioId> -> BlockMapDefinition voption =
    fun scenarioId -> scenarioBlockMaps |> Dictionary.tryFindV scenarioId

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
    (blockMap: BlockMapDefinition)
    =
    // 1. Create World and Local EventBus
    let eventBus = new EventBus()
    let struct (mutableWorld, worldView) = World.create random
    let stateWriteService = StateWrite.create mutableWorld

    // 2. Create Gameplay Services
    let physicsCacheService = Projections.PhysicsCache.create worldView

    let projections =
      Projections.create(stores.ItemStore, worldView, physicsCacheService)

    // Use BlockMap 3D camera
    let cameraService =
      BlockMapCameraSystem.create game projections blockMap playerId

    let targetingService =
      Targeting.create(
        eventBus,
        stateWriteService,
        stores.SkillStore,
        projections
      )

    let getPickBounds =
      Pomo.Core.Graphics.ModelMetrics.createPickBoundsResolver
        monoGame.Content
        stores.ModelStore

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
        getPickBounds,
        playerId
      )

    // BlockMap storage for Navigation3D
    let scenarioBlockMaps = Dictionary<Guid<ScenarioId>, BlockMapDefinition>()
    let getBlockMap = createBlockMapProvider scenarioBlockMaps

    // Use Navigation3D for BlockMap-based pathfinding
    let navigation3DService =
      Navigation3D.create eventBus stateWriteService projections getBlockMap

    let inventoryService =
      Inventory.create(eventBus, stores.ItemStore, worldView, stateWriteService)

    let equipmentService = Equipment.create worldView eventBus stateWriteService

    // 4. Create Scenario and store BlockMap
    let scenarioId = Guid.NewGuid() |> UMX.tag<ScenarioId>

    let scenario: World.Scenario = {
      Id = scenarioId
      BlockMap = ValueSome blockMap
    }

    mutableWorld.Scenarios[scenarioId] <- scenario
    scenarioBlockMaps[scenarioId] <- blockMap

    // 5. Construct PomoEnvironment (Local Scope)
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
                member _.NavigationService = navigation3DService
                member _.InventoryService = inventoryService
                member _.EquipmentService = equipmentService
            }

          member _.MonoGameServices = monoGame
          member _.StoreServices = stores
      }

    // 6. Create Systems (order matters!)
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
        getPickBounds
        projections
        worldView
        playerId

    baseComponents.Add(hoverFeedbackSystem)

    // 7. Spawn player at BlockMap spawn point
    let playerPos =
      BlockMapSpawning.findPlayerSpawnPosition blockMap
      |> clampToMapBounds blockMap

    let playerIntent: SystemCommunications.SpawnEntityIntent = {
      EntityId = playerId
      ScenarioId = scenarioId
      Type = SystemCommunications.SpawnType.Player 0
      Position = playerPos
    }

    eventBus.Publish(GameEvent.Spawn(SpawningEvent.SpawnEntity playerIntent))

    let mapKey = blockMap.MapKey |> ValueOption.defaultValue blockMap.Key

    let mapEntityGroupStore =
      let primary = MapSpawning.tryLoadMapEntityGroupStore mapKey
      let proto = MapSpawning.tryLoadMapEntityGroupStore "Proto"

      match primary, proto with
      | ValueSome p, ValueSome pr ->
        let tryFindMerged groupName =
          p.tryFind groupName
          |> ValueOption.orElseWith(fun () -> pr.tryFind groupName)

        ValueSome
          { new MapEntityGroupStore with
              member _.tryFind groupName = tryFindMerged groupName

              member _.find groupName =
                match tryFindMerged groupName with
                | ValueSome g -> g
                | ValueNone -> failwith $"MapEntityGroup not found: {groupName}"

              member _.all() = seq {
                yield! p.all()
                yield! pr.all()
              }
          }
      | ValueSome p, ValueNone -> ValueSome p
      | ValueNone, ValueSome pr -> ValueSome pr
      | ValueNone, ValueNone -> ValueNone

    let candidates = BlockMapSpawning.extractSpawnCandidates blockMap

    let maxEnemies =
      candidates |> Array.sumBy(fun c -> if c.IsPlayerSpawn then 0 else 1)

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

    // 8. RenderOrchestrator for BlockMap
    let renderOrchestrator =
      RenderOrchestrator.create(
        game,
        pomoEnv,
        Rendering.BlockMap3D blockMap,
        playerId,
        Render.Layer.TerrainBase
      )

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

    // 9. Setup Listeners (Subs)
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
    subs.Add(navigation3DService.StartListening())
    subs.Add(targetingService.StartListening())
    subs.Add(effectApplication.StartListening())
    subs.Add(inventoryService.StartListening())
    subs.Add(equipmentService.StartListening())

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

    // 10. Create ad-hoc components for World Update and HUD
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
            hudService.HideLoadingOverlay()

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
    baseComponents.Add(renderOrchestrator)

    let allComponents = [ yield! baseComponents ]

    let disposable =
      { new IDisposable with
          member _.Dispose() =
            subs.Dispose()
            // Dispose EventBus to release ring buffer
            (eventBus :> IDisposable).Dispose()
            // Dispose StateWriteService to return pooled array
            stateWriteService.Dispose()
            hudDesktop |> ValueOption.iter(fun d -> d.Dispose())
      }

    struct (allComponents, disposable)

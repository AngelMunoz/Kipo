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
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Scenes
open Pomo.Core.Domain.UI
open Pomo.Core.Domain.Core
open Pomo.Core.Stores
open Pomo.Core.Environment
open Pomo.Core.MapSpawning

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

/// BlockMap-based gameplay scene for 3D voxel worlds
/// Mirrors GameplayScene but uses BlockMaps for collision/navigation
module BlockMapScene =

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

  /// Creates a complete BlockMap gameplay scene
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

    // Use BlockMap 3D camera instead of TileMap camera
    let cameraService =
      BlockMapCameraSystem.create game projections blockMap playerId

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

    // BlockMap storage for Navigation3D
    let scenarioBlockMaps = Dictionary<Guid<ScenarioId>, BlockMapDefinition>()
    let getBlockMap = createBlockMapProvider scenarioBlockMaps

    // Use Navigation3D instead of 2D Navigation
    let navigation3DService =
      Navigation3D.create eventBus stateWriteService projections getBlockMap

    let inventoryService =
      Inventory.create(eventBus, stores.ItemStore, worldView, stateWriteService)

    let equipmentService = Equipment.create worldView eventBus stateWriteService

    // 4. Create PomoEnvironment
    let pomoEnv =
      { new PomoEnvironment with
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

    // 5. Create Scenario with BlockMap
    let scenarioId = Guid.NewGuid() |> UMX.tag<ScenarioId>

    let scenario: World.Scenario = {
      Id = scenarioId
      Map = ValueNone // No TileMap for BlockMap scenarios
      BlockMap = ValueSome blockMap
    }

    mutableWorld.Scenarios[scenarioId] <- scenario
    scenarioBlockMaps[scenarioId] <- blockMap

    // 6. Systems (same as GameplayScene)
    let baseComponents = ResizeArray<IGameComponent>()
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

    // 7. Spawn player at BlockMap spawn point
    let playerPos =
      BlockMapSpawning.findPlayerSpawnPosition blockMap
      |> clampToMapBounds blockMap

    let playerIntent: SystemCommunications.SpawnEntityIntent = {
      EntityId = playerId
      ScenarioId = scenarioId
      Type = SystemCommunications.SpawnType.Player 0
      Position = playerPos // Full 3D position preserved
    }

    eventBus.Publish(GameEvent.Spawn(SpawningEvent.SpawnEntity playerIntent))

    let tryFindEnemySpawn(map: BlockMapDefinition) =
      map.Objects
      |> List.tryPick(fun obj ->
        match obj.Data with
        | MapObjectData.Spawn props when not props.IsPlayerSpawn ->
          props.EntityGroup
          |> ValueOption.map(fun group -> struct (obj, group))
          |> ValueOption.toOption
        | _ -> None)

    let tryResolvePlaytestEnemy
      (groupName: string)
      : MapSpawning.ResolvedEntityInfo voption =
      let tryResolveFrom(store: MapEntityGroupStore option) =
        MapSpawning.tryResolveEntityFromGroup
          random
          store
          stores.AIEntityStore
          groupName

      let primary = MapSpawning.tryLoadMapEntityGroupStore blockMap.Key

      tryResolveFrom primary
      |> ValueOption.orElseWith(fun () ->
        tryResolveFrom(MapSpawning.tryLoadMapEntityGroupStore "Proto"))

    let trySpawnSingleEnemy() =
      let struct (spawnObj, groupName) =
        tryFindEnemySpawn blockMap
        |> Option.defaultValue(
          struct ({
                    Id = 0
                    Name = "Playtest Enemy"
                    Position = {
                      X = playerPos.X + BlockMap.CellSize * 4.0f
                      Y = playerPos.Y
                      Z = playerPos.Z
                    }
                    Rotation = ValueNone
                    Shape = MapObjectShape.Box(Vector3(BlockMap.CellSize))
                    Data =
                      MapObjectData.Spawn {
                        IsPlayerSpawn = false
                        EntityGroup = ValueSome "magic_casters"
                        MaxSpawns = 1
                        Faction = ValueNone
                      }
                  },
                  "magic_casters")
        )

      let resolved = tryResolvePlaytestEnemy groupName

      resolved
      |> ValueOption.iter(fun resolved ->
        let enemyId = Guid.NewGuid() |> UMX.tag

        let info: SystemCommunications.FactionSpawnInfo = {
          ArchetypeId = resolved.ArchetypeId
          EntityDefinitionKey = ValueSome resolved.EntityKey
          MapOverride = resolved.MapOverride
          Faction = resolved.Faction
          SpawnZoneName = ValueSome spawnObj.Name
        }

        let enemyIntent: SystemCommunications.SpawnEntityIntent = {
          EntityId = enemyId
          ScenarioId = scenarioId
          Type = SystemCommunications.SpawnType.Faction info
          Position = spawnObj.Position |> clampToMapBounds blockMap
        }

        eventBus.Publish(
          GameEvent.Spawn(SpawningEvent.SpawnEntity enemyIntent)
        ))

    trySpawnSingleEnemy()

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

    let subs = new CompositeDisposable()

    // Listen for Escape to return to editor
    subs.Add(
      eventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.State(Input(RawStateChanged struct (eId, rawState))) when
          eId = playerId
          && rawState.Keyboard.IsKeyDown(
            Microsoft.Xna.Framework.Input.Keys.Escape
          )
          ->
          Some()
        | _ -> None)
      |> Observable.subscribe(fun () ->
        sceneTransitionSubject.OnNext(MapEditor(ValueSome blockMap.Key)))
    )

    subs.Add(
      eventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Scene(SceneEvent.Transition t) -> Some t
        | _ -> None)
      |> Observable.subscribe(fun event ->
        sceneTransitionSubject.OnNext event.Scene)
    )

    subs.Add(actionHandler.StartListening())
    subs.Add(navigation3DService.StartListening())
    subs.Add(targetingService.StartListening())
    subs.Add(effectApplication.StartListening())
    subs.Add(inventoryService.StartListening())
    subs.Add(equipmentService.StartListening())

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

    // World update component
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

    // State flush component
    let stateWriteFlushComponent =
      { new GameComponent(game, UpdateOrder = 1000) with
          override _.Update(_) =
            stateWriteService.FlushWrites()
            physicsCacheService.RefreshAllCaches()
      }

    baseComponents.Add(hoverFeedbackSystem)
    baseComponents.Add(worldUpdateComponent)
    baseComponents.Add(hudDrawComponent)
    baseComponents.Add(renderOrchestrator)
    baseComponents.Add(stateWriteFlushComponent)

    let allComponents = baseComponents |> Seq.toList

    let disposable =
      { new IDisposable with
          member _.Dispose() =
            subs.Dispose()
            (eventBus :> IDisposable).Dispose()
            stateWriteService.Dispose()
            hudDesktop |> ValueOption.iter(fun d -> d.Dispose())
      }

    struct (allComponents, disposable)

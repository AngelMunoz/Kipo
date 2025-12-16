namespace Pomo.Core

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open FSharp.Control.Reactive
open Myra.Graphics2D.UI

open Pomo.Core.EventBus
open Pomo.Core.Domain
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Map
open Pomo.Core.Stores
open Pomo.Core.Scenes
open Pomo.Core.Environment

// System Imports
open Pomo.Core.Systems
open Pomo.Core.Systems.Effects
open Pomo.Core.Systems.StateUpdate
open Pomo.Core.Systems.Movement
open Pomo.Core.Systems.RawInput
open Pomo.Core.Systems.InputMapping
open Pomo.Core.Systems.PlayerMovement
open Pomo.Core.Systems.AbilityActivation
open Pomo.Core.Systems.UnitMovement
open Pomo.Core.Systems.Combat
open Pomo.Core.Systems.Notification
open Pomo.Core.Systems.Projectile
open Pomo.Core.Systems.DebugRender
open Pomo.Core.Systems.ResourceManager
open Pomo.Core.Systems.EntitySpawnerLogic

open Pomo.Core.Domain.Scenes

type GlobalScope = {
  Stores: StoreServices
  MonoGame: MonoGameServices
  Random: Random
  UIService: IUIService
}


module CompositionRoot =

  let sceneTransitionSubject = Subject<Scene>.broadcast

  let createGlobalScope(game: Game) =
    let deserializer = Serialization.create()
    let skillStore = Skill.create(JsonFileLoader.readSkills deserializer)
    let itemStore = Item.create(JsonFileLoader.readItems deserializer)

    let aiArchetypeStore =
      AIArchetype.create(JsonFileLoader.readAIArchetypes deserializer)

    let aiFamilyStore =
      AIFamily.create(JsonFileLoader.readAIFamilies deserializer)

    let aiEntityStore = AIEntity.create(JsonFileLoader.readAIEntities)

    let decisionTreeStore =
      DecisionTree.create(JsonFileLoader.readDecisionTrees)

    let mapStore =
      Map.create MapLoader.loadMap [
        "Content/Maps/Proto.xml"
        "Content/Maps/Lobby.xml"
      ]

    let modelStore = Model.create(JsonFileLoader.readModels deserializer)

    let animationStore =
      Animation.create(JsonFileLoader.readAnimations deserializer)

    let particleStore =
      Particle.create(JsonFileLoader.readParticles deserializer)

    let stores =
      { new StoreServices with
          member _.SkillStore = skillStore
          member _.ItemStore = itemStore
          member _.MapStore = mapStore
          member _.AIArchetypeStore = aiArchetypeStore
          member _.AIFamilyStore = aiFamilyStore
          member _.AIEntityStore = aiEntityStore
          member _.DecisionTreeStore = decisionTreeStore
          member _.ModelStore = modelStore
          member _.AnimationStore = animationStore
          member _.ParticleStore = particleStore
      }

    let monoGame =
      { new MonoGameServices with
          member _.GraphicsDevice = game.GraphicsDevice
          member _.Content = game.Content
      }

    let uiService = UIService.create()

    {
      Stores = stores
      MonoGame = monoGame
      Random = Random.Shared
      UIService = uiService
    }

  module SceneFactory =
    open Pomo.Core.Systems.Collision
    open System.Reactive.Disposables

    let createGameplay
      (game: Game)
      (scope: GlobalScope)
      (playerId: Guid<EntityId>)
      (initialMapKey: string)
      (initialTargetSpawn: string voption)
      =
      // Mutable state for the GameplayScene instance
      let mutable currentMapKey: string voption = ValueNone

      // 1. Create World and Local EventBus
      let eventBus = new EventBus()
      let struct (mutableWorld, worldView) = World.create scope.Random

      // 2. Create Gameplay Services
      let projections = Projections.create(scope.Stores.ItemStore, worldView)

      let cameraService =
        CameraSystem.create(
          game,
          projections,
          worldView,
          Array.singleton playerId
        )

      let targetingService =
        Targeting.create(eventBus, scope.Stores.SkillStore, projections)

      // 3. Create Listeners
      let effectApplication = EffectApplication.create(worldView, eventBus)

      let actionHandler =
        ActionHandler.create(
          worldView,
          eventBus,
          targetingService,
          projections,
          cameraService,
          playerId
        )

      let navigationService =
        Navigation.create(eventBus, scope.Stores.MapStore, worldView)

      let inventoryService =
        Inventory.create(eventBus, scope.Stores.ItemStore, worldView)

      let equipmentService = Equipment.create worldView eventBus

      // 4. Construct PomoEnvironment (Local Scope)
      let pomoEnv =
        { new Pomo.Core.Environment.PomoEnvironment with
            member _.CoreServices =
              { new CoreServices with
                  member _.EventBus = eventBus
                  member _.World = worldView
                  member _.Random = scope.Random
                  member _.UIService = scope.UIService
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

            member _.MonoGameServices = scope.MonoGame
            member _.StoreServices = scope.Stores
        }

      // Systems that are always present
      let baseComponents = new ResizeArray<IGameComponent>()
      baseComponents.Add(new RawInputSystem(game, pomoEnv, playerId))
      baseComponents.Add(new InputMappingSystem(game, pomoEnv, playerId))
      baseComponents.Add(new PlayerMovementSystem(game, pomoEnv, playerId))
      baseComponents.Add(new UnitMovementSystem(game, pomoEnv, playerId))
      baseComponents.Add(new AbilityActivationSystem(game, pomoEnv, playerId))
      baseComponents.Add(new CombatSystem(game, pomoEnv))
      baseComponents.Add(new ResourceManagerSystem(game, pomoEnv))
      baseComponents.Add(new ProjectileSystem(game, pomoEnv))
      baseComponents.Add(new MovementSystem(game, pomoEnv))
      baseComponents.Add(new CollisionSystem(game, pomoEnv))

      baseComponents.Add(
        new NotificationSystem(game, pomoEnv, DrawOrder = Render.Layer.UI)
      )

      baseComponents.Add(new EffectProcessingSystem(game, pomoEnv))
      baseComponents.Add(new EntitySpawnerSystem(game, pomoEnv))
      baseComponents.Add(new AISystem(game, pomoEnv))
      baseComponents.Add(new AnimationSystem(game, pomoEnv))
      baseComponents.Add(new ParticleSystem.ParticleSystem(game, pomoEnv))
      baseComponents.Add(new MotionStateAnimationSystem(game, pomoEnv))
      baseComponents.Add(new StateUpdateSystem(game, pomoEnv, mutableWorld))

      // Map Dependent Systems (Renderers)
      let mapDependentComponents = new ResizeArray<IGameComponent>()

      let clearCurrentMapData() =
        eventBus.Publish(EntityLifecycle(Removed playerId))

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

        let candidates = MapSpawning.extractSpawnCandidates mapDef scope.Random

        let playerPos =
          MapSpawning.findPlayerSpawnPosition targetSpawn candidates

        // Spawn player
        let playerIntent: SystemCommunications.SpawnEntityIntent = {
          EntityId = spawnPlayerId
          ScenarioId = scenarioId
          Type = SystemCommunications.SpawnType.Player 0
          Position = playerPos
        }

        eventBus.Publish playerIntent

        // Spawn enemies
        let mutable enemyCount = 0

        let enemyCandidates =
          candidates |> Seq.filter(fun c -> not c.IsPlayerSpawn)

        for candidate in enemyCandidates do
          if enemyCount < maxEnemies then
            let enemyId = Guid.NewGuid() |> UMX.tag

            let entityDefKey, archetypeId, mapOverride =
              match candidate.EntityGroup with
              | ValueSome groupName ->
                MapSpawning.resolveEntityFromGroup
                  scope.Random
                  mapEntityGroupStore
                  scope.Stores.AIEntityStore
                  groupName
              | ValueNone ->
                let id = if enemyCount % 2 = 0 then %1 else %2
                ValueNone, id, ValueNone

            let enemyIntent: SystemCommunications.SpawnEntityIntent = {
              EntityId = enemyId
              ScenarioId = scenarioId
              Type =
                SystemCommunications.SpawnType.Enemy(
                  archetypeId,
                  entityDefKey,
                  mapOverride
                )
              Position = candidate.Position
            }

            eventBus.Publish enemyIntent
            enemyCount <- enemyCount + 1

      // TODO: MapDependentSystems should be monitoring the
      // ScenarioState which should include the map data
      // This is a small temporary workaround to allow the game to run.
      let loadMap (newMapKey: string) (targetSpawn: string voption) =
        // Dispose and clear map-dependent systems
        // Note: In scene-based approach, we don't need to remove from game.Components here
        // because we are building the initial component list.
        // But if we reused this function for dynamic switching, we would.
        // Since we are only using it for initial load now, we can simplify.

        mapDependentComponents.Clear()
        clearCurrentMapData()

        let mapDef = scope.Stores.MapStore.find newMapKey

        // Create Scenario
        let scenarioId = Guid.NewGuid() |> UMX.tag<ScenarioId>
        let scenario: World.Scenario = { Id = scenarioId; Map = mapDef }
        mutableWorld.Scenarios[scenarioId] <- scenario

        // Recreate Renderers with new map key
        let renderOrchestrator =
          new RenderOrchestratorSystem.RenderOrchestratorSystem(
            game,
            pomoEnv,
            newMapKey,
            playerId,
            DrawOrder = Render.Layer.TerrainBase
          )

        mapDependentComponents.Add(renderOrchestrator)

        let debugRender =
          new DebugRenderSystem(
            game,
            pomoEnv,
            playerId,
            newMapKey,
            DrawOrder = Render.Layer.Debug
          )

        mapDependentComponents.Add(debugRender)

        spawnEntitiesForMap mapDef playerId scenarioId targetSpawn

      // UI for Gameplay (HUD)
      let mutable hudDesktop: Desktop voption = ValueNone

      let publishHudGuiAction(action: GuiAction) =
        match action with
        | GuiAction.BackToMainMenu -> sceneTransitionSubject.OnNext MainMenu
        | _ -> ()

      // 6. Setup Listeners (Subs)
      let subs = new System.Reactive.Disposables.CompositeDisposable()

      // Bridge EventBus to SceneTransitionSubject
      subs.Add(
        eventBus.GetObservableFor<SystemCommunications.SceneTransition>()
        |> Observable.subscribe(fun event ->
          sceneTransitionSubject.OnNext event.Scene)
      )

      // Initialize
      // Setup Listeners
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
        eventBus.GetObservableFor<SystemCommunications.PortalTravel>()
        |> Observable.subscribe(fun event ->
          if event.EntityId = playerId then
            // Trigger Scene Transition instead of local loadMap
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

              hudDesktop
              |> ValueOption.iter(fun d ->
                scope.UIService.SetMouseOverUI d.IsMouseOverGUI)
        }

      let hudDrawComponent =
        { new DrawableGameComponent(game) with
            override _.LoadContent() =
              let root = Systems.GameplayUI.build game publishHudGuiAction
              hudDesktop <- ValueSome(new Desktop(Root = root))

            override _.Draw(gameTime) =
              hudDesktop |> ValueOption.iter(fun d -> d.Render())
        }

      baseComponents.Add(worldUpdateComponent)
      baseComponents.Add(hudDrawComponent)

      let allComponents = [
        yield! baseComponents
        yield! mapDependentComponents
      ]

      let disposable =
        { new IDisposable with
            member _.Dispose() =
              subs.Dispose()
              hudDesktop |> ValueOption.iter(fun d -> d.Dispose())
              // Cleanup map dependent components
              for c in mapDependentComponents do
                match c with
                | :? IDisposable as d -> d.Dispose()
                | _ -> ()

              mapDependentComponents.Clear()
        }

      struct (allComponents, disposable)

    let createMainMenu (game: Game) (scope: GlobalScope) =
      let mutable desktop: Desktop voption = ValueNone
      let subs = new CompositeDisposable()

      let publishGuiAction(action: GuiAction) =
        match action with
        | StartNewGame ->
          sceneTransitionSubject.OnNext(Gameplay("Lobby", ValueNone))
        | OpenSettings -> () // TODO: Implement settings menu
        | ExitGame -> game.Exit()
        | BackToMainMenu -> () // Should not happen in MainMenu

      let uiComponent =
        { new DrawableGameComponent(game) with
            override _.LoadContent() =
              let root = MainMenuUI.build game publishGuiAction
              desktop <- ValueSome(new Desktop(Root = root))

            override _.Update(gameTime) =
              desktop
              |> ValueOption.iter(fun d ->
                scope.UIService.SetMouseOverUI d.IsMouseOverGUI)

            override _.Draw(gameTime) =
              desktop |> ValueOption.iter(fun d -> d.Render())
        }

      let disposable =
        { new IDisposable with
            member _.Dispose() =
              subs.Dispose()
              desktop |> ValueOption.iter(fun d -> d.Dispose())
        }

      struct ([ uiComponent :> IGameComponent ], disposable)

    let sceneLoader
      (game: Game)
      (scope: GlobalScope)
      (playerId: Guid<EntityId>)
      (scene: Scene)
      =
      match scene with
      | MainMenu -> createMainMenu game scope
      | Gameplay(mapKey, targetSpawn) ->
        createGameplay game scope playerId mapKey targetSpawn

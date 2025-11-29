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

    let mapStore =
      Map.create MapLoader.loadMap [
        "Content/Maps/Proto.xml"
        "Content/Maps/Lobby.xml"
      ]

    let stores =
      { new StoreServices with
          member _.SkillStore = skillStore
          member _.ItemStore = itemStore
          member _.MapStore = mapStore
          member _.AIArchetypeStore = aiArchetypeStore
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
      =
      // Mutable state for the GameplayScene instance
      let mutable currentMapKey: string voption = ValueNone

      // 1. Create World and Local EventBus
      let eventBus = new EventBus()
      let struct (mutableWorld, worldView) = World.create scope.Random

      // 2. Create Gameplay Services
      let projections = Projections.create(scope.Stores.ItemStore, worldView)

      let cameraService =
        CameraSystem.create(game, projections, Array.singleton playerId)

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
      baseComponents.Add(new CollisionSystem(game, pomoEnv))
      baseComponents.Add(new MovementSystem(game, pomoEnv))

      baseComponents.Add(
        new NotificationSystem(game, pomoEnv, DrawOrder = Render.Layer.UI)
      )

      baseComponents.Add(new EffectProcessingSystem(game, pomoEnv))
      baseComponents.Add(new EntitySpawnerSystem(game, pomoEnv))
      baseComponents.Add(new AISystem(game, pomoEnv))
      baseComponents.Add(new StateUpdateSystem(game, pomoEnv, mutableWorld))

      // Map Dependent Systems (Renderers)
      let mapDependentComponents = new ResizeArray<IGameComponent>()

      let clearCurrentMapData() =
        eventBus.Publish(EntityLifecycle(Removed playerId))

      let getRandomPointInPolygon (poly: IndexList<Vector2>) (random: Random) =
        if poly.IsEmpty then
          Vector2.Zero
        else
          let minX = poly |> IndexList.map(fun v -> v.X) |> Seq.min
          let maxX = poly |> IndexList.map(fun v -> v.X) |> Seq.max
          let minY = poly |> IndexList.map(fun v -> v.Y) |> Seq.min
          let maxY = poly |> IndexList.map(fun v -> v.Y) |> Seq.max

          let isPointInPolygon(p: Vector2) =
            let mutable inside = false
            let count = poly.Count
            let mutable j = count - 1

            for i = 0 to count - 1 do
              let pi = poly[i]
              let pj = poly[j]

              if
                ((pi.Y > p.Y) <> (pj.Y > p.Y))
                && (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
              then
                inside <- not inside

              j <- i

            inside

          let rec findPoint attempts =
            if attempts <= 0 then
              Vector2((minX + maxX) / 2.0f, (minY + maxY) / 2.0f)
            else
              let x = minX + float32(random.NextDouble()) * (maxX - minX)
              let y = minY + float32(random.NextDouble()) * (maxY - minY)
              let p = Vector2(x, y)
              if isPointInPolygon p then p else findPoint(attempts - 1)

          findPoint 20

      let spawnEntitiesForMap
        (mapDef: Map.MapDefinition)
        (spawnPlayerId: Guid<EntityId>)
        (scenarioId: Guid<ScenarioId>)
        =
        let mutable playerSpawned = false
        let mutable enemyCount = 0

        let maxEnemies =
          mapDef.Properties
          |> HashMap.tryFind "MaxEnemyEntities"
          |> Option.bind(fun v ->
            match System.Int32.TryParse v with
            | true, i -> Some i
            | _ -> None)
          |> Option.defaultValue 0

        let spawnCandidates =
          mapDef.ObjectGroups
          |> IndexList.collect(fun group ->
            group.Objects
            |> IndexList.choose(fun obj ->
              match obj.Type with
              | ValueSome MapObjectType.Spawn ->
                let isPlayerSpawn =
                  obj.Properties
                  |> HashMap.tryFindV "PlayerSpawn"
                  |> ValueOption.map(fun v -> v.ToLower() = "true")
                  |> ValueOption.defaultValue false

                let pos =
                  match obj.Points with
                  | ValueSome points when not points.IsEmpty ->
                    let offset = getRandomPointInPolygon points scope.Random
                    Vector2(obj.X + offset.X, obj.Y + offset.Y)
                  | _ -> Vector2(obj.X, obj.Y)

                Some(isPlayerSpawn, pos)
              | _ -> None))

        // Prioritize explicit player spawn, otherwise take the first spawn found
        let playerSpawnPos =
          spawnCandidates
          |> IndexList.tryFind(fun _ (isPlayer, _) -> isPlayer)
          |> Option.orElse(
            spawnCandidates
            |> IndexList.tryAt 0
            |> Option.map(fun (isPlayer, pos) -> (isPlayer, pos))
          )
          |> Option.map snd // Get just the position
          |> Option.defaultValue Vector2.Zero // Fallback to 0,0 if no spawn points defined

        // Spawn player
        let playerIntent: SystemCommunications.SpawnEntityIntent = {
          EntityId = spawnPlayerId
          ScenarioId = scenarioId
          Type = SystemCommunications.SpawnType.Player 0
          Position = playerSpawnPos
        }

        eventBus.Publish playerIntent
        playerSpawned <- true

        // Spawn enemies
        let enemySpawnCandidates =
          spawnCandidates |> IndexList.filter(fun (isPlayer, _) -> not isPlayer)

        for (isPlayer, pos) in enemySpawnCandidates do
          if enemyCount < maxEnemies then
            let enemyId = Guid.NewGuid() |> UMX.tag
            let archetypeId = if enemyCount % 2 = 0 then %1 else %2

            let enemyIntent: SystemCommunications.SpawnEntityIntent = {
              EntityId = enemyId
              ScenarioId = scenarioId
              Type = SystemCommunications.SpawnType.Enemy archetypeId
              Position = pos
            }

            eventBus.Publish enemyIntent
            enemyCount <- enemyCount + 1

      // TODO: MapDependentSystems should be monitoring the
      // ScenarioState which should include the map data
      // This is a small temporary workaround to allow the game to run.
      let loadMap(newMapKey: string) =
        currentMapKey <- ValueSome newMapKey

        // Dispose and clear map-dependent systems
        for c in mapDependentComponents do
          game.Components.Remove(c) |> ignore

          match c with
          | :? IDisposable as d -> d.Dispose()
          | _ -> ()

        mapDependentComponents.Clear()

        clearCurrentMapData()

        let mapDef = scope.Stores.MapStore.find newMapKey

        // Create Scenario
        let scenarioId = Guid.NewGuid() |> UMX.tag<ScenarioId>
        let scenario: World.Scenario = { Id = scenarioId; Map = mapDef }
        mutableWorld.Scenarios[scenarioId] <- scenario

        // Recreate Renderers with new map key
        mapDependentComponents.Add(
          new RenderOrchestratorSystem.RenderOrchestratorSystem(
            game,
            pomoEnv,
            newMapKey,
            playerId,
            DrawOrder = Render.Layer.TerrainBase
          )
        )

        mapDependentComponents.Add(
          new DebugRenderSystem(
            game,
            pomoEnv,
            playerId,
            newMapKey,
            DrawOrder = Render.Layer.Debug
          )
        )

        spawnEntitiesForMap mapDef playerId scenarioId

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
      loadMap initialMapKey

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
              ()
        }

      struct (allComponents, disposable)

    let createMainMenu (game: Game) (scope: GlobalScope) =
      let mutable desktop: Desktop voption = ValueNone
      let subs = new CompositeDisposable()

      let publishGuiAction(action: GuiAction) =
        match action with
        | StartNewGame -> sceneTransitionSubject.OnNext(Gameplay "Lobby")
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
    | MainMenu -> SceneFactory.createMainMenu game scope
    | Gameplay mapKey -> SceneFactory.createGameplay game scope playerId mapKey

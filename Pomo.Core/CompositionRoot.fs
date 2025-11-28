namespace Pomo.Core

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Content
open FSharp.UMX
open FSharp.Data.Adaptive
open FSharp.Control.Reactive // For Subject and Observable
open Myra
open Myra.Graphics2D
open Myra.Graphics2D.UI

open Pomo.Core.EventBus
open Pomo.Core.Domain
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Camera
open Pomo.Core.Domain.Map
open Pomo.Core.Stores
open Pomo.Core.Projections
open Pomo.Core.Serialization
open Pomo.Core.Scenes
open Pomo.Core.Environment

// System Imports
open Pomo.Core.Systems
open Pomo.Core.Systems.Targeting
open Pomo.Core.Systems.Effects
open Pomo.Core.Systems.StateUpdate
open Pomo.Core.Systems.Movement
open Pomo.Core.Systems.Render
open Pomo.Core.Systems.RawInput
open Pomo.Core.Systems.InputMapping
open Pomo.Core.Systems.PlayerMovement
open Pomo.Core.Systems.Navigation
open Pomo.Core.Systems.AbilityActivation
open Pomo.Core.Systems.UnitMovement
open Pomo.Core.Systems.Combat
open Pomo.Core.Systems.Notification
open Pomo.Core.Systems.Projectile
open Pomo.Core.Systems.ActionHandler
open Pomo.Core.Systems.DebugRender
open Pomo.Core.Systems.ResourceManager
open Pomo.Core.Systems.Inventory
open Pomo.Core.Systems.Equipment
open Pomo.Core.Systems.TerrainRenderSystem
open Pomo.Core.Systems.EntitySpawnerLogic
open Pomo.Core.Systems.Collision


type GlobalScope = {
  Stores: StoreServices
  MonoGame: MonoGameServices
  Random: Random
  UIService: IUIService // UIService is effectively global (toast notifications etc)
}

type SceneType =
  | MainMenu
  | Gameplay of selectedMap: string

module CompositionRoot =

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

    /// <summary>
    /// Creates the main Gameplay scene.
    /// </summary>
    let createGameplay
      (game: Game)
      (scope: GlobalScope)
      (playerId: Guid<EntityId>)
      (initialMapKey: string)
      (transition: SceneType -> unit)
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
        Navigation.create(eventBus, scope.Stores.MapStore, "Lobby", worldView) // Initialized with Lobby, will be updated

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
      let baseComponents = new SceneComponentCollection()
      baseComponents.Add(new RawInputSystem(game, pomoEnv, playerId))
      baseComponents.Add(new InputMappingSystem(game, pomoEnv, playerId))
      baseComponents.Add(new PlayerMovementSystem(game, pomoEnv, playerId))
      baseComponents.Add(new UnitMovementSystem(game, pomoEnv, playerId))
      baseComponents.Add(new AbilityActivationSystem(game, pomoEnv, playerId))
      baseComponents.Add(new CombatSystem(game, pomoEnv))
      baseComponents.Add(new ResourceManagerSystem(game, pomoEnv))
      baseComponents.Add(new ProjectileSystem(game, pomoEnv))
      baseComponents.Add(new CollisionSystem(game, pomoEnv, "Lobby")) // Will be recreated or updated? CollisionSystem takes mapKey in constructor.
      baseComponents.Add(new MovementSystem(game, pomoEnv))

      let notificationSys = new NotificationSystem(game, pomoEnv)
      notificationSys.DrawOrder <- Render.Layer.UI
      baseComponents.Add(notificationSys)

      baseComponents.Add(new EffectProcessingSystem(game, pomoEnv))
      baseComponents.Add(new EntitySpawnerSystem(game, pomoEnv))
      baseComponents.Add(new AISystem(game, pomoEnv))
      baseComponents.Add(new StateUpdateSystem(game, pomoEnv, mutableWorld))

      // Map Dependent Systems (Renderers)
      let mapDependentComponents = new SceneComponentCollection()

      let clearCurrentMapData() =
        transact(fun () ->
          // Remove all entities except potentially player if we want persistence, but here we clear everything for simplicity of spawn logic re-run
          // Ideally we keep player data but reset position.
          mutableWorld.Positions.Clear()
          mutableWorld.Velocities.Clear()
          mutableWorld.MovementStates.Clear()
          mutableWorld.RawInputStates.Clear()
          mutableWorld.InputMaps.Clear()
          mutableWorld.GameActionStates.Clear()
          mutableWorld.ActionSets.Clear()
          mutableWorld.ActiveActionSets.Clear()
          mutableWorld.Resources.Clear()
          mutableWorld.Factions.Clear()
          mutableWorld.BaseStats.Clear()
          mutableWorld.DerivedStats.Clear()
          mutableWorld.ActiveEffects.Clear()
          mutableWorld.AbilityCooldowns.Clear()
          mutableWorld.LiveProjectiles.Clear()
          mutableWorld.InCombatUntil.Clear()
          mutableWorld.PendingSkillCast.Clear()
          mutableWorld.ItemInstances.Clear()
          mutableWorld.EntityInventories.Clear()
          mutableWorld.EquippedItems.Clear()
          mutableWorld.AIControllers.Clear()
          mutableWorld.SpawningEntities.Clear())

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
          |> Option.defaultValue 5

        for group in mapDef.ObjectGroups do
          for obj in group.Objects do
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

              if isPlayerSpawn && not playerSpawned then
                let intent: SystemCommunications.SpawnEntityIntent = {
                  EntityId = spawnPlayerId
                  Type = SystemCommunications.SpawnType.Player 0
                  Position = pos
                }

                eventBus.Publish intent
                playerSpawned <- true
              elif not isPlayerSpawn && enemyCount < maxEnemies then
                let enemyId = Guid.NewGuid() |> UMX.tag
                let archetypeId = if enemyCount % 2 = 0 then %1 else %2

                let intent: SystemCommunications.SpawnEntityIntent = {
                  EntityId = enemyId
                  Type = SystemCommunications.SpawnType.Enemy archetypeId
                  Position = pos
                }

                eventBus.Publish intent
                enemyCount <- enemyCount + 1
            | _ -> ()

        if not playerSpawned then
          let intent: SystemCommunications.SpawnEntityIntent = {
            EntityId = spawnPlayerId
            Type = SystemCommunications.SpawnType.Player 0
            Position = Vector2.Zero
          }

          eventBus.Publish intent

      let loadMap(newMapKey: string) =
        currentMapKey <- ValueSome newMapKey

        // Dispose and clear map-dependent systems
        mapDependentComponents.Dispose()
        // mapDependentComponents.Clear() is implied by logic re-adding them, but Dispose just calls Dispose on items.
        // We need to clear the list to avoid duplicate disposed systems.
        // Note: SceneComponentCollection needs a Clear method if not present? It has one.

        clearCurrentMapData()

        let mapDef = scope.Stores.MapStore.find newMapKey

        // Recreate Renderers with new map key
        let newRenderOrch =
          new RenderOrchestratorSystem.RenderOrchestratorSystem(
            game,
            pomoEnv,
            newMapKey,
            playerId
          )

        newRenderOrch.DrawOrder <- Render.Layer.TerrainBase
        mapDependentComponents.Add(newRenderOrch)

        let newDebugRender =
          new DebugRenderSystem(game, pomoEnv, playerId, newMapKey)

        newDebugRender.DrawOrder <- Render.Layer.Debug
        mapDependentComponents.Add(newDebugRender)

        mapDependentComponents.Sort()
        mapDependentComponents.Initialize()

        spawnEntitiesForMap mapDef playerId

      // UI for Gameplay (HUD)
      let hudDesktop = new Desktop()

      let publishHudGuiAction(action: GuiAction) =
        match action with
        | GuiAction.BackToMainMenu ->
          MyraEnvironment.Game <- null
          transition SceneType.MainMenu
        | _ -> ()

      // Sort components based on their Order properties
      baseComponents.Sort()

      // 6. Setup Listeners (Subs)
      let subs = new System.Reactive.Disposables.CompositeDisposable()

      // Return the Scene
      { new Scene() with
          override _.Initialize() =
            // Initialize Myra for HUD
            MyraEnvironment.Game <- game
            let root = Systems.GameplayUI.build game publishHudGuiAction
            hudDesktop.Root <- root

            // Initialize all base systems
            baseComponents.Initialize()

            // Setup Listeners
            subs.Add(actionHandler.StartListening())
            subs.Add(navigationService.StartListening())
            subs.Add(targetingService.StartListening())
            subs.Add(effectApplication.StartListening())
            subs.Add(inventoryService.StartListening())
            subs.Add(equipmentService.StartListening())

            // Load initial map
            loadMap initialMapKey

          override _.Update(gameTime) =
            // Update MutableWorld time
            transact(fun () ->
              let previous = mutableWorld.Time.Value.TotalGameTime

              mutableWorld.Time.Value <- {
                Delta = gameTime.ElapsedGameTime
                TotalGameTime = gameTime.TotalGameTime
                Previous = previous
              })

            // Update systems
            baseComponents.Update(gameTime)
            mapDependentComponents.Update(gameTime)

            // Update HUD
            scope.UIService.SetMouseOverUI hudDesktop.IsMouseOverGUI

          override _.Draw(gameTime) =
            baseComponents.Draw(gameTime)
            mapDependentComponents.Draw(gameTime)
            hudDesktop.Render()

          member _.LoadMap(mapKey: string) = loadMap mapKey

          override _.Dispose() =
            subs.Dispose()
            baseComponents.Dispose()
            mapDependentComponents.Dispose()
            hudDesktop.Dispose()
      }

    /// <summary>
    /// Creates a MainMenu scene.
    /// </summary>
    let createMainMenu
      (game: Game)
      (scope: GlobalScope)
      (transition: SceneType -> unit)
      =
      let desktop = new Desktop() // Desktop per scene
      let subs = new System.Reactive.Disposables.CompositeDisposable()

      let publishGuiAction(action: GuiAction) =
        match action with
        | GuiAction.StartNewGame ->
          MyraEnvironment.Game <- null // Clear Myra's reference to Game. The GameplayScene will re-set it.
          transition(SceneType.Gameplay "Lobby")
        | GuiAction.OpenSettings -> () // TODO: Implement settings menu
        | GuiAction.ExitGame -> game.Exit()
        | GuiAction.BackToMainMenu -> () // Should not happen in MainMenu

      { new Scene() with
          override _.Initialize() =
            MyraEnvironment.Game <- game // Myra needs the Game object. This is a bit global, but Myra is designed this way.
            let root = Systems.MainMenuUI.build game publishGuiAction
            desktop.Root <- root

          override _.Update(gameTime) =
            // Desktop updates automatically when MyraEnvironment.Game is set.
            // Only need to update IsMouseOverUI here.
            scope.UIService.SetMouseOverUI desktop.IsMouseOverGUI

          override _.Draw(gameTime) = desktop.Render()

          override _.Dispose() =
            subs.Dispose()
            desktop.Dispose()
      }

  module SceneCoordinator =

    /// <summary>
    /// Starts the game flow by handling scene transitions via an event stream.
    /// </summary>
    let start
      (game: Game)
      (scope: GlobalScope)
      (sceneManager: SceneManager)
      (playerId: Guid<EntityId>)
      =

      let transitionSubject = new System.Reactive.Subjects.Subject<SceneType>()

      transitionSubject
      |> Observable.subscribe(fun sceneType ->

        match sceneType with
        | SceneType.MainMenu ->
          let scene =
            SceneFactory.createMainMenu game scope transitionSubject.OnNext

          sceneManager.LoadScene(scene)
        | SceneType.Gameplay mapKey ->
          let scene =
            SceneFactory.createGameplay
              game
              scope
              playerId
              mapKey
              transitionSubject.OnNext

          sceneManager.LoadScene(scene))
      |> ignore // We should probably keep this subscription alive, but SceneManager logic handles the scene lifecycle. The coordinator is global.

      // Start by loading Main Menu
      transitionSubject.OnNext SceneType.MainMenu

      // Return subscription if we wanted to dispose the coordinator, but it lives for app lifetime.
      transitionSubject

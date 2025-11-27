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

  let struct (pomoEnv, mutableWorld) = CompositionRoot.create(this, playerId)

  let (Core core) = pomoEnv.CoreServices
  let (Stores stores) = pomoEnv.StoreServices
  let (Gameplay gameplay) = pomoEnv.GameplayServices
  let (Listeners listeners) = pomoEnv.ListenerServices

  do
    base.IsMouseVisible <- true
    base.Content.RootDirectory <- "Content"
    base.Window.AllowUserResizing <- true
    graphicsDeviceManager.PreferredBackBufferWidth <- 1280
    graphicsDeviceManager.PreferredBackBufferHeight <- 720

    graphicsDeviceManager.SupportedOrientations <-
      DisplayOrientation.LandscapeLeft ||| DisplayOrientation.LandscapeRight

    base.Services.AddService<GraphicsDeviceManager> graphicsDeviceManager

    base.Components.Add(new RawInputSystem(this, pomoEnv, playerId))
    base.Components.Add(new InputMappingSystem(this, pomoEnv, playerId))
    base.Components.Add(new PlayerMovementSystem(this, pomoEnv, playerId))
    base.Components.Add(new UnitMovementSystem(this, pomoEnv, playerId))
    base.Components.Add(new AbilityActivationSystem(this, pomoEnv, playerId))
    base.Components.Add(new CombatSystem(this, pomoEnv))
    base.Components.Add(new ResourceManagerSystem(this, pomoEnv))
    base.Components.Add(new ProjectileSystem(this, pomoEnv))
    base.Components.Add(new CollisionSystem(this, pomoEnv, "Lobby"))
    base.Components.Add(new MovementSystem(this, pomoEnv))

    base.Components.Add(
      new NotificationSystem(this, pomoEnv, DrawOrder = Render.Layer.UI)
    )

    base.Components.Add(new EffectProcessingSystem(this, pomoEnv))

    base.Components.Add(new EntitySpawnerSystem(this, pomoEnv))

    base.Components.Add(
      new RenderOrchestratorSystem.RenderOrchestratorSystem(
        this,
        pomoEnv,
        "Lobby",
        playerId,
        DrawOrder = Render.Layer.TerrainBase
      )
    )

    base.Components.Add(
      new DebugRenderSystem(
        this,
        pomoEnv,
        playerId,
        "Lobby",
        DrawOrder = Render.Layer.Debug
      )
    )

    base.Components.Add(new AISystem(this, pomoEnv))

    base.Components.Add(new UISystem(this, pomoEnv, playerId, DrawOrder = 1000)) // Ensure UI is on top

    base.Components.Add(new StateUpdateSystem(this, pomoEnv, mutableWorld))


  override _.Initialize() =
    base.Initialize()

    LocalizationManager.DefaultCultureCode |> LocalizationManager.SetCulture

    // --- Map Based Spawning ---
    let mapKey = "Lobby"
    let mapDef = stores.MapStore.find mapKey

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
    // for group in mapDef.ObjectGroups do
    //   for obj in group.Objects do
    //     match obj.Type with
    //     | ValueSome MapObjectType.Spawn ->
    //       // Check for Player Spawn
    //       let isPlayerSpawn =
    //         obj.Properties
    //         |> HashMap.tryFind "PlayerSpawn"
    //         |> Option.map(fun v -> v.ToLower() = "true")
    //         |> Option.defaultValue false

    //       if isPlayerSpawn && not playerSpawned then
    //         // Spawn Player 1 here
    //         let intent: SystemCommunications.SpawnEntityIntent = {
    //           EntityId = playerId
    //           Type = SystemCommunications.SpawnType.Player 0
    //           Position = Vector2(obj.X, obj.Y)
    //         }

    //         core.EventBus.Publish intent
    //         playerSpawned <- true

    //       // Check for AI Spawn
    //       elif not isPlayerSpawn && enemyCount < maxEnemies then
    //         let enemyId = Guid.NewGuid() |> UMX.tag
    //         // Determine archetype (alternate between 1 and 2)
    //         let archetypeId = if enemyCount % 2 = 0 then %1 else %2

    //         let intent: SystemCommunications.SpawnEntityIntent = {
    //           EntityId = enemyId
    //           Type = SystemCommunications.SpawnType.Enemy archetypeId
    //           Position = Vector2(obj.X, obj.Y)
    //         }

    //         core.EventBus.Publish intent
    //         enemyCount <- enemyCount + 1

    //     | _ -> ()

    // Start listening to action events
    listeners.ActionHandler.StartListening() |> subs.Add
    listeners.NavigationService.StartListening() |> subs.Add
    gameplay.TargetingService.StartListening() |> subs.Add
    listeners.EffectApplication.StartListening() |> subs.Add
    listeners.InventoryService.StartListening() |> subs.Add
    listeners.EquipmentService.StartListening() |> subs.Add


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

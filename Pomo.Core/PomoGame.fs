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

type PomoGame() as this =
  inherit Game()

  let graphicsDeviceManager = new GraphicsDeviceManager(this)

  let subs = new System.Reactive.Disposables.CompositeDisposable()

  let deserializer = Serialization.create()

  let playerId = %Guid.NewGuid()
  let enemyId1 = %Guid.NewGuid()
  let enemyId2 = %Guid.NewGuid()
  let enemyId3 = %Guid.NewGuid()
  let enemyId4 = %Guid.NewGuid()
  let enemyId5 = %Guid.NewGuid()

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

  let targetingService =
    Targeting.create(worldView, eventBus, skillStore, projections)

  let effectApplicationService =
    Effects.EffectApplication.create(worldView, eventBus)

  let actionHandler =
    ActionHandler.create(
      worldView,
      eventBus,
      targetingService,
      projections,
      playerId
    )

  let movementService = Navigation.create(eventBus)
  let inventoryService = Inventory.create(eventBus, itemStore, worldView)
  let equipmentService = Equipment.create worldView eventBus


  do
    base.IsMouseVisible <- true
    base.Content.RootDirectory <- "Content"
    base.Window.AllowUserResizing <- true

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
    base.Components.Add(new NotificationSystem(this, eventBus))
    base.Components.Add(new EffectProcessingSystem(this))
    let terrainRenderSystem = new TerrainRenderSystem(this, "Proto1")
    base.Components.Add(terrainRenderSystem)
    base.Components.Add(new RenderSystem(this, playerId))

    let debugRenderSystem = new DebugRenderSystem(this, playerId, "Proto1")
    base.Components.Add(debugRenderSystem)

    base.Components.Add(
      new AISystem(this, worldView, eventBus, skillStore, aiArchetypeStore)
    )

    base.Components.Add(new StateUpdateSystem(this, mutableWorld))


  override _.Initialize() =
    base.Initialize()

    LocalizationManager.DefaultCultureCode |> LocalizationManager.SetCulture

    // Player Setup
    let playerEntity: Entity.EntitySnapshot = {
      Id = playerId
      Position = Vector2(100.0f, 100.0f)
      Velocity = Vector2.Zero
    }

    eventBus.Publish(EntityLifecycle(Created playerEntity))

    let playerBaseStats: Entity.BaseStats = {
      Power = 10
      Magic = 50
      Sense = 20
      Charm = 30
    }

    let maxPlayerHP = playerBaseStats.Charm * 10
    let maxPlayerMP = playerBaseStats.Magic * 5

    let playerResources: Entity.Resource = {
      HP = maxPlayerHP
      MP = maxPlayerMP
      Status = Entity.Status.Alive
    }

    let playerFactions = HashSet [ Entity.Faction.Player ]
    let inputMap = InputMapping.createDefaultInputMap()



    eventBus.Publish(
      StateChangeEvent.Combat(
        ResourcesChanged struct (playerId, playerResources)
      )
    )

    eventBus.Publish(
      StateChangeEvent.Combat(FactionsChanged struct (playerId, playerFactions))
    )

    eventBus.Publish(
      StateChangeEvent.Combat(
        BaseStatsChanged struct (playerId, playerBaseStats)
      )
    )

    eventBus.Publish(Input(MapChanged struct (playerId, inputMap)))

    // Equip starting items
    let wizardHat: Item.ItemInstance = {
      Item.InstanceId = Guid.NewGuid() |> UMX.tag
      ItemId = 4 |> UMX.tag
      UsesLeft = ValueNone
    }

    let magicStaff: Item.ItemInstance = {
      Item.InstanceId = Guid.NewGuid() |> UMX.tag
      ItemId = 5 |> UMX.tag
      UsesLeft = ValueNone
    }

    eventBus.Publish(Inventory(ItemInstanceCreated wizardHat))
    eventBus.Publish(Inventory(ItemInstanceCreated magicStaff))

    eventBus.Publish(
      Inventory(ItemAddedToInventory struct (playerId, wizardHat.InstanceId))
    )

    eventBus.Publish(
      Inventory(ItemAddedToInventory struct (playerId, magicStaff.InstanceId))
    )

    eventBus.Publish(
      Inventory(
        ItemEquipped struct (playerId, Item.Slot.Head, wizardHat.InstanceId)
      )
    )

    eventBus.Publish(
      Inventory(
        ItemEquipped struct (playerId, Item.Slot.Weapon, magicStaff.InstanceId)
      )
    )

    // Enemy Setup
    let enemyBaseStats: Entity.BaseStats = {
      Power = 2
      Magic = 2
      Sense = 2
      Charm = 200
    }

    let maxEnemyHP = enemyBaseStats.Charm * 10
    let maxEnemyMP = enemyBaseStats.Magic * 5

    let enemyResources: Entity.Resource = {
      HP = maxEnemyHP
      MP = maxEnemyMP
      Status = Entity.Status.Alive
    }

    let enemyFactions = HashSet [ Entity.Faction.Enemy ]

    let createEnemy (id: Guid<EntityId>) (pos: Vector2) =
      let enemyEntity: Entity.EntitySnapshot = {
        Id = id
        Position = pos
        Velocity = Vector2.Zero
      }

      eventBus.Publish(EntityLifecycle(Created enemyEntity))

      eventBus.Publish(
        StateChangeEvent.Combat(ResourcesChanged struct (id, enemyResources))
      )

      eventBus.Publish(
        StateChangeEvent.Combat(FactionsChanged struct (id, enemyFactions))
      )

      eventBus.Publish(
        StateChangeEvent.Combat(BaseStatsChanged struct (id, enemyBaseStats))
      )

    createEnemy enemyId1 (Vector2(300.0f, 100.0f))
    createEnemy enemyId2 (Vector2(350.0f, 150.0f))

    // AI Controller Setup
    let createAIController
      (entityId: Guid<EntityId>)
      (spawnPos: Vector2)
      (archetypeId: int<AiArchetypeId>)
      =
      let controller: AI.AIController = {
        controlledEntityId = entityId
        archetypeId = archetypeId
        currentState = AI.AIState.Idle
        stateEnterTime = TimeSpan.Zero
        spawnPosition = spawnPos
        absoluteWaypoints = ValueNone
        waypointIndex = 0
        lastDecisionTime = TimeSpan.Zero
        currentTarget = ValueNone
        memories = HashMap.empty
      }

      eventBus.Publish(
        StateChangeEvent.AI(
          AIStateChange.ControllerUpdated struct (entityId, controller)
        )
      )

    createAIController enemyId1 (Vector2(300.0f, 100.0f)) %1
    createAIController enemyId2 (Vector2(350.0f, 150.0f)) %2 // Patrolling Guard
    createAIController enemyId3 (Vector2(400.0f, 100.0f)) %1
    createAIController enemyId4 (Vector2(450.0f, 150.0f)) %1
    createAIController enemyId5 (Vector2(500.0f, 100.0f)) %1

    let potion: Item.ItemInstance = {
      InstanceId = %Guid.NewGuid()
      ItemId = %2
      UsesLeft = ValueSome 20
    }

    let trollBloodPotion: Item.ItemInstance = {
      InstanceId = %Guid.NewGuid()
      ItemId = %6
      UsesLeft = ValueSome 20
    }

    eventBus.Publish(
      Inventory(ItemAddedToInventory struct (playerId, potion.InstanceId))
    )

    eventBus.Publish(
      Inventory(
        ItemAddedToInventory struct (playerId, trollBloodPotion.InstanceId)
      )
    )

    eventBus.Publish(Inventory(ItemInstanceCreated potion))
    eventBus.Publish(Inventory(ItemInstanceCreated trollBloodPotion))


    let actionSet1 = [
      UseSlot1, Core.SlotProcessing.Skill %7 // Summon Boulder
      UseSlot2, Core.SlotProcessing.Skill %8 // Catchy Song
      UseSlot3, Core.SlotProcessing.Skill %3
      UseSlot4, Core.SlotProcessing.Skill %6
      UseSlot5, Core.SlotProcessing.Skill %4
      UseSlot6, Core.SlotProcessing.Skill %5
    ]

    let actionSet2 = [
      UseSlot1, Core.SlotProcessing.Item potion.InstanceId
      UseSlot2, Core.SlotProcessing.Item trollBloodPotion.InstanceId
    ]

    let actionSets = [
      1, HashMap.ofList actionSet1
      2, HashMap.ofList actionSet2
    ]

    eventBus.Publish(
      Input(ActionSetsChanged struct (playerId, HashMap.ofList actionSets))
    )

    eventBus.Publish(Input(ActiveActionSetChanged struct (playerId, 1)))

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

  override _.LoadContent() = base.LoadContent()

  // Load game content here
  // e.g., this.Content.Load<Texture2D>("textureName")

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

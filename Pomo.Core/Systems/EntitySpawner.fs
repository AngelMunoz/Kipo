namespace Pomo.Core.Systems

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.AI
open Pomo.Core.EventBus
open Pomo.Core.Stores
open Systems

module EntitySpawnerLogic =

  type PendingSpawn = {
    EntityId: Guid<EntityId>
    ScenarioId: Guid<ScenarioId>
    Type: SystemCommunications.SpawnType
    Position: Vector2
    SpawnStartTime: TimeSpan
    Duration: TimeSpan
  }

  // Helper to create default stats (similar to previous hardcoded values)
  let createPlayerStats() =
    let baseStats = {
      Power = 10
      Magic = 50
      Sense = 20
      Charm = 30
    }

    let resource = {
      HP = baseStats.Charm * 10
      MP = baseStats.Magic * 5
      Status = Status.Alive
    }

    baseStats, resource

  let createEnemyStats(archetype: AIArchetype) =
    let baseStats = archetype.baseStats

    let resource = {
      HP = baseStats.Charm * 10
      MP = baseStats.Magic * 5
      Status = Status.Alive
    }

    baseStats, resource

  let configurePlayerLoadout(entityId: Guid<EntityId>, eventBus: EventBus) =
    // Default Loadout (Hardcoded for now, moved from PomoGame)
    // TODO: Move this to a Loadout service or similar
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
      Inventory(ItemAddedToInventory struct (entityId, wizardHat.InstanceId))
    )

    eventBus.Publish(
      Inventory(ItemAddedToInventory struct (entityId, magicStaff.InstanceId))
    )

    eventBus.Publish(
      Inventory(
        ItemEquipped struct (entityId, Item.Slot.Head, wizardHat.InstanceId)
      )
    )

    eventBus.Publish(
      Inventory(
        ItemEquipped struct (entityId, Item.Slot.Weapon, magicStaff.InstanceId)
      )
    )

    // Default Skills
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
      Inventory(ItemAddedToInventory struct (entityId, potion.InstanceId))
    )

    eventBus.Publish(
      Inventory(
        ItemAddedToInventory struct (entityId, trollBloodPotion.InstanceId)
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

    let actionSet3 = [
      UseSlot1, Core.SlotProcessing.Skill %9 // Dragon's Breath
      UseSlot2, Core.SlotProcessing.Skill %10 // Railgun
      UseSlot3, Core.SlotProcessing.Skill %11 // Ice Shard
      UseSlot4, Core.SlotProcessing.Skill %12 // Piercing Bolt
      UseSlot5, Core.SlotProcessing.Skill %13 // Fan of Knives
    ]

    let actionSets = [
      1, HashMap.ofList actionSet1
      2, HashMap.ofList actionSet2
      3, HashMap.ofList actionSet3
    ]

    eventBus.Publish(
      Input(ActionSetsChanged struct (entityId, HashMap.ofList actionSets))
    )

    eventBus.Publish(Input(ActiveActionSetChanged struct (entityId, 3)))

  let finalizeSpawn
    (pending: PendingSpawn)
    (eventBus: EventBus)
    (aiArchetypeStore: AIArchetypeStore)
    =
    let entityId = pending.EntityId
    let pos = pending.Position

    let snapshot: EntitySnapshot = {
      Id = entityId
      ScenarioId = pending.ScenarioId
      Position = pos
      Velocity = Vector2.Zero
    }

    // 1. Create the entity in the world
    eventBus.Publish(EntityLifecycle(Created snapshot))

    match pending.Type with
    | SystemCommunications.SpawnType.Player playerIndex ->
      let baseStats, resource = createPlayerStats()
      let factions = HashSet [ Faction.Player ]

      eventBus.Publish(
        StateChangeEvent.Combat(ResourcesChanged struct (entityId, resource))
      )

      eventBus.Publish(
        StateChangeEvent.Combat(FactionsChanged struct (entityId, factions))
      )

      eventBus.Publish(
        StateChangeEvent.Combat(BaseStatsChanged struct (entityId, baseStats))
      )

      // Initialize Input Map
      let inputMap = InputMapping.createDefaultInputMap()
      eventBus.Publish(Input(MapChanged struct (entityId, inputMap)))

      configurePlayerLoadout(entityId, eventBus)


    | SystemCommunications.SpawnType.Enemy archetypeId ->
      let archetype = aiArchetypeStore.find archetypeId
      let baseStats, resource = createEnemyStats archetype
      let factions = HashSet [ Faction.Enemy ]

      eventBus.Publish(
        StateChangeEvent.Combat(ResourcesChanged struct (entityId, resource))
      )

      eventBus.Publish(
        StateChangeEvent.Combat(FactionsChanged struct (entityId, factions))
      )

      eventBus.Publish(
        StateChangeEvent.Combat(BaseStatsChanged struct (entityId, baseStats))
      )

      // Initialize AI Controller
      let controller: AIController = {
        controlledEntityId = entityId
        archetypeId = archetypeId
        currentState = AIState.Idle
        stateEnterTime = TimeSpan.Zero
        spawnPosition = pos
        absoluteWaypoints =
          if
            archetype.behaviorType = BehaviorType.Patrol
            || archetype.behaviorType = BehaviorType.Aggressive
          then
            // Generate a simple square patrol path relative to spawn
            let offset = 100.0f

            let waypoints = [|
              pos // Start at spawn
              pos + Vector2(offset, 0.0f)
              pos + Vector2(offset, offset)
              pos + Vector2(0.0f, offset)
            |]

            ValueSome waypoints
          else
            ValueNone
        waypointIndex = 0
        lastDecisionTime = TimeSpan.Zero
        currentTarget = ValueNone
        memories = HashMap.empty
      }

      eventBus.Publish(
        StateChangeEvent.AI(ControllerUpdated struct (entityId, controller))
      )

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type EntitySpawnerSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices

    let pendingSpawns = List<PendingSpawn>()
    let spawnDuration = Core.Constants.Spawning.DefaultDuration // Animation duration

    let subscriptions = new System.Reactive.Disposables.CompositeDisposable()

    do
      core.EventBus
        .GetObservableFor<SystemCommunications.SpawnEntityIntent>()
        .Subscribe(fun intent ->
          let totalGameTime =
            core.World.Time |> AVal.map _.TotalGameTime |> AVal.force

          let duration =
            match intent.Type with
            | SystemCommunications.SpawnType.Player _ -> TimeSpan.Zero
            | _ -> spawnDuration

          let pending: PendingSpawn = {
            EntityId = intent.EntityId
            ScenarioId = intent.ScenarioId
            Type = intent.Type
            Position = intent.Position
            SpawnStartTime = totalGameTime
            Duration = duration
          }

          pendingSpawns.Add(pending)

          // Notify that spawning has started (visuals can pick this up)
          core.EventBus.Publish(
            EntityLifecycle(
              Spawning
                struct (intent.EntityId,
                        intent.ScenarioId,
                        intent.Type,
                        intent.Position)
            )
          )

        )
      |> subscriptions.Add

    override _.Dispose(disposing) =
      if disposing then
        subscriptions.Dispose()

      base.Dispose(disposing)

    override _.Update(gameTime) =
      let totalGameTime =
        core.World.Time |> AVal.map _.TotalGameTime |> AVal.force

      if pendingSpawns.Count > 0 then
        let currentTime = totalGameTime
        let toRemove = List<PendingSpawn>()

        for pending in pendingSpawns do
          if currentTime >= pending.SpawnStartTime + pending.Duration then
            finalizeSpawn pending core.EventBus stores.AIArchetypeStore

            toRemove.Add(pending)

        for item in toRemove do
          pendingSpawns.Remove(item) |> ignore

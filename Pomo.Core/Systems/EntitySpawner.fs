namespace Pomo.Core.Systems

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.AI
open Pomo.Core.EventBus
open Pomo.Core.Stores
open Pomo.Core
open Systems
open Pomo.Core.Environment


module EntitySpawnerLogic =
  [<Struct>]
  type PendingSpawn = {
    EntityId: Guid<EntityId>
    ScenarioId: Guid<ScenarioId>
    Type: SystemCommunications.SpawnType
    Position: WorldPosition
    SpawnStartTime: TimeSpan
    Duration: TimeSpan
  }

  [<Struct>]
  type SpawnedEntityInfo = {
    EntityId: Guid<EntityId>
    ScenarioId: Guid<ScenarioId>
    SpawnInfo: SystemCommunications.FactionSpawnInfo
    SpawnZoneName: string voption
  }

  [<Struct>]
  type SpawnZoneConfig = {
    ZoneName: string
    MaxSpawns: int
    SpawnInfo: SystemCommunications.FactionSpawnInfo
    SpawnPositions: Vector2[]
  }

  [<Struct>]
  type ScenarioSpawnConfig = {
    ScenarioId: Guid<ScenarioId>
    MaxEnemies: int
    Zones: SpawnZoneConfig[]
  }

  /// Get a random spawn position from a zone
  let getRandomSpawnPosition (random: Random) (zone: SpawnZoneConfig) =
    zone.SpawnPositions[random.Next(zone.SpawnPositions.Length)]

  /// Get zones for a specific faction (use pre-grouped lookup instead for hot paths)
  let getZonesForFaction (faction: Faction) (config: ScenarioSpawnConfig) =
    config.Zones
    |> Array.filter(fun z ->
      match z.SpawnInfo.Faction with
      | ValueSome f -> f = faction
      | ValueNone -> false)

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

  /// Player loadout data returned for bundling
  [<Struct>]
  type PlayerLoadoutData = {
    Items: Item.ItemInstance[]
    EquippedSlots: struct (Item.Slot * Guid<ItemInstanceId>)[]
    ActionSets: HashMap<int, HashMap<GameAction, Core.SlotProcessing>>
    ActiveActionSet: int
  }

  let createPlayerLoadout() =
    // Equipment items
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

    // Consumable items
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

    // Action sets
    let actionSet1 = [
      UseSlot1, Core.SlotProcessing.Skill %7 // Summon Boulder
      UseSlot2, Core.SlotProcessing.Skill %8 // Catchy Song
      UseSlot3, Core.SlotProcessing.Skill %3
      UseSlot4, Core.SlotProcessing.Skill %2
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
      UseSlot6, Core.SlotProcessing.Skill %25 // Seraph Bombardment
    ]

    let actionSets = [
      1, HashMap.ofList actionSet1
      2, HashMap.ofList actionSet2
      3, HashMap.ofList actionSet3
    ]

    {
      Items = [| wizardHat; magicStaff; potion; trollBloodPotion |]
      EquippedSlots = [|
        struct (Item.Slot.Head, wizardHat.InstanceId)
        struct (Item.Slot.Weapon, magicStaff.InstanceId)
      |]
      ActionSets = HashMap.ofList actionSets
      ActiveActionSet = 3
    }

  let finalizeSpawn
    (pending: PendingSpawn)
    (aiArchetypeStore: AIArchetypeStore)
    (aiEntityStore: AIEntityStore)
    (aiFamilyStore: AIFamilyStore)
    (stateWrite: IStateWriteService)
    =
    let entityId = pending.EntityId
    let pos = pending.Position

    let snapshot: EntitySnapshot = {
      Id = entityId
      ScenarioId = pending.ScenarioId
      Position = pos
      Velocity = Vector2.Zero
    }

    match pending.Type with
    | SystemCommunications.SpawnType.Player _playerIndex ->
      let baseStats, resource = createPlayerStats()
      let factions = HashSet [ Faction.Player ]
      let inputMap = InputMapping.createDefaultInputMap()
      let loadout = createPlayerLoadout()

      let bundle: EntitySpawnBundle = {
        Snapshot = snapshot
        Resources = ValueSome resource
        Factions = ValueSome factions
        BaseStats = ValueSome baseStats
        ModelConfig = ValueSome "HumanoidBase"
        InputMap = ValueSome inputMap
        ActionSets = ValueSome loadout.ActionSets
        ActiveActionSet = ValueSome loadout.ActiveActionSet
        InventoryItems = ValueSome loadout.Items
        EquippedSlots = ValueSome loadout.EquippedSlots
        AIController = ValueNone
      }

      stateWrite.ApplyEntitySpawnBundle bundle

    | SystemCommunications.SpawnType.Faction info ->
      let archetype = aiArchetypeStore.find info.ArchetypeId

      // Use faction from spawn info, or default to Enemy
      let assignedFaction =
        info.Faction |> ValueOption.defaultValue Faction.Enemy

      let factions = HashSet [ assignedFaction ]

      // Look up entity definition
      let aiEntity: AIEntityDefinition option =
        match info.EntityDefinitionKey with
        | ValueSome key -> aiEntityStore.tryFind key |> Option.ofValueOption
        | ValueNone -> aiEntityStore.all() |> Seq.tryHead

      // Look up family config from entity's family
      let familyConfig: AIFamilyConfig option =
        aiEntity
        |> Option.bind(fun e ->
          let familyKey = e.Family.ToString()
          aiFamilyStore.tryFind familyKey |> Option.ofValueOption)

      // Apply full override chain for stats (including map override)
      let baseStats =
        archetype.baseStats
        |> MapSpawning.resolveStats familyConfig aiEntity info.MapOverride

      let resource = {
        HP = baseStats.Charm
        MP = baseStats.Magic
        Status = Status.Alive
      }

      // Resolve skills (with map override restrictions/extras)
      let skills =
        match aiEntity with
        | Some entity ->
          MapSpawning.resolveSkills entity.Skills info.MapOverride
        | None -> [||]

      let controller: AIController = {
        controlledEntityId = entityId
        archetypeId = info.ArchetypeId
        currentState = AIState.Idle
        stateEnterTime = TimeSpan.Zero
        spawnPosition = WorldPosition.toVector2 pos // AI uses XZ as 2D
        absoluteWaypoints =
          if
            archetype.behaviorType = BehaviorType.Patrol
            || archetype.behaviorType = BehaviorType.Aggressive
          then
            // Generate a simple square patrol path relative to spawn
            let offset = 100.0f
            let pos2D = WorldPosition.toVector2 pos

            let waypoints = [|
              pos2D // Start at spawn
              pos2D + Vector2(offset, 0.0f)
              pos2D + Vector2(offset, offset)
              pos2D + Vector2(0.0f, offset)
            |]

            ValueSome waypoints
          else
            ValueNone
        waypointIndex = 0
        lastDecisionTime = TimeSpan.Zero
        currentTarget = ValueNone
        decisionTree =
          aiEntity
          |> Option.map(fun e -> e.DecisionTree)
          |> Option.defaultValue "MeleeAttacker" // Default tree
        preferredIntent =
          familyConfig
          |> Option.map(fun f -> f.PreferredIntent)
          |> Option.defaultValue SkillIntent.Offensive // Default to offensive
        skills = skills
        memories = HashMap.empty
      }

      let modelConfig =
        aiEntity
        |> Option.map(fun e -> e.Model)
        |> Option.defaultValue "HumanoidBase"

      let bundle: EntitySpawnBundle = {
        Snapshot = snapshot
        Resources = ValueSome resource
        Factions = ValueSome factions
        BaseStats = ValueSome baseStats
        ModelConfig = ValueSome modelConfig
        InputMap = ValueNone
        ActionSets = ValueNone
        ActiveActionSet = ValueNone
        InventoryItems = ValueNone
        EquippedSlots = ValueNone
        AIController = ValueSome controller
      }

      stateWrite.ApplyEntitySpawnBundle bundle

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type EntitySpawnerSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices

    let pendingSpawns = List<PendingSpawn>()
    let spawnDuration = Core.Constants.Spawning.DefaultDuration // Animation duration

    // Respawn tracking: EntityId -> spawn info for respawning
    let spawnedEntities = Dictionary<Guid<EntityId>, SpawnedEntityInfo>()

    // Per-scenario spawn configuration (immutable)
    let scenarioConfigs = Dictionary<Guid<ScenarioId>, ScenarioSpawnConfig>()

    // Mutable count tracking (separate from config)
    let scenarioCurrentCounts = Dictionary<Guid<ScenarioId>, int>()

    let zoneCurrentCounts =
      Dictionary<struct (Guid<ScenarioId> * string), int>()

    // Pre-grouped zones by faction (avoids Array.filter on every death)
    let zonesByFaction =
      Dictionary<struct (Guid<ScenarioId> * Faction), SpawnZoneConfig[]>()

    let subscriptions = new System.Reactive.Disposables.CompositeDisposable()

    // Helper to get current zone count
    let getZoneCount (scenarioId: Guid<ScenarioId>) (zoneName: string) =
      match zoneCurrentCounts.TryGetValue(struct (scenarioId, zoneName)) with
      | true, count -> count
      | false, _ -> 0

    // Helper to get current scenario count
    let getScenarioCount(scenarioId: Guid<ScenarioId>) =
      match scenarioCurrentCounts.TryGetValue scenarioId with
      | true, count -> count
      | false, _ -> 0

    // Helper to find an available spawn zone for a faction (O(1) lookup)
    let findAvailableZone
      (scenarioId: Guid<ScenarioId>)
      (faction: Faction voption)
      =
      match scenarioConfigs.TryGetValue scenarioId with
      | true, config when getScenarioCount scenarioId < config.MaxEnemies ->
        // Use pre-grouped lookup for faction, or all zones if no faction
        let matchingZones =
          match faction with
          | ValueSome f ->
            match zonesByFaction.TryGetValue(struct (scenarioId, f)) with
            | true, zones -> zones
            | false, _ -> [||]
          | ValueNone -> config.Zones

        matchingZones
        |> Array.tryFind(fun z ->
          getZoneCount scenarioId z.ZoneName < z.MaxSpawns)
      | _ -> None

    do
      // Subscribe to RegisterSpawnZones to set up spawn zone tracking
      core.EventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Spawn(SpawningEvent.RegisterZones zones) -> Some zones
        | _ -> None)
      |> Observable.subscribe(fun event ->
        // Build immutable zone configs from event data
        let zones =
          event.Zones
          |> Array.map(fun zoneData ->
            let config: SpawnZoneConfig = {
              ZoneName = zoneData.ZoneName
              MaxSpawns = zoneData.MaxSpawns
              SpawnInfo = zoneData.SpawnInfo
              SpawnPositions = zoneData.SpawnPositions
            }

            // Initialize zone count
            zoneCurrentCounts[struct (event.ScenarioId, zoneData.ZoneName)] <-
              0

            config)

        // Store immutable config
        scenarioConfigs[event.ScenarioId] <- {
          ScenarioId = event.ScenarioId
          MaxEnemies = event.MaxEnemies
          Zones = zones
        }

        // Pre-group zones by faction for O(1) lookup
        zones
        |> Array.iter(fun zone ->
          match zone.SpawnInfo.Faction with
          | ValueSome faction ->
            let key = struct (event.ScenarioId, faction)

            match zonesByFaction.TryGetValue key with
            | true, existing ->
              zonesByFaction[key] <- Array.append existing [| zone |]
            | false, _ -> zonesByFaction[key] <- [| zone |]
          | ValueNone -> ())

        // Initialize scenario count
        scenarioCurrentCounts[event.ScenarioId] <- 0)
      |> subscriptions.Add

      core.EventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Spawn(SpawningEvent.SpawnEntity spawn) -> Some spawn
        | _ -> None)
      |> Observable.subscribe(fun intent ->
        let adjustedPosition =
          let scenarios = core.World.Scenarios |> AMap.force

          match scenarios |> HashMap.tryFindV intent.ScenarioId with
          | ValueSome scenario ->
            match scenario.BlockMap with
            | ValueSome blockMap ->
              let surfaceY =
                BlockCollision.getSurfaceHeight blockMap {
                  X = intent.Position.X
                  Y = 0f
                  Z = intent.Position.Z
                }
                |> ValueOption.defaultValue BlockMap.CellSize

              { intent.Position with Y = surfaceY }
            | ValueNone -> intent.Position
          | ValueNone -> intent.Position

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
          Position = adjustedPosition
          SpawnStartTime = totalGameTime
          Duration = duration
        }

        pendingSpawns.Add(pending)

        // Track spawn info for respawning (only for Faction spawns)
        match intent.Type with
        | SystemCommunications.SpawnType.Faction info ->
          spawnedEntities[intent.EntityId] <- {
            EntityId = intent.EntityId
            ScenarioId = intent.ScenarioId
            SpawnInfo = info
            SpawnZoneName = info.SpawnZoneName
          }

          // Increment spawn counts
          let currentScenarioCount = getScenarioCount intent.ScenarioId

          scenarioCurrentCounts[intent.ScenarioId] <- currentScenarioCount + 1

          match info.SpawnZoneName with
          | ValueSome zoneName ->
            let key = struct (intent.ScenarioId, zoneName)
            let currentZoneCount = getZoneCount intent.ScenarioId zoneName
            zoneCurrentCounts[key] <- currentZoneCount + 1
          | ValueNone -> ()
        | _ -> ()

      // Note: The spawn event was already published by the source (CompositionRoot/respawner),\n        // so we don't re-publish here to avoid infinite recursion.

      )
      |> subscriptions.Add

      // Subscribe to EntityDied for respawn handling
      core.EventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.Lifecycle(LifecycleEvent.EntityDied died) -> Some died
        | _ -> None)
      |> Observable.subscribe(fun event ->
        // Emit Removed event to clean up the dead entity
        core.StateWrite.RemoveEntity event.EntityId

        // Check if this entity has spawn info for respawning
        match spawnedEntities.TryGetValue event.EntityId with
        | true, info ->
          // Remove from tracking
          spawnedEntities.Remove event.EntityId |> ignore

          // Decrement spawn counts
          let currentScenarioCount = getScenarioCount event.ScenarioId

          scenarioCurrentCounts[event.ScenarioId] <-
            max 0 (currentScenarioCount - 1)

          match info.SpawnZoneName with
          | ValueSome zoneName ->
            let key = struct (event.ScenarioId, zoneName)
            let currentZoneCount = getZoneCount event.ScenarioId zoneName
            zoneCurrentCounts[key] <- max 0 (currentZoneCount - 1)
          | ValueNone -> ()

          // Find an available zone for this faction and respawn
          match findAvailableZone event.ScenarioId info.SpawnInfo.Faction with
          | Some zone ->
            let newEntityId = Guid.NewGuid() |> UMX.tag
            let newPosition = getRandomSpawnPosition core.Random zone

            let respawnIntent: SystemCommunications.SpawnEntityIntent = {
              EntityId = newEntityId
              ScenarioId = event.ScenarioId
              Type =
                SystemCommunications.SpawnType.Faction {
                  info.SpawnInfo with
                      SpawnZoneName = ValueSome zone.ZoneName
                }
              Position = WorldPosition.fromVector2 newPosition
            }

            core.EventBus.Publish(
              GameEvent.Spawn(SpawningEvent.SpawnEntity respawnIntent)
            )
          | None -> () // No available zone, don't respawn
        | false, _ -> ())
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
            finalizeSpawn
              pending
              stores.AIArchetypeStore
              stores.AIEntityStore
              stores.AIFamilyStore
              core.StateWrite

            toRemove.Add(pending)

        for item in toRemove do
          pendingSpawns.Remove(item) |> ignore

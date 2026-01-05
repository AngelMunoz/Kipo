namespace Pomo.Core

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.AI
open Pomo.Core.Domain.Events
open Pomo.Core.Stores
open Pomo.Core.Domain.Core

/// Module containing map-related spawning logic
module MapSpawning =

  /// Select a random entity from a group using weighted selection
  let selectEntityFromGroup
    (random: Random)
    (group: MapEntityGroup)
    : string voption =
    if group.Entities.Length = 0 then
      ValueNone
    else
      let weights =
        group.Weights
        |> ValueOption.filter(fun w -> w.Length = group.Entities.Length)
        |> ValueOption.defaultValue(
          Array.create
            group.Entities.Length
            (1.0f / float32 group.Entities.Length)
        )

      let totalWeight = Array.sum weights
      let roll = float32(random.NextDouble()) * totalWeight
      let mutable cumulative = 0.0f
      let mutable selected = group.Entities[0]
      let mutable found = false

      for i = 0 to weights.Length - 1 do
        cumulative <- cumulative + weights[i]

        if not found && roll <= cumulative then
          selected <- group.Entities[i]
          found <- true

      ValueSome selected

  /// Represents a spawn candidate extracted from map objects
  type SpawnCandidate = {
    Name: string
    IsPlayerSpawn: bool
    EntityGroup: string voption
    Position: Vector2
  }

  /// Result of resolving an entity from a group
  [<Struct>]
  type ResolvedEntityInfo = {
    EntityKey: string
    ArchetypeId: int<AiArchetypeId>
    MapOverride: MapEntityOverride voption
    Faction: Faction voption
  }

  /// Resolve entity definition, archetype, map override, and faction from an entity group
  /// Returns ValueNone if resolution fails at any step
  let tryResolveEntityFromGroup
    (random: Random)
    (groupStore: MapEntityGroupStore voption)
    (entityStore: AIEntityStore)
    (groupName: string)
    : ResolvedEntityInfo voption =
    groupStore
    |> ValueOption.bind(fun (store: MapEntityGroupStore) ->
      store.tryFind groupName)
    |> ValueOption.bind(fun group ->
      selectEntityFromGroup random group
      |> ValueOption.bind(fun entityKey ->
        entityStore.tryFind entityKey
        |> ValueOption.map(fun entity -> {
          EntityKey = entityKey
          ArchetypeId = entity.ArchetypeId
          MapOverride = group.Overrides |> HashMap.tryFindV entityKey
          Faction = group.Faction
        })))

  /// Load map entity group store for a map (returns None if not found)
  let tryLoadMapEntityGroupStore(mapKey: string) =
    try
      let deserializer = Serialization.create()

      ValueSome(
        Stores.MapEntityGroup.create
          (JsonFileLoader.readMapEntityGroups deserializer)
          mapKey
      )
    with _ ->
      ValueNone

  /// Apply family stat scaling to base stats
  let applyFamilyScaling
    (family: AIFamilyConfig option)
    (baseStats: BaseStats)
    : BaseStats =
    match family with
    | None -> baseStats
    | Some fam ->
      let getScale key =
        fam.StatScaling |> HashMap.tryFind key |> Option.defaultValue 1.0f

      {
        Power = int(float32 baseStats.Power * getScale "Power")
        Magic = int(float32 baseStats.Magic * getScale "Magic")
        Sense = int(float32 baseStats.Sense * getScale "Sense")
        Charm = int(float32 baseStats.Charm * getScale "Charm")
      }

  /// Apply entity stat overrides (replaces stats if present)
  let applyEntityOverrides
    (entityDef: AIEntityDefinition option)
    (baseStats: BaseStats)
    : BaseStats =
    match entityDef with
    | Some entity ->
      match entity.StatOverrides with
      | ValueSome overrides -> overrides
      | ValueNone -> baseStats
    | None -> baseStats

  /// Apply map stat multiplier (global multiplier)
  let applyMapMultiplier
    (mapOverride: MapEntityOverride voption)
    (baseStats: BaseStats)
    : BaseStats =
    match mapOverride with
    | ValueSome o ->
      match o.StatMultiplier with
      | ValueSome mult -> {
          Power = int(float32 baseStats.Power * mult)
          Magic = int(float32 baseStats.Magic * mult)
          Sense = int(float32 baseStats.Sense * mult)
          Charm = int(float32 baseStats.Charm * mult)
        }
      | ValueNone -> baseStats
    | ValueNone -> baseStats

  /// Resolve final stats applying the full override chain:
  /// Archetype → Family Scaling → Entity Overrides → Map Multiplier
  let resolveStats
    (family: AIFamilyConfig option)
    (entityDef: AIEntityDefinition option)
    (mapOverride: MapEntityOverride voption)
    (archetypeStats: BaseStats)
    : BaseStats =
    archetypeStats
    |> applyFamilyScaling family
    |> applyEntityOverrides entityDef
    |> applyMapMultiplier mapOverride

  /// Resolve final skills with restrictions and extras from map override
  let resolveSkills
    (entitySkills: int<SkillId>[])
    (mapOverride: MapEntityOverride voption)
    : int<SkillId>[] =
    match mapOverride with
    | ValueNone -> entitySkills
    | ValueSome o ->
      // Apply restrictions (filter to only allowed skills)
      let filtered =
        match o.SkillRestrictions with
        | ValueSome restrictions ->
          let restrictSet = Set.ofArray restrictions
          entitySkills |> Array.filter(fun s -> Set.contains s restrictSet)
        | ValueNone -> entitySkills

      // Add extra skills
      match o.ExtraSkills with
      | ValueSome extras -> Array.append filtered extras
      | ValueNone -> filtered

  [<Struct>]
  type SpawnZoneInfo = {
    ZoneName: string
    MaxSpawns: int
    SpawnInfo: SystemCommunications.FactionSpawnInfo
    SpawnPositions: Vector2[]
  }

  /// Build spawn zone info from resolved entity data
  let private buildZoneInfo
    (zoneName: string)
    (zoneItems: SpawnCandidate[])
    (resolved: ResolvedEntityInfo)
    : SpawnZoneInfo =
    {
      ZoneName = zoneName
      MaxSpawns = zoneItems.Length
      SpawnInfo = {
        ArchetypeId = resolved.ArchetypeId
        EntityDefinitionKey = ValueSome resolved.EntityKey
        MapOverride = resolved.MapOverride
        Faction = resolved.Faction
        SpawnZoneName = ValueSome zoneName
      }
      SpawnPositions = zoneItems |> Array.map(fun c -> c.Position)
    }

  /// Build spawn zones from candidates, filtering out zones that fail to resolve
  let buildSpawnZones
    (random: Random)
    (mapEntityGroupStore: MapEntityGroupStore voption)
    (entityStore: AIEntityStore)
    (candidates: SpawnCandidate[])
    : SpawnZoneInfo[] =
    candidates
    |> Array.filter(fun c -> not c.IsPlayerSpawn)
    |> Array.groupBy(fun c -> c.Name)
    |> Array.choose(fun (zoneName, zoneItems) ->
      zoneItems[0].EntityGroup
      |> ValueOption.bind(fun groupName ->
        tryResolveEntityFromGroup
          random
          mapEntityGroupStore
          entityStore
          groupName)
      |> ValueOption.map(buildZoneInfo zoneName zoneItems)
      |> ValueOption.toOption)

  /// Convert SpawnZoneInfo to SpawnZoneData for event publishing
  let toSpawnZoneData
    (scenarioId: Guid<ScenarioId>)
    (zoneInfo: SpawnZoneInfo)
    : SystemCommunications.SpawnZoneData =
    {
      ZoneName = zoneInfo.ZoneName
      ScenarioId = scenarioId
      MaxSpawns = zoneInfo.MaxSpawns
      SpawnInfo = zoneInfo.SpawnInfo
      SpawnPositions = zoneInfo.SpawnPositions
    }

  /// Context for spawning enemies in a scenario
  [<Struct>]
  type SpawnContext = {
    Random: Random
    MapEntityGroupStore: MapEntityGroupStore voption
    EntityStore: AIEntityStore
    ScenarioId: Guid<ScenarioId>
    MaxEnemies: int
  }

  /// Publishers for spawn-related events
  type SpawnEventPublisher = {
    RegisterZones: SystemCommunications.RegisterSpawnZones -> unit
    SpawnEntity: SystemCommunications.SpawnEntityIntent -> unit
  }

  /// Spawn all enemies for a scenario
  /// Builds spawn zones, registers them for respawn tracking, and spawns initial enemies
  let spawnEnemiesForScenario
    (ctx: SpawnContext)
    (candidates: SpawnCandidate[])
    (publisher: SpawnEventPublisher)
    =
    let spawnZones =
      buildSpawnZones
        ctx.Random
        ctx.MapEntityGroupStore
        ctx.EntityStore
        candidates

    // Register spawn zones for respawn tracking
    publisher.RegisterZones {
      ScenarioId = ctx.ScenarioId
      MaxEnemies = ctx.MaxEnemies
      Zones = spawnZones |> Array.map(toSpawnZoneData ctx.ScenarioId)
    }

    // Spawn initial enemies from each zone
    let mutable totalEnemyCount = 0

    for zone in spawnZones do
      for i = 0 to min
        (zone.MaxSpawns - 1)
        (ctx.MaxEnemies - totalEnemyCount - 1) do
        if
          totalEnemyCount < ctx.MaxEnemies && i < zone.SpawnPositions.Length
        then
          let enemyId = Guid.NewGuid() |> UMX.tag

          publisher.SpawnEntity {
            EntityId = enemyId
            ScenarioId = ctx.ScenarioId
            Type = SystemCommunications.SpawnType.Faction zone.SpawnInfo
            Position = WorldPosition.fromVector2 zone.SpawnPositions[i]
          }

          totalEnemyCount <- totalEnemyCount + 1

namespace Pomo.Core

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.AI
open Pomo.Core.Domain.Events
open Pomo.Core.Stores

/// Module containing map-related spawning logic
module MapSpawning =

  /// Get a random point inside a polygon
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

  /// Extract spawn candidates from a map definition
  /// Respects MaxSpawns property to generate multiple candidates per spawn area
  let extractSpawnCandidates
    (mapDef: MapDefinition)
    (random: Random)
    : SpawnCandidate seq =
    mapDef.ObjectGroups
    |> IndexList.collect(fun group ->
      group.Objects
      |> IndexList.collect(fun obj ->
        match obj.Type with
        | ValueSome MapObjectType.Spawn ->
          let isPlayerSpawn =
            obj.Properties
            |> HashMap.tryFindV "PlayerSpawn"
            |> ValueOption.map(fun v -> v.ToLower() = "true")
            |> ValueOption.defaultValue false

          let entityGroup = obj.Properties |> HashMap.tryFindV "EntityGroup"

          // Get MaxSpawns from properties, default to 1
          let maxSpawns =
            obj.Properties
            |> HashMap.tryFindV "MaxSpawns"
            |> ValueOption.bind(fun v ->
              match Int32.TryParse v with
              | true, n -> ValueSome n
              | _ -> ValueNone)
            |> ValueOption.defaultValue 1

          // Generate a position within the spawn area
          let getRandomPosition() =
            match obj.CollisionShape with
            | ValueSome(ClosedPolygon points) when not points.IsEmpty ->
              let offset = getRandomPointInPolygon points random
              Vector2(obj.X + offset.X, obj.Y + offset.Y)
            | ValueSome(RectangleShape(width, height)) ->
              // Random point within rectangle bounds
              let offsetX = float32(random.NextDouble()) * width
              let offsetY = float32(random.NextDouble()) * height
              Vector2(obj.X + offsetX, obj.Y + offsetY)
            | _ ->
              // Fallback: use object's width/height if defined
              if obj.Width > 0.0f && obj.Height > 0.0f then
                let offsetX = float32(random.NextDouble()) * obj.Width
                let offsetY = float32(random.NextDouble()) * obj.Height
                Vector2(obj.X + offsetX, obj.Y + offsetY)
              else
                Vector2(obj.X, obj.Y)

          // Create candidates for each spawn slot
          seq {
            for _ in 1..maxSpawns do
              yield {
                Name = obj.Name
                IsPlayerSpawn = isPlayerSpawn
                EntityGroup = entityGroup
                Position = getRandomPosition()
              }
          }
          |> IndexList.ofSeq
        | _ -> IndexList.empty))
    |> IndexList.toSeq

  /// Determine the player spawn position from candidates
  let findPlayerSpawnPosition
    (targetSpawn: string voption)
    (candidates: SpawnCandidate seq)
    =
    let byTargetName =
      match targetSpawn with
      | ValueSome targetName ->
        candidates
        |> Seq.tryFind(fun c -> c.Name = targetName)
        |> Option.map(fun c -> c.Position)
      | ValueNone -> None

    byTargetName
    |> Option.orElse(
      candidates
      |> Seq.tryFind(fun c -> c.IsPlayerSpawn)
      |> Option.map(fun c -> c.Position)
    )
    |> Option.orElse(
      candidates |> Seq.tryHead |> Option.map(fun c -> c.Position)
    )
    |> Option.defaultValue Vector2.Zero

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
    (groupStore: MapEntityGroupStore option)
    (entityStore: AIEntityStore)
    (groupName: string)
    : ResolvedEntityInfo voption =
    groupStore
    |> Option.toValueOption
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
  let tryLoadMapEntityGroupStore(mapKey: string) : MapEntityGroupStore option =
    try
      let deserializer = Serialization.create()

      Some(
        Stores.MapEntityGroup.create
          (JsonFileLoader.readMapEntityGroups deserializer)
          mapKey
      )
    with _ ->
      None

  /// Get max enemies allowed from map properties
  let getMaxEnemies(mapDef: MapDefinition) =
    mapDef.Properties
    |> HashMap.tryFind "MaxEnemyEntities"
    |> Option.bind(fun v ->
      match System.Int32.TryParse v with
      | true, i -> Some i
      | _ -> None)
    |> Option.defaultValue 0

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
    (mapEntityGroupStore: MapEntityGroupStore option)
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
    MapEntityGroupStore: MapEntityGroupStore option
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
            Position = zone.SpawnPositions[i]
          }

          totalEnemyCount <- totalEnemyCount + 1

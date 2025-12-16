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
  let selectEntityFromGroup (random: Random) (group: MapEntityGroup) =
    if group.Entities.Length = 0 then
      None
    else
      let weights =
        match group.Weights with
        | ValueSome w when w.Length = group.Entities.Length -> w
        | _ ->
          Array.create
            group.Entities.Length
            (1.0f / float32 group.Entities.Length)

      let totalWeight = Array.sum weights
      let roll = float32(random.NextDouble()) * totalWeight
      let mutable cumulative = 0.0f
      let mutable selected = group.Entities[0]

      for i = 0 to weights.Length - 1 do
        cumulative <- cumulative + weights[i]

        if roll <= cumulative && selected = group.Entities[0] then
          selected <- group.Entities[i]

      Some selected

  /// Represents a spawn candidate extracted from map objects
  type SpawnCandidate = {
    Name: string
    IsPlayerSpawn: bool
    EntityGroup: string voption
    Position: Vector2
  }

  /// Extract spawn candidates from a map definition
  let extractSpawnCandidates
    (mapDef: MapDefinition)
    (random: Random)
    : SpawnCandidate seq =
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

          let entityGroup = obj.Properties |> HashMap.tryFindV "EntityGroup"

          let pos =
            match obj.CollisionShape with
            | ValueSome(ClosedPolygon points) when not points.IsEmpty ->
              let offset = getRandomPointInPolygon points random
              Vector2(obj.X + offset.X, obj.Y + offset.Y)
            | _ -> Vector2(obj.X, obj.Y)

          Some {
            Name = obj.Name
            IsPlayerSpawn = isPlayerSpawn
            EntityGroup = entityGroup
            Position = pos
          }
        | _ -> None))
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

  /// Resolve entity definition and archetype from an entity group
  let resolveEntityFromGroup
    (random: Random)
    (groupStore: MapEntityGroupStore option)
    (entityStore: AIEntityStore)
    (groupName: string)
    : (string voption * int<AiArchetypeId>) =
    match groupStore with
    | Some store ->
      match store.tryFind groupName with
      | ValueSome group ->
        match selectEntityFromGroup random group with
        | Some entityKey ->
          match entityStore.tryFind entityKey with
          | ValueSome entity -> ValueSome entityKey, entity.ArchetypeId
          | ValueNone -> ValueNone, %1<AiArchetypeId>
        | None -> ValueNone, %1<AiArchetypeId>
      | ValueNone -> ValueNone, %1<AiArchetypeId>
    | None -> ValueNone, %1<AiArchetypeId>

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

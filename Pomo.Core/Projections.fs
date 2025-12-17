namespace Pomo.Core

open System
open FSharp.UMX
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Events
open Pomo.Core.Stores
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.Projectile
open Pomo.Core.Domain.Skill

module Projections =

  module private Dictionary =
    open System.Collections.Generic

    let tryFindV
      (key: 'Key)
      (dict: IReadOnlyDictionary<'Key, 'Value>)
      : 'Value voption =
      match dict.TryGetValue key with
      | true, value -> ValueSome value
      | false, _ -> ValueNone

  let private liveEntities(world: World) =
    world.Resources
    |> AMap.filter(fun _ resource -> resource.Status = Entity.Status.Alive)
    |> AMap.keys

  let inline private effectKindToCombatStatus(effect: Skill.ActiveEffect) =
    match effect.SourceEffect.Kind with
    | Skill.EffectKind.Stun -> Some CombatStatus.Stunned
    | Skill.EffectKind.Silence -> Some CombatStatus.Silenced
    | Skill.EffectKind.Taunt -> None
    | Skill.EffectKind.Buff
    | Skill.EffectKind.DamageOverTime
    | Skill.EffectKind.ResourceOverTime
    | Skill.EffectKind.Debuff -> None

  let private calculateCombatStatuses(world: World) =
    world.ActiveEffects
    |> AMap.map(fun _ effectList ->
      effectList |> IndexList.choose effectKindToCombatStatus)

  module private DerivedStatsCalculator =

    let calculateBase(baseStats: Entity.BaseStats) : Entity.DerivedStats = {
      AP = baseStats.Power * 2
      AC = baseStats.Power + int(float baseStats.Power * 1.25)
      DX = baseStats.Power
      MP = baseStats.Magic * 5
      MA = baseStats.Magic * 2
      MD = baseStats.Magic + int(float baseStats.Magic * 1.25)
      WT = baseStats.Sense * 5
      DA = baseStats.Sense * 2
      LK = baseStats.Sense + int(float baseStats.Sense * 0.5)
      HP = baseStats.Charm * 10
      DP = baseStats.Charm + int(float baseStats.Charm * 1.25)
      HV = baseStats.Charm * 2
      MS = 100
      HPRegen = 20
      MPRegen = 20
      ElementAttributes = HashMap.empty
      ElementResistances = HashMap.empty
    }

    let updateStat
      (stats: Entity.DerivedStats)
      (stat: Stat)
      (transformI: int -> int)
      (transformF: float -> float)
      =
      match stat with
      | AP -> { stats with AP = transformI stats.AP }
      | AC -> { stats with AC = transformI stats.AC }
      | DX -> { stats with DX = transformI stats.DX }
      | MP -> { stats with MP = transformI stats.MP }
      | MA -> { stats with MA = transformI stats.MA }
      | MD -> { stats with MD = transformI stats.MD }
      | WT -> { stats with WT = transformI stats.WT }
      | DA -> { stats with DA = transformI stats.DA }
      | LK -> { stats with LK = transformI stats.LK }
      | HP -> { stats with HP = transformI stats.HP }
      | DP -> { stats with DP = transformI stats.DP }
      | HV -> { stats with HV = transformI stats.HV }
      | MS -> { stats with MS = transformI stats.MS }
      | HPRegen -> {
          stats with
              HPRegen = transformI stats.HPRegen
        }
      | MPRegen -> {
          stats with
              MPRegen = transformI stats.MPRegen
        }
      | ElementResistance element ->
        let current =
          stats.ElementResistances
          |> HashMap.tryFind element
          |> Option.defaultValue 0.0

        let updated = transformF current

        {
          stats with
              ElementResistances =
                stats.ElementResistances.Add(element, updated)
        }
      | ElementAttribute element ->
        let current =
          stats.ElementAttributes
          |> HashMap.tryFind element
          |> Option.defaultValue 0.0

        let updated = transformF current

        {
          stats with
              ElementAttributes = stats.ElementAttributes.Add(element, updated)
        }

    let inline applySingleModifier
      (accStats: Entity.DerivedStats byref)
      (statModifier: StatModifier)
      =
      match statModifier with
      | Additive(stat, value) ->
        accStats <- updateStat accStats stat ((+)(int value)) ((+) value)
      | Multiplicative(stat, value) ->
        let multiplyI(current: int) = float current * value |> int
        let multiplyF(current: float) = current * value
        accStats <- updateStat accStats stat multiplyI multiplyF

    let applyModifiers
      (stats: Entity.DerivedStats)
      (effects: Skill.ActiveEffect IndexList voption)
      (equipmentBonuses: StatModifier IndexList)
      =
      let mutable result = stats

      for modifier in equipmentBonuses do
        applySingleModifier &result modifier

      match effects with
      | ValueNone -> ()
      | ValueSome effectList ->
        for effect in effectList do
          for modifier in effect.SourceEffect.Modifiers do
            match modifier with
            | Skill.EffectModifier.StaticMod m -> applySingleModifier &result m
            | Skill.EffectModifier.ResourceChange _
            | Skill.EffectModifier.AbilityDamageMod _
            | Skill.EffectModifier.DynamicMod _ -> ()

      result

    let inline collectEquipmentStats
      (equipmentStats: HashMap<Slot, StatModifier array>)
      : StatModifier IndexList =
      if equipmentStats.IsEmpty then
        IndexList.Empty
      else
        let mutable totalCount = 0

        for _, stats in equipmentStats do
          totalCount <- totalCount + stats.Length

        if totalCount = 0 then
          IndexList.Empty
        else
          let allStats = Array.zeroCreate totalCount
          let mutable idx = 0

          for _, stats in equipmentStats do
            for stat in stats do
              allStats[idx] <- stat
              idx <- idx + 1

          IndexList.ofArray allStats

    let getEquipmentStatBonusesForId
      (world: World, itemStore: ItemStore, entityId: Guid<EntityId>)
      =
      adaptive {
        let! equipmentStats =
          world.EquippedItems
          |> AMap.tryFind entityId
          |> AVal.map(
            Option.map(fun map ->
              map
              |> HashMap.chooseV(fun _ itemInstanceId ->
                world.ItemInstances
                |> Dictionary.tryFindV itemInstanceId
                |> ValueOption.bind(fun instance ->
                  itemStore.tryFind instance.ItemId)
                |> ValueOption.map(fun item ->
                  match item.Kind with
                  | Wearable props -> props.Stats
                  | _ -> Array.empty)))
          )

        let equipmentStats = equipmentStats |> Option.defaultValue HashMap.empty
        return collectEquipmentStats equipmentStats
      }

  let private calculateDerivedStats (itemStore: ItemStore) (world: World) =
    (world.BaseStats, world.ActiveEffects)
    ||> AMap.choose2V(fun _ baseStats effects ->
      baseStats |> ValueOption.map(fun stats -> struct (stats, effects)))
    |> AMap.mapA(fun entityId struct (baseStats, effects) -> adaptive {
      let! equipmentBonuses =
        DerivedStatsCalculator.getEquipmentStatBonusesForId(
          world,
          itemStore,
          entityId
        )

      let initialStats = DerivedStatsCalculator.calculateBase baseStats

      let finalStats =
        DerivedStatsCalculator.applyModifiers
          initialStats
          effects
          equipmentBonuses

      return finalStats
    })

  let private inventoryDefs(world: World, itemStore: ItemStore) =
    world.EntityInventories
    |> AMap.map(fun _ itemIds ->
      itemIds
      |> HashSet.chooseV(fun itemId ->
        match world.ItemInstances |> Dictionary.tryFindV itemId with
        | ValueSome instance ->
          match itemStore.tryFind instance.ItemId with
          | ValueSome def -> ValueSome def
          | ValueNone -> ValueNone
        | ValueNone -> ValueNone))

  let private equippedItemDefs(world: World, itemStore: ItemStore) =
    world.EquippedItems
    |> AMap.map(fun _ itemIds ->
      itemIds
      |> HashMap.chooseV(fun _ itemId ->
        match world.ItemInstances |> Dictionary.tryFindV itemId with
        | ValueSome instance ->
          match itemStore.tryFind instance.ItemId with
          | ValueSome def -> ValueSome def
          | ValueNone -> ValueNone
        | ValueNone -> ValueNone))

  let private activeActionSets(world: World) =
    (world.ActiveActionSets, world.ActionSets)
    ||> AMap.choose2V(fun _ set sets ->
      let sets = defaultValueArg sets HashMap.empty

      set |> ValueOption.bind(fun set -> sets |> HashMap.tryFindV set))



  [<Struct>]
  type MovementSnapshot = {
    Positions: HashMap<Guid<EntityId>, Vector2>
    SpatialGrid: HashMap<GridCell, IndexList<Guid<EntityId>>>
    Rotations: HashMap<Guid<EntityId>, float32>
    ModelConfigIds: HashMap<Guid<EntityId>, string>
  } with

    static member Empty = {
      Positions = HashMap.empty
      SpatialGrid = HashMap.empty
      Rotations = HashMap.empty
      ModelConfigIds = HashMap.empty
    }

  [<Struct>]
  type EntityScenarioContext = {
    ScenarioId: Guid<ScenarioId>
    Scenario: Scenario
    MapKey: string
  }

  [<Struct>]
  type EffectOwnerTransform = {
    Position: Vector2
    Velocity: Vector2
    Rotation: float32
    Effects: ActiveEffect IndexList
  }

  type ProjectionService =
    abstract LiveEntities: aset<Guid<EntityId>>
    abstract CombatStatuses: amap<Guid<EntityId>, IndexList<CombatStatus>>
    abstract DerivedStats: amap<Guid<EntityId>, Entity.DerivedStats>
    abstract EquipedItems: amap<Guid<EntityId>, HashMap<Slot, ItemDefinition>>
    abstract Inventories: amap<Guid<EntityId>, HashSet<ItemDefinition>>
    abstract EntityScenarios: amap<Guid<EntityId>, Guid<ScenarioId>>

    abstract ActionSets:
      amap<Guid<EntityId>, HashMap<Action.GameAction, SlotProcessing>>

    abstract EntityScenarioContexts: amap<Guid<EntityId>, EntityScenarioContext>
    abstract EffectOwnerTransforms: amap<Guid<EntityId>, EffectOwnerTransform>
    abstract ComputeMovementSnapshot: Guid<ScenarioId> -> MovementSnapshot

    abstract GetNearbyEntitiesSnapshot:
      MovementSnapshot * HashSet<Guid<EntityId>> * Vector2 * float32 ->
        IndexList<struct (Guid<EntityId> * Vector2)>


  let private calculateMovementSnapshot
    (time: TimeSpan)
    (velocities: HashMap<Guid<EntityId>, Vector2>)
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (rotations: HashMap<Guid<EntityId>, float32>)
    (modelConfigIds: HashMap<Guid<EntityId>, string>)
    (entityScenarios: HashMap<Guid<EntityId>, Guid<ScenarioId>>)
    (scenarioId: Guid<ScenarioId>)
    =
    let dt = float32 time.TotalSeconds
    let mutable newPositions = HashMap.empty
    let mutable newGrid = HashMap.empty
    let mutable newRotations = HashMap.empty
    let mutable newModelConfigIds = HashMap.empty

    for (id, startPos) in positions do
      match entityScenarios |> HashMap.tryFindV id with
      | ValueSome sId when sId = scenarioId ->
        // Calculate Position
        let currentPos =
          match velocities |> HashMap.tryFindV id with
          | ValueSome v -> startPos + (v * dt)
          | ValueNone -> startPos

        newPositions <- newPositions |> HashMap.add id currentPos

        // Calculate Rotation (Derived from Velocity if moving, else keep existing)
        let rotation =
          match velocities |> HashMap.tryFindV id with
          | ValueSome v when v <> Vector2.Zero ->
            float32(Math.Atan2(float v.X, float v.Y))
          | _ -> rotations |> HashMap.tryFind id |> Option.defaultValue 0.0f

        newRotations <- newRotations |> HashMap.add id rotation

        // Model Config
        match modelConfigIds |> HashMap.tryFindV id with
        | ValueSome configId ->
          newModelConfigIds <- newModelConfigIds |> HashMap.add id configId
        | ValueNone -> ()

        // Calculate Grid
        let cell =
          Spatial.getGridCell Core.Constants.Collision.GridCellSize currentPos

        // Add to Grid
        let cellContent =
          match newGrid |> HashMap.tryFindV cell with
          | ValueSome list -> list
          | ValueNone -> IndexList.empty

        newGrid <- newGrid |> HashMap.add cell (cellContent |> IndexList.add id)
      | _ -> ()

    {
      Positions = newPositions
      SpatialGrid = newGrid
      Rotations = newRotations
      ModelConfigIds = newModelConfigIds
    }

  let private entityScenarioContexts(world: World) =
    world.EntityScenario
    |> AMap.mapA(fun _entityId scenarioId -> adaptive {
      let! scenario = world.Scenarios |> AMap.tryFind scenarioId

      match scenario with
      | Some s ->
        return
          Some {
            ScenarioId = scenarioId
            Scenario = s
            MapKey = s.Map.Key
          }
      | None -> return None
    })
    |> AMap.choose(fun _ v -> v)

  let private effectOwnerTransforms(world: World) =
    world.ActiveEffects
    |> AMap.mapA(fun entityId effects -> adaptive {
      let! position = world.Positions |> AMap.tryFind entityId
      and! velocity = world.Velocities |> AMap.tryFind entityId
      and! rotation = world.Rotations |> AMap.tryFind entityId

      return {
        Position = position |> Option.defaultValue Vector2.Zero
        Velocity = velocity |> Option.defaultValue Vector2.Zero
        Rotation = rotation |> Option.defaultValue 0.0f
        Effects = effects
      }
    })

  let create(itemStore: ItemStore, world: World) =
    { new ProjectionService with
        member _.LiveEntities = liveEntities world
        member _.CombatStatuses = calculateCombatStatuses world
        member _.DerivedStats = calculateDerivedStats itemStore world
        member _.EquipedItems = equippedItemDefs(world, itemStore)
        member _.Inventories = inventoryDefs(world, itemStore)
        member _.EntityScenarios = world.EntityScenario
        member _.ActionSets = activeActionSets world
        member _.EntityScenarioContexts = entityScenarioContexts world
        member _.EffectOwnerTransforms = effectOwnerTransforms world

        member _.ComputeMovementSnapshot(scenarioId) =
          let time = world.Time |> AVal.map _.Delta |> AVal.force
          let velocities = world.Velocities |> AMap.force
          let positions = world.Positions |> AMap.force
          let rotations = world.Rotations |> AMap.force
          let modelConfigIds = world.ModelConfigId |> AMap.force
          let entityScenarios = world.EntityScenario |> AMap.force

          calculateMovementSnapshot
            time
            velocities
            positions
            rotations
            modelConfigIds
            entityScenarios
            scenarioId

        member _.GetNearbyEntitiesSnapshot
          (snapshot, liveEntities, center, radius)
          =
          let cells =
            Spatial.getCellsInRadius
              Constants.Collision.GridCellSize
              center
              radius

          let potentialTargets =
            cells
            |> IndexList.collect(fun cell ->
              match snapshot.SpatialGrid |> HashMap.tryFindV cell with
              | ValueSome list -> list
              | ValueNone -> IndexList.empty)

          potentialTargets
          |> IndexList.choose(fun entityId ->
            // Filter out non-live entities (projectiles, dead entities, etc.)
            if not(liveEntities.Contains entityId) then
              None
            else
              match snapshot.Positions |> HashMap.tryFindV entityId with
              | ValueSome pos when Vector2.Distance(pos, center) <= radius ->
                Some struct (entityId, pos)
              | _ -> None)
    }

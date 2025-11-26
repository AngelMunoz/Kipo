namespace Pomo.Core

open System
open FSharp.UMX
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Core
open Pomo.Core.Stores
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.Spatial

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

  let private effectKindToCombatStatus(effect: Skill.ActiveEffect) =
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


    let applySingleModifier
      (accStats: Entity.DerivedStats)
      (statModifier: StatModifier)
      =
      match statModifier with
      | Additive(stat, value) ->
        updateStat accStats stat ((+)(int value)) ((+) value)
      | Multiplicative(stat, value) ->
        let multiplyI(current: int) = float current * value |> int
        let multiplyF(current: float) = current * value
        updateStat accStats stat multiplyI multiplyF

    let applyModifiers
      (stats: Entity.DerivedStats)
      (effects: Skill.ActiveEffect IndexList voption)
      (equipmentBonuses: StatModifier IndexList)
      =
      let fromEffects =
        match effects with
        | ValueNone -> [||]
        | ValueSome effectList ->
          effectList.AsArray
          |> Array.collect _.SourceEffect.Modifiers
          |> Array.choose (function
            | Skill.EffectModifier.StaticMod m -> Some m
            | Skill.EffectModifier.ResourceChange _
            | Skill.EffectModifier.AbilityDamageMod _
            | Skill.EffectModifier.DynamicMod _ -> None)

      equipmentBonuses.AsArray
      |> Array.append fromEffects
      |> Array.fold applySingleModifier stats

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

        return
          equipmentStats
          |> HashMap.toValueArray
          |> Array.fold
            (fun acc stats -> IndexList.ofArray stats |> IndexList.append acc)
            IndexList.Empty
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
  }

  type ProjectionService =
    abstract LiveEntities: aset<Guid<EntityId>>
    abstract CombatStatuses: amap<Guid<EntityId>, IndexList<CombatStatus>>
    abstract DerivedStats: amap<Guid<EntityId>, Entity.DerivedStats>
    abstract EquipedItems: amap<Guid<EntityId>, HashMap<Slot, ItemDefinition>>
    abstract Inventories: amap<Guid<EntityId>, HashSet<ItemDefinition>>

    abstract ActionSets:
      amap<Guid<EntityId>, HashMap<Action.GameAction, SlotProcessing>>

    /// Forces the current world state and computes the physics/grid for this frame.
    abstract ComputeMovementSnapshot: unit -> MovementSnapshot

    /// Helper to query a snapshot (pure function, no longer adaptive)
    abstract GetNearbyEntitiesSnapshot:
      MovementSnapshot * Vector2 * float32 ->
        IndexList<struct (Guid<EntityId> * Vector2)>


  let private calculateMovementSnapshot
    (time: TimeSpan)
    (velocities: HashMap<Guid<EntityId>, Vector2>)
    (positions: HashMap<Guid<EntityId>, Vector2>)
    =
    let dt = float32 time.TotalSeconds
    let mutable newPositions = HashMap.empty
    let mutable newGrid = HashMap.empty

    for (id, startPos) in positions do
      // Calculate Position
      let currentPos =
        match velocities |> HashMap.tryFindV id with
        | ValueSome v -> startPos + (v * dt)
        | ValueNone -> startPos

      newPositions <- newPositions |> HashMap.add id currentPos

      // Calculate Grid
      let cell =
        Spatial.getGridCell Core.Constants.Collision.GridCellSize currentPos

      // Add to Grid
      let cellContent =
        match newGrid |> HashMap.tryFindV cell with
        | ValueSome list -> list
        | ValueNone -> IndexList.empty

      newGrid <- newGrid |> HashMap.add cell (cellContent |> IndexList.add id)

    {
      Positions = newPositions
      SpatialGrid = newGrid
    }

  let create(itemStore: ItemStore, world: World) =

    { new ProjectionService with
        member _.LiveEntities = liveEntities world
        member _.CombatStatuses = calculateCombatStatuses world
        member _.DerivedStats = calculateDerivedStats itemStore world
        member _.EquipedItems = equippedItemDefs(world, itemStore)
        member _.Inventories = inventoryDefs(world, itemStore)
        member _.ActionSets = activeActionSets world

        member _.ComputeMovementSnapshot() =
          let time = world.Time |> AVal.map _.Delta |> AVal.force
          let velocities = world.Velocities |> AMap.force
          let positions = world.Positions |> AMap.force
          calculateMovementSnapshot time velocities positions

        member _.GetNearbyEntitiesSnapshot(snapshot, center, radius) =
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
            match snapshot.Positions |> HashMap.tryFindV entityId with
            | ValueSome pos when Vector2.Distance(pos, center) <= radius ->
              Some struct (entityId, pos)
            | _ -> None)
    }

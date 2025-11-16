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


  let private updatedPositions(world: World) =
    (world.Velocities, world.Positions)
    ||> AMap.choose2V(fun _ velocity position ->
      match velocity, position with
      | ValueSome vel, ValueSome pos -> ValueSome struct (vel, pos)
      | _ -> ValueNone)
    |> AMap.mapA(fun _ struct (velocity, position) -> adaptive {
      let! time = world.Time |> AVal.map _.Delta
      let displacement = velocity * float32 time.TotalSeconds
      return position + displacement
    })

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

  type ProjectionService =
    abstract UpdatedPositions: amap<Guid<EntityId>, Vector2>
    abstract LiveEntities: aset<Guid<EntityId>>
    abstract CombatStatuses: amap<Guid<EntityId>, IndexList<CombatStatus>>
    abstract DerivedStats: amap<Guid<EntityId>, Entity.DerivedStats>
    abstract EquipedItems: amap<Guid<EntityId>, HashMap<Slot, ItemDefinition>>
    abstract Inventories: amap<Guid<EntityId>, HashSet<ItemDefinition>>


  let create(itemStore: ItemStore, world: World) =
    { new ProjectionService with
        member _.UpdatedPositions = updatedPositions world
        member _.LiveEntities = liveEntities world
        member _.CombatStatuses = calculateCombatStatuses world
        member _.DerivedStats = calculateDerivedStats itemStore world
        member _.EquipedItems = equippedItemDefs(world, itemStore)
        member _.Inventories = inventoryDefs(world, itemStore)
    }

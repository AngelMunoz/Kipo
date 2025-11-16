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

  let UpdatedPositions(world: World) =
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

  let LiveEntities(world: World) =
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

  let CalculateCombatStatuses(world: World) =
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
      (equipmentBonuses: StatModifier array)
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

      equipmentBonuses
      |> Array.append fromEffects
      |> Array.fold applySingleModifier stats

  let getEquipmentStatBonuses(world: World, itemStore: ItemStore) =
    let equipmentStats =
      world.EquippedItems
      |> AMap.mapA(fun _ equippedMap -> adaptive {
        let! items = world.ItemInstances |> AMap.toAVal

        let foundItems =
          equippedMap
          |> HashMap.chooseV(fun _ itemInstanceId ->
            match items |> HashMap.tryFindV itemInstanceId with
            | ValueSome itemInstance -> itemStore.tryFind itemInstance.ItemId
            | ValueNone -> ValueNone)
          |> HashMap.toValueArray

        return
          foundItems
          |> Array.fold
            (fun acc item ->
              match item.Kind with
              | Wearable wearable -> Array.append acc wearable.Stats
              | _ -> acc)
            Array.empty
      })

    equipmentStats


  let getEquippedItems(world: World, itemStore: ItemStore) =
    world.EquippedItems
    |> AMap.mapA(fun _ equippedMap -> adaptive {
      let! items = world.ItemInstances |> AMap.toAVal

      let foundItems =
        equippedMap
        |> HashMap.chooseV(fun _ itemInstanceId ->
          match items |> HashMap.tryFindV itemInstanceId with
          | ValueSome itemInstance ->
            match itemStore.tryFind itemInstance.ItemId with
            | ValueSome item when item.Kind.IsWearable -> ValueSome item
            | _ -> ValueNone
          | ValueNone -> ValueNone)
        |> HashMap.toValueArray
      return foundItems |> Array.map(fun item -> item.)
    })

  let getInventory(world: World) =
    world.EntityInventories
    |> AMap.mapA(fun _ inventorySet -> adaptive {
      let! itemInstances = world.ItemInstances
      let mutable inventory = HashSet.empty

      for itemInstanceId in inventorySet do
        match itemInstances |> AMap.tryFindV itemInstanceId with
        | ValueSome itemInstance -> inventory <- inventory.Add(itemInstance)
        | ValueNone -> ()

      return inventory
    })

  let CalculateDerivedStats(world: World, itemStore: ItemStore) =
    (world.BaseStats, world.ActiveEffects)
    ||> AMap.choose2V(fun _ baseStats effects ->
      match baseStats with
      | ValueSome stats -> ValueSome struct (stats, effects)
      | _ -> ValueNone)
    |> AMap.mapA(fun _ struct (baseStats, effects) -> adaptive {
      let! equipmentBonuses =
        getEquipmentStatBonuses(world, itemStore)
        |> AMap.fold
          (fun acc _ bonuses -> Array.append acc bonuses)
          Array.empty

      let initialStats = DerivedStatsCalculator.calculateBase baseStats

      let finalStats =
        DerivedStatsCalculator.applyModifiers
          initialStats
          effects
          equipmentBonuses

      return finalStats
    })

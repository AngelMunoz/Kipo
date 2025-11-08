namespace Pomo.Core

open System
open FSharp.UMX
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Core

module Projections =

  let UpdatedPositions(world: World) =
    (world.Velocities, world.Positions)
    ||> AMap.choose2V(fun _ velocity position ->
      match velocity, position with
      | ValueSome vel, ValueSome pos -> ValueSome struct (vel, pos)
      | _ -> ValueNone)
    |> AMap.mapA(fun _ struct (velocity, position) -> adaptive {
      let! time = world.DeltaTime
      let displacement = velocity * float32 time.TotalSeconds
      return position + displacement
    })

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
      // Movement
      MS = 100 // Base speed
      // Elements
      ElementAttributes = HashMap.empty
      ElementResistances = HashMap.empty
    }

    let private updateStat
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

    let private applySingleModifier
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
      (effects: Skill.Effect IndexList voption)
      =
      match effects with
      | ValueNone -> stats
      | ValueSome effectList ->
        effectList.AsArray
        |> Array.collect(fun effect -> effect.Modifiers)
        |> Array.choose (function
          | Skill.EffectModifier.StaticMod m -> Some m
          // TODO: Handle dynamic modifiers
          | Skill.EffectModifier.AbilityDamageMod _
          | Skill.EffectModifier.DynamicMod _ -> None)
        |> Array.fold applySingleModifier stats

  let CalculateDerivedStats(world: World) =
    (world.BaseStats, world.ActiveEffects)
    ||> AMap.choose2V(fun _ baseStats effects ->
      match baseStats with
      | ValueSome stats -> ValueSome struct (stats, effects)
      | _ -> ValueNone)
    |> AMap.map(fun _ struct (baseStats, effects) ->
      let initialStats = DerivedStatsCalculator.calculateBase baseStats

      // TODO: Apply equipment stats here. This will likely involve another
      // projection that reads equipped items and aggregates their stat bonuses.
      let statsWithEquipment = initialStats

      let finalStats =
        DerivedStatsCalculator.applyModifiers statsWithEquipment effects

      finalStats)

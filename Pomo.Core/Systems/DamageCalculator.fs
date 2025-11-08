namespace Pomo.Core.Systems

open System
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Skill
open Pomo.Core.Systems.FormulaEvaluator

module DamageCalculator =

  [<Struct>]
  type DamageResult = {
    Amount: int
    IsCritical: bool
    IsEvaded: bool
  }

  let private calculateHitChance
    (attackerStats: DerivedStats)
    (defenderStats: DerivedStats)
    =
    // This logic is taken from the old prototype's EffectApplication.fs
    let attackerValue = float attackerStats.AC
    let defenderValue = float defenderStats.HV
    let baseHitChance = 0.5

    if attackerValue = 0.0 && defenderValue = 0.0 then
      1.0
    else
      let effectiveAttacker = max 0.0 attackerValue
      let effectiveDefender = max 0.0 defenderValue
      let statAdvantage = effectiveAttacker - effectiveDefender
      let divisor = 100.0
      let chance = baseHitChance + (statAdvantage / divisor)
      max 0.05 (min 0.95 chance)

  let calculateFinalDamage
    (rng: Random)
    (attackerStats: DerivedStats)
    (defenderStats: DerivedStats)
    (skill: ActiveSkill)
    =
    // 1. Hit/Evasion
    let hitRoll = rng.NextDouble()
    let hitChance = calculateHitChance attackerStats defenderStats

    if hitRoll > hitChance then
      {
        Amount = 0
        IsCritical = false
        IsEvaded = true
      }
    else
      // 2. Base Damage Calculation
      let baseDamage =
        skill.Formula
        |> ValueOption.map(evaluate attackerStats)
        |> ValueOption.defaultValue 0.0

      let elementalDamage, elementalResistance =
        match skill.ElementFormula with
        | ValueSome ef ->
          let dmg = evaluate attackerStats ef.Formula

          let res =
            defenderStats.ElementResistances
            |> HashMap.tryFind ef.Element
            |> Option.defaultValue 0.0

          (dmg, res)
        | ValueNone -> (0.0, 0.0)

      // 3. Critical Hit
      let critRoll = rng.NextDouble()
      let isCritical = critRoll < (float attackerStats.LK * 0.01)

      let critBonus =
        if isCritical then
          (baseDamage + elementalDamage) * 0.5 // 50% bonus for crits
        else
          0.0

      // 4. Elemental Resistance
      let elementalDamageAfterResistance =
        elementalDamage * (1.0 - elementalResistance)

      // 5. Combine
      let totalDamage = baseDamage + elementalDamageAfterResistance + critBonus

      // 6. Mitigation
      let damageAfterMitigation =
        match skill.DamageSource with
        | Physical -> totalDamage - (float defenderStats.DP)
        | Magical -> totalDamage - (float defenderStats.MD)

      let finalDamage = max 0 (int damageAfterMitigation)

      {
        Amount = finalDamage
        IsCritical = isCritical
        IsEvaded = false
      }

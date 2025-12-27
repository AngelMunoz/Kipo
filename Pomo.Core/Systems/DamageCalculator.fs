namespace Pomo.Core.Systems

open System
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Skill
open Pomo.Core.Systems.FormulaEvaluator

module DamageCalculator =
  open Pomo.Core.Domain.Core

  [<Struct>]
  type DamageResult = {
    Amount: int
    IsCritical: bool
    IsEvaded: bool
  }

  let private calculateHitChance
    (source: DamageSource)
    (attackerStats: DerivedStats)
    (defenderStats: DerivedStats)
    =
    // This logic is taken from the old prototype's EffectApplication.fs
    let attackerValue =
      match source with
      | Physical -> attackerStats.AC
      | Magical -> attackerStats.LK
      |> float

    let defenderValue =
      match source with
      | Physical -> defenderStats.HV
      | Magical -> defenderStats.LK
      |> float

    let baseHitChance = 0.5

    if attackerValue = 0.0 && defenderValue = 0.0 then
      1.0
    else
      let effectiveAttacker = max 0.0 attackerValue
      let effectiveDefender = max 0.0 defenderValue
      let statAdvantage = effectiveAttacker - effectiveDefender
      let divisor = 200.0
      let chance = baseHitChance + statAdvantage / divisor
      max 0.20 (min 0.80 chance)

  let calculateEffectDamage
    (attackerStats: DerivedStats)
    (defenderStats: DerivedStats)
    (formula: Formula.MathExpr)
    (damageType: Skill.DamageSource)
    (element: Element voption)
    =
    // 1. Base Damage Calculation (from effect)
    let baseDamage = evaluate attackerStats formula

    // 2. Elemental Resistance
    let elementalResistance =
      match element with
      | ValueSome element ->
        defenderStats.ElementResistances
        |> HashMap.tryFind element
        |> Option.defaultValue 0.0
      | ValueNone -> 0.0

    let damageAfterResistance = baseDamage * (1.0 - elementalResistance)

    // 3. Combine
    let totalDamage = damageAfterResistance

    // 4. Mitigation
    let damageAfterMitigation =
      match damageType with
      | Physical -> totalDamage - float defenderStats.DP
      | Magical -> totalDamage - float defenderStats.MD

    max 0 (int damageAfterMitigation)

  let caculateEffectRestoration
    (attackerStats: DerivedStats)
    (formula: Formula.MathExpr)
    =
    // 1. Base Restoration Calculation (from effect)
    let baseRestoration = evaluate attackerStats formula

    max 0 (int baseRestoration)

  let calculateFinalDamage
    (rng: Random)
    (attackerStats: DerivedStats)
    (defenderStats: DerivedStats)
    (skill: ActiveSkill)
    =
    // 1. Hit/Evasion
    let hitRoll = rng.NextDouble()

    let hitChance =
      calculateHitChance skill.DamageSource attackerStats defenderStats

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

      let struct (elementalDamage, elementalResistance) =
        match skill.ElementFormula with
        | ValueSome ef ->
          let dmg = evaluate attackerStats ef.Formula

          let res =
            defenderStats.ElementResistances
            |> HashMap.tryFind ef.Element
            |> Option.defaultValue 0.0

          struct (dmg, res)
        | ValueNone -> struct (0.0, 0.0)

      // 3. Critical Hit
      let critRoll = rng.NextDouble()
      let isCritical = critRoll < float attackerStats.LK * 0.01

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
        | Physical -> totalDamage - float defenderStats.DP
        | Magical -> totalDamage - float defenderStats.MD

      let finalDamage = max 0 (int damageAfterMitigation)

      {
        Amount = finalDamage
        IsCritical = isCritical
        IsEvaded = false
      }

  let calculateRawDamageSelfTarget
    (rng: Random)
    (attackerStats: DerivedStats)
    (defenderStats: DerivedStats)
    (skill: ActiveSkill)
    =
    // 1. Base Damage Calculation
    let baseDamage =
      skill.Formula
      |> ValueOption.map(evaluate attackerStats)
      |> ValueOption.defaultValue 0.0

    let elementalDamageAfterResistance =
      match skill.ElementFormula with
      | ValueSome ef ->
        let dmg = evaluate attackerStats ef.Formula

        let res =
          defenderStats.ElementResistances
          |> HashMap.tryFind ef.Element
          |> Option.defaultValue 0.0

        dmg * (1.0 - res)
      | ValueNone -> 0.0

    // Combine
    let finalDamage = baseDamage + elementalDamageAfterResistance

    {
      Amount = max 0 (int finalDamage)
      IsCritical = false
      IsEvaded = false
    }

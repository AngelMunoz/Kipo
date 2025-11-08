namespace Pomo.Core.Systems

open System
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
    (rng: System.Random)
    (attackerStats: DerivedStats)
    (defenderStats: DerivedStats)
    (skill: ActiveSkill)
    =
    // 1. Hit/Evasion
    let hitRoll = rng.NextDouble() * 100.0
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

      // TODO: This assumes the element is in the formula name (e.g., FireA).
      // A more robust solution might be needed if skills can have elements
      // independent of their formula variables.
      let elementalDamage =
        skill.ElementFormula
        |> ValueOption.map(_.Formula >> evaluate attackerStats)
        |> ValueOption.defaultValue 0.0

      // 3. Critical Hit
      let critRoll = rng.NextDouble() * 100.0
      let isCritical = critRoll < float attackerStats.LK * 0.01

      let critBonus =
        if isCritical then
          (baseDamage + elementalDamage) * 0.5 // 50% bonus for crits
        else
          0.0

      // 4. Elemental Resistance
      // This part is tricky without knowing the skill's element.
      // For now, we'll assume no resistance.
      // TODO: Determine skill's element to apply correct resistance.
      let damageAfterResistance = baseDamage + elementalDamage

      // 5. Combine
      let totalDamage = damageAfterResistance + critBonus

      // 6. Mitigation
      // TODO: Determine if damage is Physical or Magical to use DP or MD.
      // For now, we'll assume physical and use DP.
      let damageAfterMitigation = totalDamage - (float defenderStats.DP)

      let finalDamage = max 0 (int damageAfterMitigation)

      {
        Amount = finalDamage
        IsCritical = isCritical
        IsEvaded = false
      }

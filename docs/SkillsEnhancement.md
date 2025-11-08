Implementing Advanced Combat Logic

Our goal is to replace the placeholder logic in Combat.fs and implement the complete skill activation and damage calculation pipeline.

Step 1: Implement the Stat Calculation System

The BaseStats and DerivedStats components already exist in World.fs, but we need a system to keep DerivedStats up-to-date. This is a prerequisite for damage formulas.

1.  Create `Pomo.Core/Systems/StatSystem.fs`:
    - This system will subscribe to the EventBus.
    - It will listen for EntityCreated, BaseStatsChanged, and EffectApplied events.
    - When triggered, it will:
      - Read the entity's BaseStats.
      - Read all ActiveEffects for that entity.
      - Apply the modifiers from the effects to the base stats to calculate the DerivedStats.
      - Publish a StatsChanged event with the new DerivedStats.
2.  Update `StateUpdateSystem`:
    - Ensure the StateUpdateSystem handles the StatsChanged event and updates the DerivedStats cmap in the MutableWorld. (This might already be in place, but we should verify).

Step 2: Implement Skill Pre-Activation Checks

We need to add the validation logic from the old CommandHandler.fs to decide if a skill can be used. This logic should live in the CombatSystem before it proceeds with an AbilityIntent.

1.  Enhance `Pomo.Core/Systems/Combat.fs`:
    - Modify the handleAbilityIntent function.
    - Before handling the delivery (Projectile, Melee, etc.), perform a series of validation checks by reading the current World state for the casterId:
      - Check Resources: Does world.Resources[casterId] have enough MP/HP based on the activeSkill.Cost?
      - Check Cooldown: We need a new component in World.fs, like AbilityCooldowns: cmap<Guid<EntityId>, HashMap<int<SkillId>, TimeSpan>>, to track when skills can be used again. The check will compare the current game time to the
        value in this map.
      - Check State: Does the entity have a Stun or Silence effect in world.ActiveEffects[casterId]?
      - Check Range: Is world.Positions[targetId] within activeSkill.Range of world.Positions[casterId]?
    - If any check fails, publish a feedback event (e.g., ShowNotification("Not enough mana")).
    - If all checks pass, proceed with the existing delivery logic and apply the resource cost and cooldown.

Step 3: Implement the Damage Formula Pipeline

This is the core of the task. We'll replace the let damage = 10 placeholder in Combat.fs with a real calculation pipeline that uses the parsed formulas from Skill.fs.

1.  Create `Pomo.Core/Systems/FormulaEvaluator.fs`:

    - This new module will contain a function evaluate(expr: MathExpr, attackerStats: DerivedStats, defenderStats: DerivedStats) : float.
    - This function will recursively walk the MathExpr tree and substitute Var nodes with the corresponding values from the attackerStats and defenderStats.
    - This keeps the evaluation logic separate and testable.

2.  Enhance `Pomo.Core/Systems/Combat.fs`:
    - Modify the handleProjectileImpact (and later, handleMeleeHit) function.
    - Inside this handler:
      1.  Fetch the skill from the skillStore.
      2.  Fetch the DerivedStats for both the casterId and targetId from the world.
      3.  Hit/Evasion: Implement the calculateHitChance logic from the old EffectApplication.fs using attacker's AC/LK vs. defender's HV/LK. If it's a miss, publish a Missed event and stop.
      4.  Damage Calculation:
          - If there's a skill.Formula, call FormulaEvaluator.evaluate to get the base damage.
          - If there's an skill.ElementFormula, evaluate that too.
      5.  Critical Hit: Check for a critical hit based on the attacker's LK. Apply bonus damage if needed.
      6.  Apply Elemental Resistance to ElementFormula damage if applicable.
      7.  Combine the results to get the final damage amount.
      8.  Mitigation: Reduce the damage based on the target's DP (for physical) or MD (for magical).
      9.  Publish Events: Publish the final DamageDealt event with the calculated amount. Also publish CriticalHit or Evaded events for floating text.

This revised plan leverages the existing code and focuses on the missing pieces: the StatSystem, the validation logic, and the formula evaluation pipeline.

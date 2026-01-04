module DamageCalculatorTests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.UMX
open FSharp.Data.Adaptive
open FsCheck
open FsCheck.FSharp
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Formula
open Pomo.Core.Systems

// ============================================================================
// Test Helpers
// ============================================================================

/// Default stats (all zeros except HP/MS for valid entity)
let defaultStats: DerivedStats = {
  AP = 0
  AC = 0
  DX = 0
  MP = 0
  MA = 0
  MD = 0
  WT = 0
  DA = 0
  LK = 0
  HP = 100
  DP = 0
  HV = 0
  MS = 100
  HPRegen = 0
  MPRegen = 0
  ElementAttributes = HashMap.empty
  ElementResistances = HashMap.empty
}

/// Creates DerivedStats with specific overrides
let withAC ac stats = { stats with AC = ac }
let withHV hv stats = { stats with HV = hv }
let withLK lk stats = { stats with LK = lk }
let withDP dp stats = { stats with DP = dp }
let withMD md stats = { stats with MD = md }

/// Creates a minimal ActiveSkill for testing damage calculation
let createSkill (damageSource: DamageSource) (baseDamage: float) : ActiveSkill = {
  Id = %1
  Name = "TestSkill"
  Description = ""
  Intent = Offensive
  DamageSource = damageSource
  Cost = ValueNone
  Cooldown = ValueNone
  CastingTime = ValueNone
  Targeting = TargetEntity
  Range = ValueSome 100.0f
  Delivery = Delivery.Instant
  Area = SkillArea.Point
  ChargePhase = ValueNone
  Formula = ValueSome(Const baseDamage)
  ElementFormula = ValueNone
  Effects = [||]
  Origin = Caster
  CastVisuals = VisualManifest.empty
  ImpactVisuals = VisualManifest.empty
}

/// Runs many trials and returns the observed rate of a boolean condition
let measureRate (trials: int) (action: unit -> bool) : float =
  let successes =
    [| for _ in 1..trials -> if action() then 1 else 0 |] |> Array.sum

  float successes / float trials

/// Statistical tolerance for probability tests (based on sample size)
let tolerance100 = 0.15
let tolerance1k = 0.05

// ============================================================================
// Hit Chance Unit Tests
// ============================================================================

[<TestClass>]
type HitChanceTests() =

  [<TestMethod>]
  member _.``equal stats give 50 percent hit chance``() =
    let rng = Random(42)
    let attacker = defaultStats |> withAC 50
    let defender = defaultStats |> withHV 50
    let skill = createSkill Physical 100.0

    let hitRate =
      measureRate 100 (fun () ->
        let result =
          DamageCalculator.calculateFinalDamage rng attacker defender skill

        not result.IsEvaded)

    Assert.IsTrue(
      abs(hitRate - 0.5) < tolerance100,
      sprintf "Hit rate was %.3f, expected ~0.5 (equal AC/HV stats)" hitRate
    )

  [<TestMethod>]
  member _.``higher AC increases hit chance``() =
    let rng = Random(42)
    let attacker = defaultStats |> withAC 80
    let defender = defaultStats |> withHV 50
    let skill = createSkill Physical 100.0

    let hitRate =
      measureRate 100 (fun () ->
        let result =
          DamageCalculator.calculateFinalDamage rng attacker defender skill

        not result.IsEvaded)

    // Expected: 0.5 + 30/200 = 0.65
    Assert.IsTrue(
      abs(hitRate - 0.65) < tolerance100,
      sprintf "Hit rate was %.3f, expected ~0.65 (+30 AC advantage)" hitRate
    )

  [<TestMethod>]
  member _.``higher HV decreases hit chance``() =
    let rng = Random(42)
    let attacker = defaultStats |> withAC 50
    let defender = defaultStats |> withHV 80
    let skill = createSkill Physical 100.0

    let hitRate =
      measureRate 100 (fun () ->
        let result =
          DamageCalculator.calculateFinalDamage rng attacker defender skill

        not result.IsEvaded)

    // Expected: 0.5 - 30/200 = 0.35
    Assert.IsTrue(
      abs(hitRate - 0.35) < tolerance100,
      sprintf "Hit rate was %.3f, expected ~0.35 (+30 HV defense)" hitRate
    )

  [<TestMethod>]
  member _.``hit chance clamps to minimum 20 percent``() =
    let rng = Random(42)
    let attacker = defaultStats
    let defender = defaultStats |> withHV 200
    let skill = createSkill Physical 100.0

    let hitRate =
      measureRate 100 (fun () ->
        let result =
          DamageCalculator.calculateFinalDamage rng attacker defender skill

        not result.IsEvaded)

    Assert.IsTrue(
      abs(hitRate - 0.20) < tolerance100,
      sprintf "Hit rate was %.3f, expected ~0.20 (minimum clamp)" hitRate
    )

  [<TestMethod>]
  member _.``hit chance clamps to maximum 80 percent``() =
    let rng = Random(42)
    let attacker = defaultStats |> withAC 200
    let defender = defaultStats
    let skill = createSkill Physical 100.0

    let hitRate =
      measureRate 100 (fun () ->
        let result =
          DamageCalculator.calculateFinalDamage rng attacker defender skill

        not result.IsEvaded)

    Assert.IsTrue(
      abs(hitRate - 0.80) < tolerance100,
      sprintf "Hit rate was %.3f, expected ~0.80 (maximum clamp)" hitRate
    )

  [<TestMethod>]
  member _.``magical skills use LK for hit calculation``() =
    let rng = Random(42)
    let attacker = defaultStats |> withLK 80
    let defender = defaultStats |> withLK 50
    let skill = createSkill Magical 100.0

    let hitRate =
      measureRate 100 (fun () ->
        let result =
          DamageCalculator.calculateFinalDamage rng attacker defender skill

        not result.IsEvaded)

    // Expected: 0.5 + 30/200 = 0.65
    Assert.IsTrue(
      abs(hitRate - 0.65) < tolerance100,
      sprintf "Hit rate was %.3f, expected ~0.65 (magical uses LK)" hitRate
    )

// ============================================================================
// Critical Hit Tests
// ============================================================================

[<TestClass>]
type CriticalHitTests() =

  [<TestMethod>]
  member _.``crit chance is 1 percent per LK point``() =
    let rng = Random(42)
    let attacker = defaultStats |> withAC 100 |> withLK 20
    let defender = defaultStats
    let skill = createSkill Physical 100.0

    // Measure hit rate first
    let hitsOnly =
      measureRate 100 (fun () ->
        let result =
          DamageCalculator.calculateFinalDamage rng attacker defender skill

        not result.IsEvaded)

    // Measure crits among all trials
    let critsTotal =
      measureRate 100 (fun () ->
        let result =
          DamageCalculator.calculateFinalDamage rng attacker defender skill

        result.IsCritical && not result.IsEvaded)

    // Crit rate among hits = critsTotal / hitsOnly
    let critAmongHits = critsTotal / hitsOnly

    Assert.IsTrue(
      abs(critAmongHits - 0.2) < tolerance100,
      sprintf
        "Crit rate among hits was %.3f, expected ~0.2 (20 LK)"
        critAmongHits
    )

  [<TestMethod>]
  member _.``zero LK gives zero crits``() =
    let rng = Random(42)
    let attacker = defaultStats |> withAC 100
    let defender = defaultStats
    let skill = createSkill Physical 100.0

    let critRate =
      measureRate 100 (fun () ->
        let result =
          DamageCalculator.calculateFinalDamage rng attacker defender skill

        result.IsCritical)

    Assert.AreEqual(0.0, critRate, "Expected zero crits with 0 LK")

  [<TestMethod>]
  member _.``crits deal 50 percent bonus damage``() =
    let rng = Random(42)
    let attacker = defaultStats |> withLK 100 |> withAC 100
    let defender = defaultStats
    let skill = createSkill Physical 100.0

    let result =
      DamageCalculator.calculateFinalDamage rng attacker defender skill

    Assert.IsTrue(result.IsCritical, "Should always crit with 100 LK")

    Assert.AreEqual(
      150,
      result.Amount,
      "Crit should deal 150 percent damage (100 + 50 percent bonus)"
    )

// ============================================================================
// Damage Calculation Tests
// ============================================================================

[<TestClass>]
type DamageCalculationTests() =

  [<TestMethod>]
  member _.``physical damage reduced by DP``() =
    let rng = Random(42)
    let attacker = defaultStats |> withAC 100
    let defender = defaultStats |> withDP 30
    let skill = createSkill Physical 100.0

    let result =
      DamageCalculator.calculateFinalDamage rng attacker defender skill

    // base 100 - dp 30 = 70
    Assert.AreEqual(70, result.Amount)

  [<TestMethod>]
  member _.``magical damage reduced by MD``() =
    let rng = Random(42)
    let attacker = defaultStats |> withLK 100
    let defender = defaultStats |> withMD 25
    let skill = createSkill Magical 100.0

    let result =
      DamageCalculator.calculateFinalDamage rng attacker defender skill

    // base 100, 100 percent crit = 150, - md 25 = 125
    Assert.AreEqual(125, result.Amount)

  [<TestMethod>]
  member _.``damage cannot go below zero``() =
    let rng = Random(42)
    let attacker = defaultStats |> withAC 100
    let defender = defaultStats |> withDP 500
    let skill = createSkill Physical 50.0

    let result =
      DamageCalculator.calculateFinalDamage rng attacker defender skill

    Assert.IsTrue(result.Amount >= 0, "Damage should never be negative")

// ============================================================================
// Property-Based Tests with Distribution Observation
// ============================================================================

[<TestClass>]
type PropertyTests() =

  let statArb = Arb.fromGen(Gen.choose(0, 100))
  let twoStats = Arb.zip(statArb, statArb)

  [<TestMethod>]
  member _.``hit rate follows formula within tolerance``() =
    let rng = Random()

    Prop.forAll twoStats (fun (attackerAC, defenderHV) ->
      let attacker = defaultStats |> withAC attackerAC
      let defender = defaultStats |> withHV defenderHV
      let skill = createSkill Physical 100.0

      // Calculate expected hit chance from formula
      let expectedHitChance =
        let statAdvantage = float attackerAC - float defenderHV
        let raw = 0.5 + statAdvantage / 200.0
        max 0.20 (min 0.80 raw)

      // Measure actual hit rate
      let actualHitRate =
        measureRate 100 (fun () ->
          let result =
            DamageCalculator.calculateFinalDamage rng attacker defender skill

          not result.IsEvaded)

      abs(actualHitRate - expectedHitChance) < 0.2
      |> Prop.classify (expectedHitChance <= 0.20) "low chance (<=20%)"
      |> Prop.classify
        (expectedHitChance > 0.20 && expectedHitChance < 0.80)
        "medium chance (20-80%)"
      |> Prop.classify (expectedHitChance >= 0.80) "high chance (>=80%)"
      |> Prop.collect(sprintf "expected %.0f%%" (expectedHitChance * 100.0)))
    |> Check.VerboseThrowOnFailure

  [<TestMethod>]
  member _.``damage is always non-negative``() =
    let damageArb = Arb.fromGen(Gen.choose(0, 200))
    let mitigationArb = Arb.fromGen(Gen.choose(0, 300))
    let combined = Arb.zip(damageArb, mitigationArb)

    let rng = Random(42)

    Prop.forAll combined (fun (baseDmg, mitigation) ->
      let attacker = defaultStats |> withAC 100
      let defender = defaultStats |> withDP mitigation
      let skill = createSkill Physical (float baseDmg)

      let result =
        DamageCalculator.calculateFinalDamage rng attacker defender skill

      result.Amount >= 0)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``crits always deal more damage than non-crits``() =
    let damageArb = Arb.fromGen(Gen.choose(10, 100))

    Prop.forAll damageArb (fun baseDmg ->
      let rngCrit = Random(42)
      let rngNoCrit = Random(42)

      let attackerCrit = defaultStats |> withAC 100 |> withLK 100
      let attackerNoCrit = defaultStats |> withAC 100
      let defender = defaultStats
      let skill = createSkill Physical (float baseDmg)

      let critResult =
        DamageCalculator.calculateFinalDamage
          rngCrit
          attackerCrit
          defender
          skill

      let noCritResult =
        DamageCalculator.calculateFinalDamage
          rngNoCrit
          attackerNoCrit
          defender
          skill

      critResult.IsCritical
      && not noCritResult.IsCritical
      && critResult.Amount > noCritResult.Amount)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``evaded attacks deal zero damage``() =
    let rng = Random()
    let attacker = defaultStats
    let defender = defaultStats |> withHV 200
    let skill = createSkill Physical 100.0

    let allMissesZeroDamage =
      [|
        for _ in 1..100 ->
          let result =
            DamageCalculator.calculateFinalDamage rng attacker defender skill

          if result.IsEvaded then result.Amount = 0 else true
      |]
      |> Array.forall id

    Assert.IsTrue(
      allMissesZeroDamage,
      "All evaded attacks should deal 0 damage"
    )

// ============================================================================
// End-to-End Integration Tests
// ============================================================================

/// Helper to create a skill with elemental damage
let createElementalSkill
  (damageSource: DamageSource)
  (baseDamage: float)
  (element: Element)
  (elementDamage: float)
  : ActiveSkill =
  {
    Id = %1
    Name = "ElementalSkill"
    Description = ""
    Intent = Offensive
    DamageSource = damageSource
    Cost = ValueNone
    Cooldown = ValueNone
    CastingTime = ValueNone
    Targeting = TargetEntity
    Range = ValueSome 100.0f
    Delivery = Delivery.Instant
    Area = SkillArea.Point
    ChargePhase = ValueNone
    Formula = ValueSome(Const baseDamage)
    ElementFormula =
      ValueSome {
        Element = element
        Formula = Const elementDamage
      }
    Effects = [||]
    Origin = Caster
    CastVisuals = VisualManifest.empty
    ImpactVisuals = VisualManifest.empty
  }

/// Helper to add element resistance to stats
let withFireResist resist stats = {
  stats with
      ElementResistances = HashMap.single Element.Fire resist
}

[<TestClass>]
type EndToEndTests() =

  // -------------------------------------------------------------------------
  // Edge Cases (not covered by property tests)
  // -------------------------------------------------------------------------

  [<TestMethod>]
  member _.``100 percent elemental resist negates all elemental damage``() =
    let rng = Random(42)
    let attacker = defaultStats |> withAC 100 |> withLK 0
    let defender = defaultStats |> withFireResist 1.0 // 100% resist
    let skill = createElementalSkill Physical 100.0 Element.Fire 100.0

    let result =
      DamageCalculator.calculateFinalDamage rng attacker defender skill

    // 100 base + 100 fire * 0 = 100
    Assert.AreEqual(
      100,
      result.Amount,
      "100% resist should negate all elemental"
    )

  [<TestMethod>]
  member _.``no formula skill deals zero damage``() =
    let rng = Random(42)
    let attacker = defaultStats |> withAC 100
    let defender = defaultStats

    let skill = {
      createSkill Physical 0.0 with
          Formula = ValueNone
    }

    let result =
      DamageCalculator.calculateFinalDamage rng attacker defender skill

    Assert.AreEqual(0, result.Amount, "No formula = 0 damage")

  [<TestMethod>]
  member _.``mitigation cannot make damage negative``() =
    let rng = Random(42)
    let attacker = defaultStats |> withAC 100
    let defender = defaultStats |> withDP 1000 // Massive DP
    let skill = createSkill Physical 50.0

    let result =
      DamageCalculator.calculateFinalDamage rng attacker defender skill

    Assert.AreEqual(0, result.Amount, "Damage floored at 0")
    Assert.IsFalse(result.IsEvaded, "Not evaded, just mitigated to 0")

// ============================================================================
// Property-Based End-to-End Tests
// ============================================================================

/// Generators for property tests
let damageGen = Gen.choose(10, 200)
let resistGen = Gen.map (fun x -> float x / 100.0) (Gen.choose(0, 100))
let mitigationGen = Gen.choose(0, 50)

[<TestClass>]
type EndToEndPropertyTests() =

  [<TestMethod>]
  member _.``elemental resistance always reduces or equals total damage``() =
    let gen = Gen.map3 (fun a b c -> (a, b, c)) damageGen damageGen resistGen
    let arb = Arb.fromGen gen

    Prop.forAll arb (fun (baseDmg, elemDmg, resist) ->
      let rng1 = Random(42)
      let rng2 = Random(42)

      let attacker = defaultStats |> withAC 100 |> withLK 0
      let defenderNoResist = defaultStats
      let defenderWithResist = defaultStats |> withFireResist resist

      let skillElem =
        createElementalSkill
          Physical
          (float baseDmg)
          Element.Fire
          (float elemDmg)

      let resultNoResist =
        DamageCalculator.calculateFinalDamage
          rng1
          attacker
          defenderNoResist
          skillElem

      let resultWithResist =
        DamageCalculator.calculateFinalDamage
          rng2
          attacker
          defenderWithResist
          skillElem

      // With resistance, damage should be <= without resistance
      resultWithResist.Amount <= resultNoResist.Amount)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``critical hits always deal more or equal damage than non-crits``() =
    let gen = Gen.map2 (fun a b -> (a, b)) damageGen damageGen
    let arb = Arb.fromGen gen

    Prop.forAll arb (fun (baseDmg, elemDmg) ->
      let rngCrit = Random(42)
      let rngNoCrit = Random(42)

      let attackerCrit = defaultStats |> withAC 100 |> withLK 100
      let attackerNoCrit = defaultStats |> withAC 100 |> withLK 0
      let defender = defaultStats

      let skill =
        createElementalSkill
          Physical
          (float baseDmg)
          Element.Fire
          (float elemDmg)

      let critResult =
        DamageCalculator.calculateFinalDamage
          rngCrit
          attackerCrit
          defender
          skill

      let noCritResult =
        DamageCalculator.calculateFinalDamage
          rngNoCrit
          attackerNoCrit
          defender
          skill

      critResult.Amount >= noCritResult.Amount)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``mitigation always reduces or equals total damage``() =
    let gen = Gen.map2 (fun a b -> (a, b)) damageGen mitigationGen
    let arb = Arb.fromGen gen

    Prop.forAll arb (fun (baseDmg, mitigation) ->
      let rng1 = Random(42)
      let rng2 = Random(42)

      let attacker = defaultStats |> withAC 100 |> withLK 0
      let defenderNoMit = defaultStats
      let defenderWithMit = defaultStats |> withDP mitigation

      let skill = createSkill Physical (float baseDmg)

      let resultNoMit =
        DamageCalculator.calculateFinalDamage rng1 attacker defenderNoMit skill

      let resultWithMit =
        DamageCalculator.calculateFinalDamage
          rng2
          attacker
          defenderWithMit
          skill

      resultWithMit.Amount <= resultNoMit.Amount)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``full pipeline formula is mathematically correct``() =
    let gen =
      Gen.map4
        (fun a b c d -> (a, b, c, d))
        damageGen
        damageGen
        resistGen
        mitigationGen

    let arb = Arb.fromGen gen

    Prop.forAll arb (fun (baseDmg, elemDmg, resist, mitigation) ->
      let rng = Random(42)
      let attacker = defaultStats |> withAC 100 |> withLK 100 // Always crit
      let defender = defaultStats |> withFireResist resist |> withDP mitigation

      let skill =
        createElementalSkill
          Physical
          (float baseDmg)
          Element.Fire
          (float elemDmg)

      let result =
        DamageCalculator.calculateFinalDamage rng attacker defender skill

      // Calculate expected damage manually
      let base' = float baseDmg
      let elem = float elemDmg
      let critBonus = (base' + elem) * 0.5
      let elemAfterResist = elem * (1.0 - resist)
      let total = base' + elemAfterResist + critBonus
      let afterMit = total - float mitigation
      let expected = max 0 (int afterMit)

      result.Amount = expected
      |> Prop.classify (resist > 0.5) "high resist (>50%)"
      |> Prop.classify (mitigation > 25) "high mitigation (>25)")
    |> Check.QuickThrowOnFailure

module ProjectionsTests

open System
open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.Stores
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Skill
open Pomo.Core.Projections

// Module access for World creation (avoids interface shadowing)
let private createWorld() =
  Pomo.Core.Domain.World.create(Random(42))

// ============================================================================
// Fake Services
// ============================================================================

/// Creates a fake ItemStore from a list of items
let createFakeItemStore
  (items: (int<ItemId> * ItemDefinition) list)
  : ItemStore =
  let map = items |> HashMap.ofList

  { new ItemStore with
      member _.find id = HashMap.find id map
      member _.tryFind id = HashMap.tryFindV id map
      member _.all() = map |> HashMap.toValueSeq
  }

/// Creates a fake ItemStore with no items
let emptyItemStore: ItemStore = createFakeItemStore []

/// Creates a dummy PhysicsCache that returns empty snapshots
let dummyPhysicsCache: Projections.PhysicsCache.PhysicsCacheService =
  { new Projections.PhysicsCache.PhysicsCacheService with
      member _.GetMovementSnapshot _ = Projections.MovementSnapshot.Empty
      member _.GetMovement3DSnapshot _ = Projections.Movement3DSnapshot.Empty
      member _.RefreshAllCaches() = ()
  }

// ============================================================================
// Test Data Factories
// ============================================================================

/// Creates a test item with stat modifiers
let createWearableItem
  (id: int)
  (name: string)
  (slot: Slot)
  (stats: StatModifier array)
  : ItemDefinition =
  {
    Id = UMX.tag<ItemId> id
    Name = name
    Weight = 1
    Kind = Wearable { Slot = slot; Stats = stats }
  }

/// Creates base stats for testing
let createBaseStats power magic sense charm : BaseStats = {
  Power = power
  Magic = magic
  Sense = sense
  Charm = charm
}

/// Creates a buff effect with stat modifiers
let createBuffEffect (name: string) (modifiers: StatModifier array) : Effect = {
  Name = name
  Kind = EffectKind.Buff
  DamageSource = DamageSource.Physical
  Stacking = StackingRule.RefreshDuration
  Duration = Duration.Timed(TimeSpan.FromSeconds(30.0))
  Visuals = VisualManifest.empty
  Modifiers = modifiers |> Array.map EffectModifier.StaticMod
}

/// Creates a debuff effect with stat modifiers
let createDebuffEffect (name: string) (modifiers: StatModifier array) : Effect = {
  Name = name
  Kind = EffectKind.Debuff
  DamageSource = DamageSource.Physical
  Stacking = StackingRule.RefreshDuration
  Duration = Duration.Timed(TimeSpan.FromSeconds(30.0))
  Visuals = VisualManifest.empty
  Modifiers = modifiers |> Array.map EffectModifier.StaticMod
}

/// Creates an active effect instance from an effect definition
let createActiveEffect
  (sourceEntity: Guid<EntityId>)
  (targetEntity: Guid<EntityId>)
  (effect: Effect)
  : ActiveEffect =
  {
    Id = Guid.NewGuid() |> UMX.tag<EffectId>
    SourceEffect = effect
    SourceEntity = sourceEntity
    TargetEntity = targetEntity
    StartTime = TimeSpan.Zero
    StackCount = 1
  }

/// Default base stats (all 10)
let defaultBaseStats = createBaseStats 10 10 10 10

/// Creates a test world with a single entity configured with base stats
let createTestWorldWithEntity
  (entityId: Guid<EntityId>)
  (baseStats: BaseStats)
  =
  let struct (mutable', view) = createWorld()

  // Add entity with base stats
  transact(fun () ->
    mutable'.EntityExists.Add entityId |> ignore
    mutable'.BaseStats.Add(entityId, baseStats) |> ignore)

  struct (mutable', view)

/// Creates an item instance and equips it to an entity
let equipItem
  (mutable': Pomo.Core.Domain.World.MutableWorld)
  (entityId: Guid<EntityId>)
  (slot: Slot)
  (itemId: int<ItemId>)
  : Guid<ItemInstanceId> =
  let instanceId = Guid.NewGuid() |> UMX.tag<ItemInstanceId>

  // Add item instance
  mutable'.ItemInstances.TryAdd(
    instanceId,
    {
      InstanceId = instanceId
      ItemId = itemId
      UsesLeft = ValueNone
    }
  )
  |> ignore

  // Equip to entity
  transact(fun () ->
    let currentEquipment =
      match mutable'.EquippedItems.Value |> HashMap.tryFindV entityId with
      | ValueSome eq -> eq
      | ValueNone -> HashMap.empty

    let newEquipment = currentEquipment.Add(slot, instanceId)
    mutable'.EquippedItems.Add(entityId, newEquipment) |> ignore)

  instanceId

/// Applies an active effect to an entity
let applyEffect
  (mutable': Pomo.Core.Domain.World.MutableWorld)
  (entityId: Guid<EntityId>)
  (effect: ActiveEffect)
  =
  transact(fun () ->
    let currentEffects =
      match mutable'.ActiveEffects.Value |> HashMap.tryFindV entityId with
      | ValueSome effects -> effects
      | ValueNone -> IndexList.empty

    let newEffects = currentEffects |> IndexList.add effect
    mutable'.ActiveEffects.Add(entityId, newEffects) |> ignore)

// ============================================================================
// End-to-End Tests: Equipment
// ============================================================================

[<TestClass>]
type EquipmentTests() =

  [<TestMethod>]
  member _.``equipment bonus adds to derived stats``() =
    let entityId = Guid.NewGuid() |> UMX.tag<EntityId>
    let baseStats = createBaseStats 10 0 0 0
    let struct (mutable', world) = createTestWorldWithEntity entityId baseStats

    // Create sword with +10 AP
    let swordId = 1<ItemId>

    let sword =
      createWearableItem 1 "Test Sword" Weapon [| Additive(AP, 10.0) |]

    let itemStore = createFakeItemStore [ (swordId, sword) ]

    // Equip the sword
    equipItem mutable' entityId Weapon swordId |> ignore

    let projections = Projections.create(itemStore, world, dummyPhysicsCache)

    let derivedStats =
      projections.DerivedStats |> AMap.find entityId |> AVal.force

    // Base AP = 20, equipment bonus = +10, total = 30
    Assert.AreEqual(
      30,
      derivedStats.AP,
      "AP should be base (20) + equipment (10)"
    )

  [<TestMethod>]
  member _.``multiple equipment pieces stack correctly``() =
    let entityId = Guid.NewGuid() |> UMX.tag<EntityId>
    let baseStats = createBaseStats 10 0 0 10
    let struct (mutable', world) = createTestWorldWithEntity entityId baseStats

    // Create sword with +10 AP and shield with +5 DP
    let swordId = 1<ItemId>

    let sword =
      createWearableItem 1 "Test Sword" Weapon [| Additive(AP, 10.0) |]

    let shieldId = 2<ItemId>

    let shield =
      createWearableItem 2 "Test Shield" Shield [| Additive(DP, 5.0) |]

    let itemStore = createFakeItemStore [ (swordId, sword); (shieldId, shield) ]

    // Equip both
    equipItem mutable' entityId Weapon swordId |> ignore
    equipItem mutable' entityId Shield shieldId |> ignore

    let projections = Projections.create(itemStore, world, dummyPhysicsCache)

    let derivedStats =
      projections.DerivedStats |> AMap.find entityId |> AVal.force

    // Base AP = 20, sword = +10, total = 30
    Assert.AreEqual(30, derivedStats.AP, "AP with sword equipped")
    // Base DP = 22 (Charm 10), shield = +5, total = 27
    Assert.AreEqual(27, derivedStats.DP, "DP with shield equipped")

  [<TestMethod>]
  member _.``multiplicative equipment bonus multiplies stat``() =
    let entityId = Guid.NewGuid() |> UMX.tag<EntityId>
    let baseStats = createBaseStats 10 0 0 0
    let struct (mutable', world) = createTestWorldWithEntity entityId baseStats

    // Create ring with 1.5x AP multiplier
    let ringId = 1<ItemId>

    let ring =
      createWearableItem 1 "Power Ring" Accessory [| Multiplicative(AP, 1.5) |]

    let itemStore = createFakeItemStore [ (ringId, ring) ]
    equipItem mutable' entityId Accessory ringId |> ignore

    let projections = Projections.create(itemStore, world, dummyPhysicsCache)

    let derivedStats =
      projections.DerivedStats |> AMap.find entityId |> AVal.force

    // Base AP = 20, * 1.5 = 30
    Assert.AreEqual(30, derivedStats.AP, "AP should be base (20) * 1.5")

// ============================================================================
// End-to-End Tests: Active Effects (Buffs & Debuffs)
// ============================================================================

[<TestClass>]
type ActiveEffectTests() =

  [<TestMethod>]
  member _.``buff effect adds to derived stats``() =
    let entityId = Guid.NewGuid() |> UMX.tag<EntityId>
    let sourceId = Guid.NewGuid() |> UMX.tag<EntityId>
    let baseStats = createBaseStats 10 0 0 0
    let struct (mutable', world) = createTestWorldWithEntity entityId baseStats

    // Create "Strength" buff that adds +15 AP
    let strengthBuff = createBuffEffect "Strength" [| Additive(AP, 15.0) |]
    let activeEffect = createActiveEffect sourceId entityId strengthBuff
    applyEffect mutable' entityId activeEffect

    let projections =
      Projections.create(emptyItemStore, world, dummyPhysicsCache)

    let derivedStats =
      projections.DerivedStats |> AMap.find entityId |> AVal.force

    // Base AP = 20, buff = +15, total = 35
    Assert.AreEqual(35, derivedStats.AP, "AP should be base (20) + buff (15)")

  [<TestMethod>]
  member _.``debuff effect reduces derived stats``() =
    let entityId = Guid.NewGuid() |> UMX.tag<EntityId>
    let sourceId = Guid.NewGuid() |> UMX.tag<EntityId>
    let baseStats = createBaseStats 10 0 0 0
    let struct (mutable', world) = createTestWorldWithEntity entityId baseStats

    // Create "Weakness" debuff that reduces AP by 5
    let weaknessDebuff = createDebuffEffect "Weakness" [| Additive(AP, -5.0) |]
    let activeEffect = createActiveEffect sourceId entityId weaknessDebuff
    applyEffect mutable' entityId activeEffect

    let projections =
      Projections.create(emptyItemStore, world, dummyPhysicsCache)

    let derivedStats =
      projections.DerivedStats |> AMap.find entityId |> AVal.force

    // Base AP = 20, debuff = -5, total = 15
    Assert.AreEqual(15, derivedStats.AP, "AP should be base (20) - debuff (5)")

  [<TestMethod>]
  member _.``multiple effects stack correctly``() =
    let entityId = Guid.NewGuid() |> UMX.tag<EntityId>
    let sourceId = Guid.NewGuid() |> UMX.tag<EntityId>
    let baseStats = createBaseStats 10 10 0 0
    let struct (mutable', world) = createTestWorldWithEntity entityId baseStats

    // Apply two buffs: +10 AP and +20 MA
    let strengthBuff = createBuffEffect "Strength" [| Additive(AP, 10.0) |]
    let magicBuff = createBuffEffect "Magic Boost" [| Additive(MA, 20.0) |]

    applyEffect
      mutable'
      entityId
      (createActiveEffect sourceId entityId strengthBuff)

    applyEffect
      mutable'
      entityId
      (createActiveEffect sourceId entityId magicBuff)

    let projections =
      Projections.create(emptyItemStore, world, dummyPhysicsCache)

    let derivedStats =
      projections.DerivedStats |> AMap.find entityId |> AVal.force

    // Base AP = 20, buff = +10, total = 30
    Assert.AreEqual(30, derivedStats.AP, "AP with strength buff")
    // Base MA = 20, buff = +20, total = 40
    Assert.AreEqual(40, derivedStats.MA, "MA with magic buff")

  [<TestMethod>]
  member _.``multiplicative buff effect multiplies stat``() =
    let entityId = Guid.NewGuid() |> UMX.tag<EntityId>
    let sourceId = Guid.NewGuid() |> UMX.tag<EntityId>
    let baseStats = createBaseStats 10 0 0 0
    let struct (mutable', world) = createTestWorldWithEntity entityId baseStats

    // Create "Empower" buff that doubles AP
    let empowerBuff = createBuffEffect "Empower" [| Multiplicative(AP, 2.0) |]
    let activeEffect = createActiveEffect sourceId entityId empowerBuff
    applyEffect mutable' entityId activeEffect

    let projections =
      Projections.create(emptyItemStore, world, dummyPhysicsCache)

    let derivedStats =
      projections.DerivedStats |> AMap.find entityId |> AVal.force

    // Base AP = 20, * 2.0 = 40
    Assert.AreEqual(40, derivedStats.AP, "AP should be base (20) * 2.0")

// ============================================================================
// End-to-End Tests: Equipment + Effects Combined
// ============================================================================

[<TestClass>]
type CombinedTests() =

  [<TestMethod>]
  member _.``equipment and buff effects combine correctly``() =
    let entityId = Guid.NewGuid() |> UMX.tag<EntityId>
    let sourceId = Guid.NewGuid() |> UMX.tag<EntityId>
    let baseStats = createBaseStats 10 0 0 0
    let struct (mutable', world) = createTestWorldWithEntity entityId baseStats

    // Equip sword with +10 AP
    let swordId = 1<ItemId>

    let sword =
      createWearableItem 1 "Test Sword" Weapon [| Additive(AP, 10.0) |]

    let itemStore = createFakeItemStore [ (swordId, sword) ]
    equipItem mutable' entityId Weapon swordId |> ignore

    // Apply buff with +5 AP
    let strengthBuff = createBuffEffect "Strength" [| Additive(AP, 5.0) |]

    applyEffect
      mutable'
      entityId
      (createActiveEffect sourceId entityId strengthBuff)

    let projections = Projections.create(itemStore, world, dummyPhysicsCache)

    let derivedStats =
      projections.DerivedStats |> AMap.find entityId |> AVal.force

    // Base AP = 20, equipment = +10, buff = +5, total = 35
    Assert.AreEqual(35, derivedStats.AP, "AP = base + sword + buff")

  [<TestMethod>]
  member _.``equipment bonus plus debuff penalty balance out``() =
    let entityId = Guid.NewGuid() |> UMX.tag<EntityId>
    let sourceId = Guid.NewGuid() |> UMX.tag<EntityId>
    let baseStats = createBaseStats 10 0 0 0
    let struct (mutable', world) = createTestWorldWithEntity entityId baseStats

    // Equip sword with +10 AP
    let swordId = 1<ItemId>

    let sword =
      createWearableItem 1 "Test Sword" Weapon [| Additive(AP, 10.0) |]

    let itemStore = createFakeItemStore [ (swordId, sword) ]
    equipItem mutable' entityId Weapon swordId |> ignore

    // Apply debuff that reduces AP by 10
    let weaknessDebuff = createDebuffEffect "Weakness" [| Additive(AP, -10.0) |]

    applyEffect
      mutable'
      entityId
      (createActiveEffect sourceId entityId weaknessDebuff)

    let projections = Projections.create(itemStore, world, dummyPhysicsCache)

    let derivedStats =
      projections.DerivedStats |> AMap.find entityId |> AVal.force

    // Base AP = 20, equipment = +10, debuff = -10, total = 20 (back to base)
    Assert.AreEqual(
      20,
      derivedStats.AP,
      "Equipment bonus should be cancelled by debuff"
    )

  [<TestMethod>]
  member _.``full loadout scenario - multiple equipment and effects``() =
    let entityId = Guid.NewGuid() |> UMX.tag<EntityId>
    let sourceId = Guid.NewGuid() |> UMX.tag<EntityId>
    // Warrior with 20 Power, 5 Magic, 10 Sense, 15 Charm
    let baseStats = createBaseStats 20 5 10 15
    let struct (mutable', world) = createTestWorldWithEntity entityId baseStats

    // Equipment: Sword (+15 AP), Armor (+10 DP), Ring (1.2x LK)
    let swordId, armorId, ringId = 1<ItemId>, 2<ItemId>, 3<ItemId>

    let sword =
      createWearableItem 1 "Warrior Sword" Weapon [| Additive(AP, 15.0) |]

    let armor =
      createWearableItem 2 "Plate Armor" Chest [| Additive(DP, 10.0) |]

    let ring =
      createWearableItem 3 "Lucky Ring" Accessory [| Multiplicative(LK, 1.2) |]

    let itemStore =
      createFakeItemStore [ (swordId, sword); (armorId, armor); (ringId, ring) ]

    equipItem mutable' entityId Weapon swordId |> ignore
    equipItem mutable' entityId Chest armorId |> ignore
    equipItem mutable' entityId Accessory ringId |> ignore

    // Buffs: Battle Cry (+10 AP), Bless (+5 DP)
    let battleCry = createBuffEffect "Battle Cry" [| Additive(AP, 10.0) |]
    let bless = createBuffEffect "Bless" [| Additive(DP, 5.0) |]

    applyEffect
      mutable'
      entityId
      (createActiveEffect sourceId entityId battleCry)

    applyEffect mutable' entityId (createActiveEffect sourceId entityId bless)

    // Debuff: Poison (-3 DP per tick simulated as static)
    let poison = createDebuffEffect "Poison" [| Additive(DP, -3.0) |]
    applyEffect mutable' entityId (createActiveEffect sourceId entityId poison)

    let projections = Projections.create(itemStore, world, dummyPhysicsCache)

    let derivedStats =
      projections.DerivedStats |> AMap.find entityId |> AVal.force

    // AP: Base = 20*2 = 40, + Sword 15 + BattleCry 10 = 65
    Assert.AreEqual(65, derivedStats.AP, "Full warrior AP calculation")

    // DP: Base = 15 + 15*1.25 = 33, + Armor 10 + Bless 5 - Poison 3 = 45
    Assert.AreEqual(
      45,
      derivedStats.DP,
      "Full warrior DP with armor, buff, and debuff"
    )

    // LK: Base = 10 + 10*0.5 = 15, * 1.2 = 18
    Assert.AreEqual(18, derivedStats.LK, "LK with multiplicative ring")

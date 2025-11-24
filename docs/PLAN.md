# Pomo V2: Architectural Prototype Guide

## 1. Introduction

This document outlines the incremental development plan for the Pomo V2 prototype. The primary goal is to systematically build a feature-equivalent version of the game using a robust, uni-directional, event-driven architecture.

By building in small, verifiable "vertical slices," we will ensure the architecture remains clean, scalable, and avoids the pitfalls identified in the previous prototype.

## 2. Core Principles

These are the foundational rules for this development effort. Adhering to them is critical for success.

- **The `StateUpdateSystem` is Sacred:** Game state (the `MutableWorld`) is only ever modified within the `StateUpdateSystem`. All other systems read from the `World` view and publish events. No exceptions.
- **Think in SCE (System, Component, Event):** Every new feature must be designed in terms of these three pillars. What `Systems` provide the logic? What `Components` hold the data? What `Events` communicate the changes?
- **Debug Render Everything:** The fastest way to verify data flow is to see it. For every new piece of state (HP, stats, AI state), add a simple on-screen text renderer.
- **One Slice at a Time:** Complete and verify one vertical slice of gameplay before moving to the next.

## 3. Advanced Pattern: Handling Same-Frame Dependencies

The core architecture relies on a "deferred" event model where state changes are only visible on the _next_ frame. However, some complex game logic requires immediate, synchronous results within a single frame. This section documents the "Immediate Dispatcher" pattern for handling these cases without violating our core principles.

**The Principle:** The Immediate Dispatcher is a **synchronous calculation pipeline**, not a state-mutation tool. It uses pure functions to calculate a final value from a chain of modifications. It **never** mutates the `MutableWorld` and therefore **does not** trigger multiple adaptive graph updates per frame.

### Use Case 1: Final Ability Cost Calculation

- **Scenario:** An ability costs 100 MP, but the caster has a "-20% MP cost" buff and a "-15 MP cost for Fire spells" item. The system must know the final cost _now_ to validate the cast.
- **Pattern in Action:**
  1.  The `AbilitySystem` creates an `AbilityCostContext` record containing the base cost and ability details.
  2.  It calls `immediateDispatcher.Dispatch(initialContext)`.
  3.  The dispatcher synchronously runs registered handlers from the buff and the item. Each handler is a pure function that takes the context and returns a modified version (e.g., `ctx with BaseCost = ...`).
  4.  The dispatcher returns a `finalContext` with the final cost calculated (e.g., 65 MP).
  5.  The `AbilitySystem` uses this final value to validate the cast and then publishes the real `AbilityCasted` or `NotEnoughMP` event to the main (deferred) `EventBus`.

### Use Case 2: Complex Damage Calculation

- **Scenario:** An attack deals 100 Physical damage. This needs to be modified by, in order: damage-type conversions (25% to Poison), attacker's percentage bonuses (+20% Poison damage), target's percentage resistances (\*0.9 Physical resist), and finally target's flat reductions (-15 Physical DR).
- **Pattern in Action:**
  1.  The `CombatSystem` creates a `DamagePacket` record containing a map of damage types and amounts.
  2.  It calls `immediateDispatcher.Dispatch(initialPacket)`.
  3.  The dispatcher runs handlers in a specified order (using priorities).
      - **Priority 10 (Conversion):** A handler modifies the packet from `{Physical: 100}` to `{Physical: 75, Poison: 25}`.
      - **Priority 20 (Bonuses):** A handler modifies the packet to `{Physical: 75, Poison: 30}`.
      - **Priority 30 (Resistances):** A handler modifies it to `{Physical: 67.5, Poison: 30}`.
      - **Priority 40 (Reductions):** The final handler modifies it to `{Physical: 52.5, Poison: 30}`.
  4.  The dispatcher returns the final `DamagePacket`.
  5.  The `CombatSystem` sums the values (`82.5`) and publishes a single, final `DamageDealt` event to the main `EventBus`.

This pattern is the designated solution for resolving complex, nested, same-frame logic while upholding the "one transaction per frame" rule.

## 4. File Structure Philosophy

We will avoid both a single monolithic `Domain.fs` and a Java-style file explosion. The structure will be organized by domain/feature.

```
/Pomo.Core
|-- Pombo/
|   |-- CoreTypes.fs      # Absolutely foundational types used everywhere (EntityId, etc.)
|   |-- World.fs          # MutableWorld, World, StateUpdateSystem definitions
|
|-- Domains/
|   |-- Movement.fs       # Contains Movement components, events, AND the MovementSystem
|   |-- Attributes.fs     # Contains Stat components, events, AND the StatSystem
|   |-- Combat.fs         # Contains Health/Faction, combat events, AND the CombatSystem
|   |-- Inventory.fs      # ...and so on for each major domain.
|
|-- PomoGame.fs           # Main game class, registers systems from all domain modules
```

### What a Domain File Looks Like

A file like `Domains/Combat.fs` would be structured internally with modules:

```fsharp
// In file: Domains/Combat.fs
module Pomo.Core.Domains.Combat

module Systems =
    open Pomo.Core.Pombo.World
    open Components // Open local modules
    open Events

    type CombatSystem(game: Game) as this =
        inherit GameComponent(game)
        let world = this.World // from GameSystem base if you have one

        override this.Update(gameTime) =
            // ... logic that reads world state and publishes combat events
            ()
```

## 5. Phase 1: The Core Loop (Movement)

**Goal:** A single, controllable entity moving on a screen. This validates the entire end-to-end architecture.

- **Components to Define:**
  - `EntityId`
  - `PositionComponent` (data for a `cmap` in `MutableWorld`)
- **Events to Define:**
  - `EntityCreated of Guid<EntityId>`
  - `MovementIntent of Direction: Vector2`
  - `PositionChanged of EntityId: Guid<EntityId> * NewPosition: Vector2`
- **Systems to Implement:**
  - `SpawningSystem` (can be temporary logic in `PomoGame.Initialize`)
  - `InputSystem`
  - `MovementSystem`
  - `RenderSystem` (logic in `PomoGame.Draw`)
- **✅ Verification Checklist:**
  - [x] Does an entity appear on screen at launch?
  - [x] Does pressing input keys publish a `MovementIntent` event?
  - [x] Does the `MovementSystem` publish a `PositionChanged` event?
  - [x] Does the `StateUpdateSystem` correctly update the entity's position in the `MutableWorld`?
  - [x] Does the rendered entity's position match the state?

## 6. Phase 2: Basic Combat

**Goal:** The player entity can attack a static target, causing a state change (death).

- **Components to Define:**
  - `HealthComponent`
  - `FactionComponent`
- **Events to Define:**
  - `AttackIntent of Attacker: Guid<EntityId> * Target: Guid<EntityId>`
  - `DamageDealt of Target: Guid<EntityId> * Amount: int`
  - `EntityDied of Guid<EntityId>`
- **Systems to Implement:**
  - `CombatSystem`
- **✅ Verification Checklist:**
  - [x] Does a second "enemy" entity appear at launch?
  - [x] Does pressing an "attack" key publish an `AttackIntent`?
  - [x] Does the `CombatSystem` publish a `DamageDealt` event?
  - [x] Does the target's health (in a debug renderer) decrease?
  - [x] Does the target entity disappear from the screen upon death?
  - [x] Is the `Health` cmap updated only by the `StateUpdateSystem`?

## 7. Phase 3: The Skill Effect & Stat Pipeline

**Goal:** Implement a complete, end-to-end pipeline for applying skill effects, managing their lifecycle, and calculating their impact on entity stats. This phase is critical for enabling most of the game's core mechanics.

### 7.1 Data Model: Components & Events

First, we define the necessary data structures that will be stored in the world state and the events that will drive all logic.

- **Components to Define:**

  - `ActiveEffect`: A record representing an instance of an effect on an entity. It should store the source `Effect`, the entity that applied it, the start time, remaining duration, and current stack count.
  - `ActiveEffectComponent`: A `cmap<EntityId, ActiveEffect list>` in the `MutableWorld` to track all active effects on every entity.
  - `BaseStatsComponent`: Holds the unmodified, base statistics of an entity.
  - `DerivedStatsComponent`: Holds the final, calculated statistics of an entity after all effect modifiers have been applied. This is a read-only, projected component.

- **Events to Define:**
  - `EffectApplied of EntityId * ActiveEffect`: Published when an effect is successfully applied to an entity. The `StateUpdateSystem` will use this to add to the `ActiveEffectComponent`.
  - `EffectExpired of EntityId * EffectId`: Published when an effect's duration runs out or it is removed. The `StateUpdateSystem` will use this to remove from the `ActiveEffectComponent`.
  - `EffectRefreshed of EntityId * EffectId`: Published when an effect's duration is reset.
  - `StatsRecalculated of EntityId * DerivedStats`: Published by the `StatSystem` after it computes new stats for an entity.

### 7.2 System 1: `EffectApplicationSystem`

This system is the entry point for all effects.

- **Responsibilities:**
  - Listens for trigger events like `AbilityCasted` and `ProjectileImpacted`.
  - Determines the target(s) of the skill or ability.
  - For each effect on the skill, it checks the target and applies the effect according to its `StackingRule` (NoStack, RefreshDuration, AddStack).
  - Publishes `EffectApplied` or `EffectRefreshed` events.

### 7.3 System 2: `EffectLifecycleSystem`

This system manages the "time" component of all active effects.

- **Responsibilities:**
  - Runs each frame, iterating through all entities with an `ActiveEffectComponent`.
  - For `Timed` effects, it decrements their duration and publishes `EffectExpired` when they run out.
  - For `Loop` and `PermanentLoop` effects, it checks if the interval has passed. If so, it triggers the effect's action (e.g., for a Damage-over-Time effect, it would publish a `DamageDealt` event).

### 7.4 System 3: `StatSystem`

This system is the core of the stat calculation pipeline.

- **Responsibilities:**
  - Listens for `EffectApplied` and `EffectExpired` events.
  - When an entity's effects change, it triggers a recalculation for that entity.
  - **Calculation Pipeline:**
    1.  Fetches the entity's `BaseStatsComponent`.
    2.  Fetches the entity's `ActiveEffectComponent`.
    3.  Iterates through each `ActiveEffect`, applying its `EffectModifier` (`StaticMod`, `DynamicMod`, etc.) to the stats. This will require a **Formula Evaluator** to process `MathExpr`.
    4.  Once all modifiers are applied, it publishes a `StatsRecalculated` event with the final `DerivedStats`.

### 7.5 ✅ Verification Checklist

- [x] Can an ability apply a "Stun" (or similar state-based) effect to an entity? Does a debug view confirm the `ActiveEffect` is present?
- [x] Does a "Damage over Time" effect cause the target's health to decrease periodically, driven by the `EffectLifecycleSystem`?
- [x] Can a temporary debug command apply a "+10 AP" buff effect?
- [x] Does a debug renderer show the updated `DerivedStats` value on the _next_ frame?
- [x] Does a subsequent ability cast use the newly calculated (buffed) stats for its own calculations (e.g., damage)?

### 7.6 Advanced Skill System Refactor

**Goal:** Refactor the entire skill pipeline to support a wide variety of composable skill types, including complex targeting, delivery, and area of effect, as well as advanced projectile behaviors like chaining.

- **Key Architectural Changes:**

  - **Decoupled Skill Definition:** A skill is now composed of `Targeting` (Self, Entity, Position), `Delivery` (Instant, Projectile), and `SkillArea` (Point, Circle, Cone, Line, MultiPoint).
  - **Flexible Targeting:** The `TargetingSystem` now handles different targeting modes and initiates movement-then-cast behavior for out-of-range targets via a `PendingSkillCast` state.
  - **Generalized Activation:** The `AbilityActivationSystem` correctly interprets the `PendingSkillCast` and publishes a generic `AbilityIntent` event. It also handles spawning multiple projectiles for `MultiPoint` area skills.
  - **Area of Effect Combat:** The `CombatSystem` now uses the `SkillArea` definition to apply effects to multiple targets for both `Instant` delivery skills and on-impact effects for `Projectile` skills.
  - **Advanced Projectile Logic:** The `ProjectileSystem` has been refactored to support complex variations. It can now handle `Chained` projectiles, creating new projectiles at impact points to continue the chain.

- **✅ Verification Checklist:**
  - [x] Can a skill be defined with `TargetPosition` and an `Instant` `Circle` area of effect (e.g., "Summon Boulder")?
  - [x] Does the `CombatSystem` correctly find and apply damage to multiple targets within the AoE?
  - [x] Can a `Projectile` skill be defined with a `Circle` area of effect on impact?
  - [x] Does the `CombatSystem` apply AoE damage when the projectile hits its target?
  - [x] Can a `Projectile` be defined with a `Chained` variation?
  - [x] Does the `ProjectileSystem` correctly spawn a new projectile at the impact site to continue the chain?
  - [x] Is the entire flow, from input to damage, handled through the event bus and `StateUpdateSystem`?

## 8. Phase 4: Inventory & Equipment

**Goal:** Implement a complete pipeline for picking up, managing, and equipping items, and have their stats correctly influence character performance.

### 8.1 Data Model: Components & Core Types

- **`ItemDefinition`**: Static data loaded from `Content/Items.json`.
  - Fields: `Id: int<ItemId>`, `Name: string`, `Weight: float32`, `Kind: ItemKind`.
- **`ItemKind`**: A DU defining item behavior: `Wearable of EquipmentProperties`, `Usable of UsabilityProperties`, `NonUsable`.
- **`ItemInstance`**: A unique item in the game world.
  - Fields: `InstanceId: Guid<ItemInstanceId>`, `ItemId: int<ItemId>`.
- **`ItemInstanceComponent: cmap<Guid<ItemInstanceId>, ItemInstance>`**
  - This is a global map of all item instances in the world. It's a `cmap` because individual `ItemInstance`s might have mutable state (e.g., durability, charges) that we want to react to.
- **`EntityInventoryComponent: cmap<EntityId, HashSet<Guid<ItemInstanceId>>>`**
  - This maps an entity to a **`HashSet`** of `ItemInstanceId`s it owns. The `HashSet` is a non-reactive collection. When an item is added or removed from an entity's inventory, the entire `HashSet` for that entity is replaced in the `cmap`. This is efficient for updates and queries.
- **`EquippedItemsComponent: cmap<EntityId, HashMap<Slot, Guid<ItemInstanceId>>>`**
  - This maps an entity to a **`HashMap`** of `Slot` to `ItemInstanceId`. The `HashMap` is a non-reactive collection. When an item is equipped or unequipped, the entire `HashMap` for that entity is replaced in the `cmap`. This allows for efficient lookups by slot and reactive updates when equipment changes.

### 8.2 SCE: Systems, Components, Events

- **Events to Define:**
  - `PickUpItemIntent of Picker: EntityId * Item: ItemInstance`
  - `ItemAddedToInventory of Owner: EntityId * Item: ItemInstance`
  - `EquipItemIntent of EntityId * ItemInstanceId * Slot`
  - `ItemEquipped of EntityId * ItemInstanceId * Slot`
  - `UnequipItemIntent of EntityId * Slot`
  - `ItemUnequipped of EntityId * ItemInstanceId * Slot`
- **Systems to Implement:**
  - **`InventorySystem`**: Listens for `PickUpItemIntent`, validates, and publishes `ItemAddedToInventory`.
  - **`EquipmentSystem`**: Listens for `EquipItemIntent` and `UnequipItemIntent`, validates, and publishes `ItemEquipped` or `ItemUnequipped`.
  - **`StateUpdateSystem`**: Updated to handle the new events and modify the `EntityInventoryComponent` and `EquippedItemsComponent`.

### 8.3 Data-Driven Definitions

- **`Content/Items.json`**: A new file to define all items in the game.
- **`ItemStore`**: A new service, loaded at startup and added to `EngineServices`, that provides access to `ItemDefinition`s.

### 8.4 Integration & Projections

- **New Projection (`Projections.getEquipmentStatBonuses`)**:
  - This will be an `amap<EntityId, StatBonus list>` derived from `EquippedItemsComponent` and `ItemStore`.
  - It will reactively compute the list of stat bonuses for an entity whenever its gear changes.
- **Update `Projections.getDerivedStats`**:
  - The main `DerivedStats` projection will be updated to consume `Projections.getEquipmentStatBonuses`.
  - It will combine bonuses from equipment with bonuses from active effects to produce the final character stats.
- **New Projections for UI/Consumption**:
  - **`Projections.getEquippedItems`**: `world -> amap<EntityId, HashMap<Slot, ItemInstance>>`. Provides a reactive map of equipped items, perfect for a character UI. This projection will join `EquippedItemsComponent` with `ItemInstanceComponent`.
  - **`Projections.getInventory`**: `world -> amap<EntityId, HashSet<ItemInstance>>`. Provides a reactive set of inventory contents for an inventory screen. This projection will join `EntityInventoryComponent` with `ItemInstanceComponent`.

### 8.5 ✅ Verification Checklist

- [x] Can an `Items.json` file be created and loaded by a new `ItemStore`?
- [x] Can an entity pick up an item, triggering an `ItemAddedToInventory` event and updating the `EntityInventoryComponent`?
- [x] Can an entity equip an item from its inventory, triggering `ItemEquipped` and updating `EquippedItemsComponent`?
- [x] Does equipping an item with stats cause the `DerivedStats` projection for that entity to update on the next frame?
- [x] Does a debug renderer show the updated stats?
- [x] Does unequipping the item revert the stats correctly?

## 9. Phase 5: Scenario & Terrain

**Goal:** Implement a data-driven map system that supports tile-based rendering, collision, and spatial queries, laying the groundwork for advanced AI and gameplay.

### 9.1 Data Model: Components & Core Types

- **`TileDefinition`**: Static data for a tile type, e.g., `Id`, `TerrainType` (`Walkable`, `Blocked`, `Water`), `MovementCost`.
- **`MapTile`**: An instance of a tile on the map, holding a `TileDefinitionId` and its position.
- **`MapDefinition`**: Static data for a whole map, loaded from a Tiled JSON file.
  - Fields: `Name`, `Dimensions` (width/height in tiles), `TileSize`, a 2D array of `MapTile`s, and a list of `MapObject`s (for things like spawn points or triggers placed in Tiled).
- **`CollisionShape`**: A DU for entity collision, e.g., `Circle of float32` or `Box of Vector2`. This will be added to the `Entity` record.
- **`SpatialGrid`**: A core data structure in the `World` state. It will be a simple grid-based spatial index to accelerate queries like "what entities are in this area?".

### 9.2 SCE: Systems, Components, Events

- **Events:**
  - `MapLoaded of MapDefinition`
  - `CollisionDetected of EntityA: EntityId * EntityB: EntityId` (for entity-entity collisions)
- **Systems:**
  - **`MapLoadingService`**: A service responsible for parsing Tiled JSON files into our `MapDefinition` format.
  - **`TerrainRenderSystem`**: A `DrawableGameComponent` that renders the `MapDefinition` with an isometric projection.
  - **`SpatialIndexingSystem`**: A system that runs each frame to update the `SpatialGrid` with the current positions of all entities.
  - **`CollisionSystem`**: This system will use the `SpatialGrid` to get nearby entities and perform narrow-phase collision checks. It will be used by other systems (like `MovementSystem`) to validate actions.

### 9.3 Data-Driven Definitions

- Maps will be created in the **Tiled Map Editor** and exported to JSON format (e.g., `Content/Maps/Forest.json`).
- We will establish a convention for using **Custom Properties** in Tiled to define `TerrainType` and other metadata for our tilesets.

### 9.4 Integration & Projections

- **`MovementSystem` Integration**: Will be updated to query the `CollisionSystem` to check if a proposed new position is valid.
- **`CombatSystem` Integration**: Will use the `SpatialGrid` to efficiently find targets for Area of Effect abilities.
- **New Projection (`Projections.getNearbyEntities`)**:
  - **Signature**: `world -> EntityId -> float -> alist<EntityId>`
  - **Purpose**: A crucial projection for AI. It will take an entity and a radius and use the `SpatialGrid` to efficiently return a reactive list of all entities within that radius.

### 9.5 ✅ Verification Checklist

- [x] Can a map file exported from Tiled (`.xml`) be successfully parsed into a `MapDefinition`?
- [x] Does the `TerrainRenderSystem` correctly draw the tilemap with data-driven layer ordering?
- [x] Does the `CollisionSystem` detect and publish events for entity-wall collisions?
- [x] Do `PlayerMovementSystem` and `UnitMovementSystem` react to wall collisions to prevent entities from moving into blocked spaces?
- [x] Can the `SpatialGrid` be queried via a helper function (`Collision.getNearbyEntities`) to find entities in a specific area?
- [x] Does a debug renderer correctly visualize collision shapes and the spatial grid, rendering on top of all game elements?
- [x] Is there a reactive `Projections.getNearbyEntities` that uses the spatial grid for efficient, declarative queries (e.g., for AI)?
- [x] Is the `CombatSystem`'s AoE logic refactored to use the `SpatialGrid` for efficient target acquisition instead of iterating all entities?
- [x] Is map loading fully dynamic, allowing systems like the `RenderOrchestrator` to react to map changes at runtime?
- [ ] (Optional Refactor) Has the duplicated collision response logic in `PlayerMovementSystem` and `UnitMovementSystem` been unified for better maintainability?

## 10. Phase 6: AI

**Goal:** An enemy that moves and attacks the player, using the terrain and spatial index for intelligent behavior.

### 10.1 Current State (Implemented)

- **Core Architecture:** `AIController`, `AIArchetype`, `PerceptionSystem`, `AISystem` are implemented.
- **Perception:** Visual cues and memory decay are working.
- **Navigation:** Waypoint patrolling and "Move To" commands are implemented.
- **Decision Making:** Basic priority-based decision logic exists (`Investigate`, `Engage`, `Flee`).

### 10.2 Missing Pieces (Next Steps)

- **Combat Integration:** The `Engage` response currently only moves to the target. It needs to:
  - Check for available abilities in `SkillStore`.
  - Check range and resource costs.
  - Publish `AbilityIntent` events to cast spells.
- **State Machine Refinement:** Ensure transitions between `Chasing` and `Attacking` are smooth.

### 10.3 ✅ Verification Checklist

- [x] Can an AI entity perceive the player and generate a `Visual` cue?
- [x] Does the AI transition from `Patrol` to `Investigate` or `Engage` based on cues?
- [x] Does the AI successfully navigate to a target position?
- [ ] **Does the AI cast an ability (e.g., "Fireball") when in range of the player?**
- [ ] Does the AI respect cooldowns and resources?
- [x] Does the AI return to patrolling after the player is lost (memory expired)?

## 11. Phase 7 and Beyond

Once the above phases are complete, the core architecture is proven and battle-tested. You can now add the remaining domains from your old prototype by repeating this pattern.

- **Next Slice: Audio**
  - **Goal:** Play sounds in response to game events.
  - **SCE:** `AudioSystem` that listens to `DamageDealt`, `ProjectileFired`, etc., and publishes `PlaySound` events.

## Appendix: Architectural Patterns for Common Problems

This section provides high-level solutions for complex mechanics, demonstrating how the event-driven architecture can handle them cleanly.

### Pattern: Chained Strikes

- **Problem:** An ability hits a target and must immediately "jump" to N other valid targets in a sequence within a single game action.
- **Architectural Solution:** Model this as a **synchronous query loop** within a single system (e.g., `CombatSystem`), which then publishes a batch of deferred events.
  1.  When an initial hit event is processed, the system starts a loop.
  2.  **Inside the loop:** It performs a **read-only** query against the current `World` state to find the next valid target (e.g., closest enemy not yet hit).
  3.  If a target is found, the system publishes the necessary deferred events (e.g., `DamageDealt`, `VfxSpawned`) to the main `EventBus`.
  4.  The loop continues until N jumps are complete or no valid target is found.
- **Benefit:** The complex targeting logic is contained and only ever reads state. The consequences (damage, VFX) are handled by the normal, robust event flow, preventing race conditions.

### Pattern: Dash/Rush Abilities vs. Pathfinding

- **Problem:** A special "Dash" movement needs to override the default pathfinding movement, causing conflicts, bugs, and difficulty implementing visual trails.
- **Architectural Solution:** Make the entity's movement behavior an **explicit state component** and use different systems for each state.
  1.  **Create a `MovementState` Component:** Define a discriminated union like `type MovementState = Idle | Pathfinding of ... | Dashing of ...`.
  2.  **Create Specialized Systems:**
      - `PathfindingSystem`: Only processes entities where `MovementState = Pathfinding`.
      - `DashSystem`: Only processes entities where `MovementState = Dashing`. It handles the interpolation, publishes `PositionChanged` events, and can also publish `VfxSpawned` events for the trail. When the dash is over, it publishes an event to set the state back to `Idle`.
  3.  **Trigger the State Change:** An ability doesn't move the entity directly; it just publishes a `MovementStateChanged` event.
- **Benefit:** The conflict is eliminated. Two simple, independent systems operate on different entity states. The entity's behavior is driven by a clean, explicit state machine that is easy to debug and extend.

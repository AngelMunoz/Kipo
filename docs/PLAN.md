# Pomo V2: Architectural Prototype Guide

## 1. Introduction

This document outlines the incremental development plan for the Pomo V2 prototype. The primary goal is to systematically build a feature-equivalent version of the game using a robust, uni-directional, event-driven architecture.

By building in small, verifiable "vertical slices," we will ensure the architecture remains clean, scalable, and avoids the pitfalls identified in the previous prototype.

## 2. Core Principles

These are the foundational rules for this development effort. Adhering to them is critical for success.

*   **The `StateUpdateSystem` is Sacred:** Game state (the `MutableWorld`) is only ever modified within the `StateUpdateSystem`. All other systems read from the `World` view and publish events. No exceptions.
*   **Think in SCE (System, Component, Event):** Every new feature must be designed in terms of these three pillars. What `Systems` provide the logic? What `Components` hold the data? What `Events` communicate the changes?
*   **Debug Render Everything:** The fastest way to verify data flow is to see it. For every new piece of state (HP, stats, AI state), add a simple on-screen text renderer.
*   **One Slice at a Time:** Complete and verify one vertical slice of gameplay before moving to the next.

## 3. Advanced Pattern: Handling Same-Frame Dependencies

The core architecture relies on a "deferred" event model where state changes are only visible on the *next* frame. However, some complex game logic requires immediate, synchronous results within a single frame. This section documents the "Immediate Dispatcher" pattern for handling these cases without violating our core principles.

**The Principle:** The Immediate Dispatcher is a **synchronous calculation pipeline**, not a state-mutation tool. It uses pure functions to calculate a final value from a chain of modifications. It **never** mutates the `MutableWorld` and therefore **does not** trigger multiple adaptive graph updates per frame.

### Use Case 1: Final Ability Cost Calculation

*   **Scenario:** An ability costs 100 MP, but the caster has a "-20% MP cost" buff and a "-15 MP cost for Fire spells" item. The system must know the final cost *now* to validate the cast.
*   **Pattern in Action:**
    1.  The `AbilitySystem` creates an `AbilityCostContext` record containing the base cost and ability details.
    2.  It calls `immediateDispatcher.Dispatch(initialContext)`.
    3.  The dispatcher synchronously runs registered handlers from the buff and the item. Each handler is a pure function that takes the context and returns a modified version (e.g., `ctx with BaseCost = ...`).
    4.  The dispatcher returns a `finalContext` with the final cost calculated (e.g., 65 MP).
    5.  The `AbilitySystem` uses this final value to validate the cast and then publishes the real `AbilityCasted` or `NotEnoughMP` event to the main (deferred) `EventBus`.

### Use Case 2: Complex Damage Calculation

*   **Scenario:** An attack deals 100 Physical damage. This needs to be modified by, in order: damage-type conversions (25% to Poison), attacker's percentage bonuses (+20% Poison damage), target's percentage resistances (*0.9 Physical resist), and finally target's flat reductions (-15 Physical DR).
*   **Pattern in Action:**
    1.  The `CombatSystem` creates a `DamagePacket` record containing a map of damage types and amounts.
    2.  It calls `immediateDispatcher.Dispatch(initialPacket)`.
    3.  The dispatcher runs handlers in a specified order (using priorities).
        *   **Priority 10 (Conversion):** A handler modifies the packet from `{Physical: 100}` to `{Physical: 75, Poison: 25}`.
        *   **Priority 20 (Bonuses):** A handler modifies the packet to `{Physical: 75, Poison: 30}`.
        *   **Priority 30 (Resistances):** A handler modifies it to `{Physical: 67.5, Poison: 30}`.
        *   **Priority 40 (Reductions):** The final handler modifies it to `{Physical: 52.5, Poison: 30}`.
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

// Import foundational types needed from other files
open Pomo.Core.Pombo.CoreTypes

// 1. COMPONENT definitions for this domain
module Components =
    type Health = { Current: int; Max: int }
    type Faction = Player | Enemy

// 2. EVENT definitions for this domain
module Events =
    type DamageDealt = { Target: Guid<EntityId>; Amount: int }
    type EntityDied = { Target: Guid<EntityId> }

// 3. SYSTEM definitions for this domain
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

*   **Components to Define:**
    *   `EntityId`
    *   `PositionComponent` (data for a `cmap` in `MutableWorld`)
*   **Events to Define:**
    *   `EntityCreated of Guid<EntityId>`
    *   `MovementIntent of Direction: Vector2`
    *   `PositionChanged of EntityId: Guid<EntityId> * NewPosition: Vector2`
*   **Systems to Implement:**
    *   `SpawningSystem` (can be temporary logic in `PomoGame.Initialize`)
    *   `InputSystem`
    *   `MovementSystem`
    *   `RenderSystem` (logic in `PomoGame.Draw`)
*   **✅ Verification Checklist:**
    *   [ ] Does an entity appear on screen at launch?
    *   [ ] Does pressing input keys publish a `MovementIntent` event?
    *   [ ] Does the `MovementSystem` publish a `PositionChanged` event?
    *   [ ] Does the `StateUpdateSystem` correctly update the entity's position in the `MutableWorld`?
    *   [ ] Does the rendered entity's position match the state?

## 6. Phase 2: Basic Combat

**Goal:** The player entity can attack a static target, causing a state change (death).

*   **Components to Define:**
    *   `HealthComponent`
    *   `FactionComponent`
*   **Events to Define:**
    *   `AttackIntent of Attacker: Guid<EntityId> * Target: Guid<EntityId>`
    *   `DamageDealt of Target: Guid<EntityId> * Amount: int`
    *   `EntityDied of Guid<EntityId>`
*   **Systems to Implement:**
    *   `CombatSystem`
*   **✅ Verification Checklist:**
    *   [ ] Does a second "enemy" entity appear at launch?
    *   [ ] Does pressing an "attack" key publish an `AttackIntent`?
    *   [ ] Does the `CombatSystem` publish a `DamageDealt` event?
    *   [ ] Does the target's health (in a debug renderer) decrease?
    *   [ ] Does the target entity disappear from the screen upon death?
    *   [ ] Is the `Health` cmap updated only by the `StateUpdateSystem`?

## 7. Phase 3: Advanced Systems (Abilities & Stats)

### 7.1 The Stat Calculation Pipeline

**Goal:** A robust, non-circular system for calculating derived stats from base stats and effects.

*   **Components to Define:**
    *   `BaseStatsComponent`
    *   `ActiveEffectComponent`
    *   `DerivedStatsComponent`
*   **Events to Define:**
    *   `StatsCalculated of EntityId: Guid<EntityId> * Stats: DerivedStats`
*   **Systems to Implement:**
    *   `StatSystem` (implements the multi-pass pipeline)
*   **✅ Verification Checklist:**
    *   [ ] Can a temporary debug command apply a simple "+10 Power" buff effect to an entity?
    *   [ ] Does the `StatSystem` run and publish a `StatsCalculated` event?
    *   [ ] Does a debug renderer show the updated `DerivedStats` value on the *next* frame?

### 7.2 Chained Ability Execution

**Goal:** Implement a single projectile ability using the event-chaining pattern.

*   **Events to Define:**
    *   `AbilityCasted`
    *   `ProjectileFired`
    *   `ProjectileImpacted`
*   **Systems to Implement:**
    *   `AbilitySystem`
    *   `ProjectileSystem`
    *   `VfxSystem` (can just draw primitive shapes for now)
*   **✅ Verification Checklist:**
    *   [ ] Does using the ability publish an `AbilityCasted` event?
    *   [ ] Does the `VfxSystem` react by publishing a `ProjectileFired` event?
    *   [ ] Does the `ProjectileSystem` move the projectile and publish `ProjectileImpacted` on collision?
    *   [ ] Does the `CombatSystem` react to `ProjectileImpacted` by publishing `DamageDealt`?

## 8. Phase 4 and Beyond

Once the above phases are complete, the core architecture is proven and battle-tested. You can now add the remaining domains from your old prototype by repeating this pattern.

*   **Next Slice: AI**
    *   **Goal:** An enemy that moves and attacks the player.
    *   **SCE:** `AIControllerComponent`, `PerceptionSystem`, `AIBehaviorSystem`, `TargetChanged` event.
*   **Next Slice: Inventory & Equipment**
    *   **Goal:** Pick up, equip, and get stat bonuses from an item.
    *   **SCE:** `InventoryComponent`, `ItemStore` service, `EquipItemIntent`, `ItemEquipped` event.
*   **Next Slice: Audio**
    *   **Goal:** Play sounds in response to game events.
    *   **SCE:** `AudioSystem` that listens to `DamageDealt`, `ProjectileFired`, etc., and publishes `PlaySound` events.

## Appendix: Architectural Patterns for Common Problems

This section provides high-level solutions for complex mechanics, demonstrating how the event-driven architecture can handle them cleanly.

### Pattern: Chained Strikes

*   **Problem:** An ability hits a target and must immediately "jump" to N other valid targets in a sequence within a single game action.
*   **Architectural Solution:** Model this as a **synchronous query loop** within a single system (e.g., `CombatSystem`), which then publishes a batch of deferred events.
    1.  When an initial hit event is processed, the system starts a loop.
    2.  **Inside the loop:** It performs a **read-only** query against the current `World` state to find the next valid target (e.g., closest enemy not yet hit).
    3.  If a target is found, the system publishes the necessary deferred events (e.g., `DamageDealt`, `VfxSpawned`) to the main `EventBus`.
    4.  The loop continues until N jumps are complete or no valid target is found.
*   **Benefit:** The complex targeting logic is contained and only ever reads state. The consequences (damage, VFX) are handled by the normal, robust event flow, preventing race conditions.

### Pattern: Dash/Rush Abilities vs. Pathfinding

*   **Problem:** A special "Dash" movement needs to override the default pathfinding movement, causing conflicts, bugs, and difficulty implementing visual trails.
*   **Architectural Solution:** Make the entity's movement behavior an **explicit state component** and use different systems for each state.
    1.  **Create a `MovementState` Component:** Define a discriminated union like `type MovementState = Idle | Pathfinding of ... | Dashing of ...`.
    2.  **Create Specialized Systems:**
        *   `PathfindingSystem`: Only processes entities where `MovementState = Pathfinding`.
        *   `DashSystem`: Only processes entities where `MovementState = Dashing`. It handles the interpolation, publishes `PositionChanged` events, and can also publish `VfxSpawned` events for the trail. When the dash is over, it publishes an event to set the state back to `Idle`.
    3.  **Trigger the State Change:** An ability doesn't move the entity directly; it just publishes a `MovementStateChanged` event.
*   **Benefit:** The conflict is eliminated. Two simple, independent systems operate on different entity states. The entity's behavior is driven by a clean, explicit state machine that is easy to debug and extend.
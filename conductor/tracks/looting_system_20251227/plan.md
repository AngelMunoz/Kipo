# Implementation Plan - Loot System

## Phase 1: Core Data & Configuration

Establish the data structures and loading mechanisms for the loot system.

- [ ] Task: Define Domain Types for Loot
  - [ ] Sub-task: Create `Loot.fs` in `Pomo.Core.Domain` to define `LootArchetype`, `LootFamily`, and `LootBinding`.
  - [ ] Sub-task: Update `AI.fs` to include `LootFamilies: string array` (and optionally accept a single `LootBindingId` as sugar) in `AIEntityDefinition` and extend its decoder.
- [ ] Task: Define Icon System
  - [ ] Sub-task: Create `Icon.fs` in `Pomo.Core.Domain` for `IconDefinition` (texture path, source rect, tint).
  - [ ] Sub-task: Add `IconId: string voption` to `ActiveSkill` and `PassiveSkill` in `Skill.fs` (for skill bars, skill trees).
  - [ ] Sub-task: Add `IconId: string voption` to `Effect` in `Skill.fs` (for status bar icons, buff/debuff display).
  - [ ] Sub-task: Add `IconId: string voption` to `ItemDefinition` in `Item.fs` (for inventory, loot popups).
  - [ ] Sub-task: Add `WorldVisuals: VisualManifest voption` to `ItemDefinition` for 3D world representation (dropped loot models, VFX).
  - [ ] Note: Icons (UI representation) are separate from `VisualManifest` (world representation). A skill's `CastVisuals` may show fire particles in the world, while its `IconId` shows a fireball sprite in the skill bar.
- [ ] Task: Implement Serialization & Stores
  - [ ] Sub-task: Implement JSON decoders for new Loot types in `Loot.fs`.
  - [ ] Sub-task: Create `LootStore` interface and implementation in `Stores.fs`.
  - [ ] Sub-task: Create `IconStore` interface and implementation in `Stores.fs`.
  - [ ] Sub-task: Update `JsonFileLoader.fs` to handle loading `Content/Loot/*.json` and `Content/Icons.json`, adding `readLootArchetypes`, `readLootFamilies`, and `readLootBindings` with missing-file tolerance similar to `readMapEntityGroups`.
- [ ] Task: Create Initial Content
  - [ ] Sub-task: Create directory `Pomo.Core/Content/Loot/`.
  - [ ] Sub-task: Author basic `Archetypes.json`, `Families.json`, and `LootEntities.json`.
  - [ ] Sub-task: Create `Icons.json` with initial skill/item icons.
  - [ ] Sub-task: Update `AIEntities.json` to reference test loot bindings via `LootFamilies` (optionally allow single `LootBindingId` as sugar).
- [ ] Task: Conductor - User Manual Verification 'Phase 1: Core Data & Configuration' (Protocol in workflow.md)

## Phase 2: Loot Logic Service

Implement the core logic for calculating drops independent of the game loop.

- [ ] Task: Implement LootService
  - [ ] Sub-task: Create `LootService` module (pure, stateless).
  - [ ] Sub-task: Implement `calculateDrops(entityKey, seed)` that traverses the hierarchy (Entity -> Family -> Archetype) and returns loot entries with ownership policies; RNG seed derived from `ScenarioId + EntityId` (do not capture `World.Rng`).
  - [ ] Sub-task: Write Unit Tests for `CalculateDrops` covering weights, guaranteed drops, and hierarchy fallbacks.
- [ ] Task: Conductor - User Manual Verification 'Phase 2: Loot Logic Service' (Protocol in workflow.md)

## Phase 3: World Representation & Physics

Make loot appear physically in the game world.

- [ ] Task: Update World State
  - [ ] Sub-task: Add `Loot` component to `World` in `World.fs` (`cmap<Guid<EntityId>, struct(Guid<ItemInstanceId> * Guid<ScenarioId> * TimeSpan)>`), reusing the existing `ItemInstances` store, where the `TimeSpan` is spawn time for TTL/despawn cues.
  - [ ] Sub-task: Add `EntityKeys` component to `World` to track original entity definitions at runtime for deterministic loot lookup.
- [ ] Task: Implement LootSystem (Spawning)
  - [ ] Sub-task: Create `LootSystem.fs`.
  - [ ] Sub-task: Subscribe to `EntityDied` events.
  - [ ] Sub-task: Call `LootService` to get drops using deterministic seed (`ScenarioId + EntityId`).
  - [ ] Sub-task: For each drop, create an `ItemInstance` and a new World Entity.
  - [ ] Sub-task: Implement basic physics logic (spawn with upward velocity, apply gravity, stop at floor Y) using existing particle/physics facilities.
- [ ] Task: Visuals Implementation
  - [ ] Sub-task: Integrate with `RenderOrchestrator` to render the Item's 3D model at the entity position.
  - [ ] Sub-task: Attach billboard particle effect (glow) to the loot entity.
- [ ] Task: Conductor - User Manual Verification 'Phase 3: World Representation & Physics' (Protocol in workflow.md)

## Phase 4: Interaction & Integration

Allow the player to pick up the loot.

- [ ] Task: Interaction Logic
  - [ ] Sub-task: Update `CursorSystem.fs` or create `InteractionSystem.fs`.
  - [ ] Sub-task: Implement Raycast/Picking logic for Loot Entities.
  - [ ] Sub-task: Implement "Proximity Check" for the player character near loot.
- [ ] Task: UI Feedback
  - [ ] Sub-task: Display "Press F to Loot" or hover tooltip when valid loot is targeted/nearby.
- [ ] Task: Handle Pickup
  - [ ] Sub-task: Map Click/Keypress to `PickUpItemIntent`.
  - [ ] Sub-task: Update `Inventory.fs` to handle the intent: remove Entity from World, remove from `World.Loot` map, add to Inventory.
- [ ] Task: Conductor - User Manual Verification 'Phase 4: Interaction & Integration' (Protocol in workflow.md)

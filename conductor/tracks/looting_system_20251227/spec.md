# Specification: Loot System Implementation

## 1. Overview

Implement a comprehensive Loot System that bridges static item definitions with the dynamic game world. The system will use a hierarchical data-driven configuration (Archetypes -> Families -> Entities) to determine drops. Dropped items will appear as physical, interactive 3D entities in the world that react physically when spawned and can be collected via mouse click or proximity interaction.

## 2. Functional Requirements

### 2.1 Configuration & Data Structure

- **Loot Archetypes (`Content/Loot/Archetypes.json`):** Define base loot motifs (weights, rarity curves, duplicate caps).
- **Loot Families (`Content/Loot/Families.json`):** Compose archetypes with context tags (biome/faction) and adjustments.
- **Loot Bindings (`Content/Loot/LootEntities.json`):** Bind specific drop sources (Entity Keys) to Loot Families with local tweaks.
- **Icon System:** Icons represent UI visuals (inventory, skill bars, status bars) and are separate from `VisualManifest` which handles world visuals (3D models, VFX, animations).
  - Define `IconDefinition` (texture path, source rect, tint) and `IconStore` for centralized lookup.
  - Add `IconId: string voption` to: `ActiveSkill`, `PassiveSkill`, `Effect`, and `ItemDefinition`.
  - This separation allows the same skill/effect to have different world VFX (via `VisualManifest`) and UI icons (via `IconId`).
- **Entity Integration:**
  - Update `AIEntities.json` and `AIFamilies.json` schemas to support `LootFamilies: string[]` (optionally accept a single `LootBindingId` as sugar); extend the `AIEntityDefinition` decoder accordingly.
  - Support hierarchical resolution: Entity Specific -> Family Default -> Global Default.
  - Support Map-specific overrides (e.g., loading `Maps/<MapName>.loot.json`).

### 2.2 Loot Generation (The `LootSystem`)

- Listen for `EntityDied` events.
- Resolve the correct Loot Table/Binding for the deceased entity.
- Execute the loot roll logic (RNG based on weights/chance).
- Create new `ItemInstance`s for any successful drops.

### 2.3 World Representation & Physics

- **Spawning:** Convert successful drops into World Entities.
- **Visuals:** Render the actual 3D model associated with the item using 3D mesh particles.
- **Physics:** Apply physics impulses upon spawn (e.g., popping out/bouncing) using the engine's existing particle/physics facilities, ensuring they eventually settle on the floor.
- **VFX:** Attach billboard particle effects (e.g., glow/sparkle) to dropped items to highlight them.

### 2.4 Interaction

- **Click-to-Loot:** Allow players to click on the item entity to pick it up.
- **Proximity Loot:** Display a prompt (e.g., "Press F to Loot") when the player is within a defined range.
- **Pickup Logic:**
  - Trigger `PickUpItemIntent`.
  - Add item to player inventory.
  - Despawn the world entity.

## 3. Technical Components

- **`LootService`:** Core domain service to load configurations and calculate drops; pure/stateless with deterministic RNG seeded from `ScenarioId + EntityId` (do not capture mutable world RNG). Forward-compatible with optional ownership policies for local multiplayer.
- **`LootSystem` (ECS System):** Handles `EntityDied` events and manages World Entity spawning/despawning.
- **`InteractionSystem` Update:** Extend `CursorSystem` or create a new `InteractionSystem` to handle the "Press F" proximity logic and raycasting for loot clicks.
- **`World` Domain Update:** Add storage for `World.Loot` (mapping loot entity ids to `struct(Guid<ItemInstanceId> * Guid<ScenarioId> * TimeSpan)`), reusing the existing `ItemInstances` store and adding entity key tracking for deterministic lookup and TTL/despawn.
- **`IconStore`**: Store and provide icon definitions for UI.

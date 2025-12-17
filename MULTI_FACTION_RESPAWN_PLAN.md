# Multi-Faction Combat and Entity Respawning - Implementation Plan

> **Purpose**: This document enables any LLM to continue implementing this feature from scratch.
> **Created**: 2025-12-17
> **Status**: Approved, ready for implementation

---

## Context for LLMs

### Project Overview

- **Language**: F# with MonoGame
- **Architecture**: Event-driven ECS-like system using FSharp.Data.Adaptive
- **Key Patterns**: See [AGENTS.md](AGENTS.md) and [.agents/fsharp_conventions.md](.agents/fsharp_conventions.md)

### Relevant Codebase Locations

| File                                            | Purpose                             | Key Lines                                          |
| ----------------------------------------------- | ----------------------------------- | -------------------------------------------------- |
| `Pomo.Core/Domain/Entity.fs`                    | Entity types including `Faction` DU | Lines 34-39 (Faction), 129-144 (Faction.decoder)   |
| `Pomo.Core/Domain/AI.fs`                        | AI types including `MapEntityGroup` | Lines 157-161 (MapEntityGroup), 486-501 (decoder)  |
| `Pomo.Core/Domain/Events.fs`                    | Event definitions, `SpawnType`      | Lines 33-47 (SpawnType), 180-191 (EntityLifecycle) |
| `Pomo.Core/Systems/AISystem.fs`                 | `isHostileFaction` logic            | Lines 196-205                                      |
| `Pomo.Core/Systems/ResourceManager.fs`          | Damage handling (death trigger)     | Lines 21-45 (handleDamageDealt)                    |
| `Pomo.Core/Systems/EntitySpawner.fs`            | Entity spawning/finalization        | Lines 157-304 (finalizeSpawn), 309-384 (system)    |
| `Pomo.Core/MapSpawning.fs`                      | Spawn candidate extraction          | Lines 182-203 (resolveEntityFromGroup)             |
| `Pomo.Core/CompositionRoot.fs`                  | Spawn orchestration                 | Lines 236-297 (spawnEntitiesForMap)                |
| `Pomo.Core/Content/Maps/Proto.ai-entities.json` | Entity group definitions            | Entire file (~33 lines)                            |

### Critical Conventions

1. **Use `voption`** not `option` for struct-like optional values
2. **Events flow through `EventBus`** - systems publish/subscribe
3. **State lives in `World`** as `cmap` (changeable maps)
4. **Per-scenario tracking**: Use `ScenarioId` from `world.EntityScenario`

---

## Goal

Enable AI entities to:

1. Fight entities of **different factions** (not same faction)
2. **Die** when HP ≤ 0 (emit `EntityDied` event)
3. **Respawn** to maintain `MaxEnemyEntities` per scenario, respecting per-zone `MaxSpawns`

---

## Implementation Tasks

### 1. Expand Faction System

**File**: `Pomo.Core/Domain/Entity.fs`

Add 10 team-color factions to the `Faction` DU (after line 39):

```fsharp
[<Struct>]
type Faction =
  | Player
  | NPC
  | Ally
  | Enemy
  | AIControlled
  | TeamRed
  | TeamBlue
  | TeamGreen
  | TeamYellow
  | TeamOrange
  | TeamPurple
  | TeamPink
  | TeamCyan
  | TeamWhite
  | TeamBlack
```

Update `Faction.decoder` (around line 129) to handle new variants.

---

### 2. Update Hostility Rules

**File**: `Pomo.Core/Systems/AISystem.fs` (lines 196-205)

Replace `isHostileFaction` with:

```fsharp
let inline isHostileFaction
  (controllerFactions: Faction HashSet)
  (targetFactions: Faction HashSet)
  =
  // Rule 1: Same faction NEVER attacks same faction
  let hasOverlap =
    controllerFactions |> Seq.exists targetFactions.Contains
  if hasOverlap then false
  else
    // Rule 2: Ally and Player don't attack each other
    let isAllyOrPlayer =
      controllerFactions.Contains Ally || controllerFactions.Contains Player
    let targetIsAllyOrPlayer =
      targetFactions.Contains Ally || targetFactions.Contains Player
    if isAllyOrPlayer && targetIsAllyOrPlayer then false
    else true // All other combinations are hostile
```

---

### 3. Add EntityDied Event

**File**: `Pomo.Core/Domain/Events.fs`

Add in `SystemCommunications` module:

```fsharp
[<Struct>]
type EntityDied = {
  EntityId: Guid<EntityId>
  ScenarioId: Guid<ScenarioId>
}
```

---

### 4. Refactor SpawnType

**File**: `Pomo.Core/Domain/Events.fs`

Replace tuple-based `SpawnType.Enemy` with struct:

```fsharp
[<Struct>]
type FactionSpawnInfo = {
  ArchetypeId: int<AiArchetypeId>
  EntityDefinitionKey: string voption
  MapOverride: MapEntityOverride voption
  Faction: Entity.Faction voption
  SpawnZoneName: string voption
}

[<RequireQualifiedAccess>]
type SpawnType =
  | Player of playerIndex: int
  | Faction of FactionSpawnInfo
```

Update all usages (EntitySpawner.fs, CompositionRoot.fs).

---

### 5. Add Faction to MapEntityGroup

**File**: `Pomo.Core/Domain/AI.fs`

Update `MapEntityGroup`:

```fsharp
type MapEntityGroup = {
  Entities: string[]
  Weights: float32[] voption
  Overrides: HashMap<string, MapEntityOverride>
  Faction: Entity.Faction voption
}
```

Update decoder to parse optional `"Faction"` field.

---

### 6. Emit EntityDied on Death

**File**: `Pomo.Core/Systems/ResourceManager.fs`

In `handleDamageDealt` (after line 37), add:

```fsharp
if newHP <= 0 then
  let scenarioId =
    world.EntityScenario
    |> AMap.force
    |> HashMap.tryFindV event.Target
  match scenarioId with
  | ValueSome sid ->
    eventBus.Publish({ EntityId = event.Target; ScenarioId = sid }: SystemCommunications.EntityDied)
  | ValueNone -> ()
```

---

### 7. Add Respawn Handling

**File**: `Pomo.Core/Systems/EntitySpawner.fs`

Add to `EntitySpawnerSystem`:

1. Track per-scenario spawn zones: `Dictionary<Guid<ScenarioId>, SpawnZoneState[]>`
2. Subscribe to `EntityDied`:
   - Emit `EntityLifecycle(Removed entityId)`
   - Decrement zone count
   - If below limits, emit new `SpawnEntityIntent`
3. In `finalizeSpawn`, use faction from `FactionSpawnInfo`

---

### 8. Update Content

**File**: `Pomo.Core/Content/Maps/Proto.ai-entities.json`

Add `"Faction"` to each group:

```json
{
  "magic_casters": {
    "Entities": ["FireMage", "IceMage", "ElementalCaster"],
    "Faction": "TeamRed",
    ...
  },
  "melee_fighters": {
    "Entities": ["Warrior", "Berserker"],
    "Faction": "TeamBlue",
    ...
  }
}
```

---

## Verification

```bash
cd /home/amunoz/repos/Kipo
dotnet build Pomo.DesktopGL/Pomo.DesktopGL.fsproj
cd Pomo.DesktopGL && dotnet run
```

1. Navigate to Proto map
2. Observe TeamRed vs TeamBlue entities fighting
3. Kill an entity → confirm death → confirm respawn
4. Verify same-team entities don't attack each other

# StateWriteService Migration - Session Notes

> **For LLM Assistants:** This document summarizes the state event migration work. Use it to continue any remaining refactoring.

---

## Why We Removed State-Only Events

The game previously used an event-driven architecture where systems published `StateChangeEvent` instances to an `EventBus`. The `StateUpdateSystem` applied these changes to `MutableWorld`.

**Solution:** Replaced state-only events with direct calls to `IStateWriteService`, which queues writes and flushes them atomically at end-of-frame via `transact()`.

---

### Phase 5-8: State-Only Event Elimination ✅

Eliminated all state-only events and the `StateUpdateSystem`. The `EventBus` now only carries events with multiple subscribers or complex lifecycle requirements.

| Category        | Status       | Details                                                                                                        |
| --------------- | ------------ | -------------------------------------------------------------------------------------------------------------- |
| **Inventory**   | ✅ Completed | All events migrated to `StateWriteService`. `InventoryEvents` type removed.                                    |
| **AI**          | ✅ Completed | `ControllerUpdated` migrated to `UpdateAIController`. `AIStateChange` type removed.                            |
| **Combat**      | ✅ Completed | `ResourcesChanged`, `Cooldowns`, `InCombat`, `Effect*`, `PendingSkill*` migrated. `CombatEvents` type removed. |
| **Projectiles** | ✅ Completed | `CreateProjectile` migrated. `Projectile.fs` updated to use `RemoveEntity` direct write.                       |
| **Input**       | ✅ Completed | `RawStateChanged`, `GameActionStatesChanged`, `ActiveActionSetChanged` migrated to direct writes.              |
| **Movement**    | ✅ Completed | `MovementStateChanged` migrated to direct writes in `Navigation.fs` and `MovementLogic.fs`.                    |
| **Lifecycle**   | ✅ Completed | `Removed` and `EntitySpawned` migrated to `RemoveEntity` and `ApplyEntitySpawnBundle`.                         |

### Events to Keep (Cross-System Notifications)

These events remain on the `EventBus` for side-effects (visuals, UI, clear pending logic), but **do not** drive the core state update loop:

- `MovementStateChanged`: Still published for `AbilityActivation.fs`.
- `RawStateChanged`: Still published for `Targeting.fs`.
- `GameActionStatesChanged`: Still published for side-effect systems.
- `LifecycleEvent.ProjectileImpacted`: Subscribed by `Combat.fs` for damage/effect resolution.

---

## Completed Infrastructure & Performance Optimization ✅

1. **World.fs Dictionary Migration**:

   - Migrated `Positions`, `Velocities`, `Rotations`, `Factions`, etc., from `cmap/amap` to `System.Collections.Generic.Dictionary`.
   - Goal: Fully eliminated FSharp.Adaptive overhead for core physical properties and lookups.

2. **EntityExists HashSet**:

   - Implemented `HashSet<Guid<EntityId>>` for fast O(1) existence checks.
   - **CRITICAL**: All state updates (Position, Velocity, etc.) are gated by `EntityExists.Contains` to ensure data integrity. New entities must be registered in `applyCommand` for `AddEntity` and `CreateProjectile`.

3. **Zero-Allocation CommandBuffer**:

   - Optimized `CommandBuffer` using `System.Buffers.ArrayPool<Command>`.
   - Eliminated heap allocations during the high-frequency mutation loop.

4. **State.fs Cleanup**:
   - Removed `StateUpdateSystem`.
   - Purged truly dead helper functions.
   - Preserved modular helpers (Entity, Combat, Inventory, AI) to keep `applyCommand` readable and maintainable.

---

## Key Files

| File                           | Purpose                                          |
| ------------------------------ | ------------------------------------------------ |
| `Pomo.Core/Environment.fs`     | `IStateWriteService` interface definition        |
| `Pomo.Core/Systems/State.fs`   | StateWrite implementation and buffered mutations |
| `Pomo.Core/Domain/Events.fs`   | Event type definitions (minimized)               |
| `Pomo.Core/CompositionRoot.fs` | Service injection and `FlushWrites()` call       |

---

## Build Command

```bash
cd ~/repos/Kipo/Pomo.Core
dotnet build
```

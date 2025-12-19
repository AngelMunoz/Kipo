# StateWriteService Migration - Session Notes

> **For LLM Assistants:** This document summarizes the state event migration work. Use it to continue the refactoring in follow-up sessions.

---

## Why We Are Removing State-Only Events

The game uses an event-driven architecture where systems publish `StateChangeEvent` instances to an `EventBus`. The `StateUpdateSystem` subscribes and applies these changes to `MutableWorld`.

**Problem:** Many events are _state-only_ - they have only one consumer (StateUpdateSystem). This creates unnecessary overhead:

- Event allocation and GC pressure every frame
- Observable/subscription dispatch overhead
- Unnecessary indirection for simple state writes

**Solution:** Replace state-only events with direct calls to `IStateWriteService`, which queues writes and flushes them atomically at end-of-frame via `transact()`.

---

### Phase 5-8: State-Only Event Elimination ✅

Eliminated all identified state-only events. The `EventBus` now only carries events with multiple subscribers or complex lifecycle requirements.

| Category        | Status        | Details                                                                                                              |
| --------------- | ------------- | -------------------------------------------------------------------------------------------------------------------- |
| **Inventory**   | ✅ Eliminated | All 7 events migrated to `StateWriteService` or removed as dead. `InventoryEvents` type removed.                     |
| **AI**          | ✅ Eliminated | `ControllerUpdated` migrated to `UpdateAIController`. `AIStateChange` type removed.                                  |
| **Combat**      | ✅ Eliminated | `ResourcesChanged`, `Cooldowns`, `InCombat`, `Effect*`, `PendingSkill*` migrated. `CombatEvents` type removed.       |
| **Projectiles** | ✅ Eliminated | `CreateProjectile` migrated. `Projectile.fs` updated to use `RemoveEntity` direct write.                             |
| **Input**       | 🔄 Partial    | `RawStateChanged`, `GameActionStatesChanged`, `ActiveActionSetChanged` KEPT (multi-subscriber). Dead events removed. |

### Events to Keep (Multi-Subscriber)

These events represent true inter-system communication and will remain on the `EventBus`:

- `MovementStateChanged`: Subscribed by `State.fs` (state) and `AbilityActivation.fs` (clear pending).
- `RawStateChanged`: Subscribed by `State.fs` (input update) and `Targeting.fs` (mode/target resolution).
- `GameActionStatesChanged`: Subscribed by `State.fs` and `ActionHandler.fs`.
- `ActiveActionSetChanged`: Published by `ActionHandler.fs`, used by `State.fs`.
- `EntityLifecycleEvents`: Complex spawning/removal workflows.
- `LifecycleEvent.ProjectileImpacted`: Subscribed by `Combat.fs` for damage/effect resolution.

---

## What Remains

### Infrastructure & Performance Optimization

1. **World.fs Dictionary Migration** (CRITICAL):

   - Convert `Positions`, `Velocities`, `Rotations` from `cmap` to `System.Collections.Generic.Dictionary<Guid<EntityId>, _>`.
   - Update `Projections.fs` to read from `IReadOnlyDictionary`.
   - Goal: Fully eliminate FSharp.Adaptive overhead for core physical properties.

2. **EntityExists HashSet**:

   - Add `HashSet<Guid<EntityId>>` for fast existence checks during `applyCommand`.

3. **Batching & Buffering**:
   - Evaluate if `CommandBuffer` needs further optimization (e.g., `ArrayPool` for `ResizeArray` backing).
   - Currently using a fixed 2048 initial capacity which is sufficient for current scale.

---

## How to Continue

1. **Pick an event category** (e.g., InputEvents, remaining CombatEvents)
2. **Verify it's state-only** via grep:
   ```
   grep -r "EventName" Pomo.Core --include="*.fs"
   ```
   If only `Events.fs` (definition) and `State.fs` (handler) appear, it's state-only.
3. **Add method to IStateWriteService** in `Environment.fs`
4. **Add Command case** in `State.fs` StateWrite module
5. **Add applyCommand handler** in StateWrite module
6. **Add interface implementation** in `StateWrite.create`
7. **Remove event from Events.fs**
8. **Fix compilation errors** - update publishers to use `stateWrite.NewMethod()`
9. **Remove pattern match** from StateUpdateSystem in `State.fs`
10. **Build and test**

---

## Key Files

| File                           | Purpose                                        |
| ------------------------------ | ---------------------------------------------- |
| `Pomo.Core/Environment.fs`     | `IStateWriteService` interface definition      |
| `Pomo.Core/Systems/State.fs`   | StateWrite module + StateUpdateSystem handlers |
| `Pomo.Core/Domain/Events.fs`   | Event type definitions                         |
| `Pomo.Core/CompositionRoot.fs` | Service injection                              |

---

## Build Command

```powershell
cd c:\Users\scyth\repos\Kipo
dotnet build Pomo.Core
```

All changes should compile before testing in-game.

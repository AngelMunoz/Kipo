# StateWriteService Migration - Session Notes

> **For LLM Assistants:** This document summarizes the state event migration work. Use it to continue the refactoring in follow-up sessions.

---

## Why We Are Removing State-Only Events

The game uses an event-driven architecture where systems publish `StateChangeEvent` instances to an `EventBus`. The `StateUpdateSystem` subscribes and applies these changes to `MutableWorld`.

**Problem:** Many events are *state-only* - they have only one consumer (StateUpdateSystem). This creates unnecessary overhead:
- Event allocation and GC pressure every frame
- Observable/subscription dispatch overhead
- Unnecessary indirection for simple state writes

**Solution:** Replace state-only events with direct calls to `IStateWriteService`, which queues writes and flushes them atomically at end-of-frame via `transact()`.

---

## What Was Completed

### Phase 1: StateWriteService Infrastructure ✅

Created in `Pomo.Core/Systems/State.fs`:
- `StateWrite.Command` struct union for all write command types
- `StateWrite.CommandBuffer` class using `ResizeArray<Command>`
- `StateWrite.create` function returning `IStateWriteService`
- Flush component with `UpdateOrder = 1000`

Added to `Pomo.Core/Environment.fs`:
- `IStateWriteService` interface with all update methods

Integrated in `Pomo.Core/CompositionRoot.fs`:
- Service injection into `CoreServices`
- Disposal handling

### Phase 2-3: World.fs Dictionary Migration ❌ NOT DONE

The original plan included migrating `Positions`, `Velocities`, `Rotations` from `cmap` to `Dictionary<>`. This was deferred - the current implementation still uses `cmap` but writes go through `StateWriteService`.

### Phase 4: StateWriteService Injection ✅

`IStateWriteService` is available via `core.StateWrite` in all systems.

### Phase 5-6: Event Migration ✅ (Partial)

**Events Removed (10 total):**

| Category | Event | Replaced With |
|----------|-------|---------------|
| PhysicsEvents | `PositionChanged` | `UpdatePosition` |
| PhysicsEvents | `VelocityChanged` | `UpdateVelocity` |
| PhysicsEvents | `RotationChanged` | `UpdateRotation` |
| CombatEvents | `ResourcesChanged` | `UpdateResources` |
| CombatEvents | `CooldownsChanged` | `UpdateCooldowns` |
| CombatEvents | `InCombatTimerRefreshed` | `UpdateInCombatTimer` |
| AnimationEvents | `ActiveAnimationsChanged` | `UpdateActiveAnimations` |
| AnimationEvents | `PoseChanged` | `UpdatePose` |
| AnimationEvents | `AnimationStateRemoved` | `RemoveAnimationState` |
| VisualsEvents | `ModelConfigChanged` | `UpdateModelConfig` |

**Event Types Removed Entirely:**
- `AnimationEvents` (all 3 events migrated)
- `VisualsEvents` (single event migrated)
- Removed `Visuals` and `Animation` cases from `StateChangeEvent`

**Systems Updated:**
- `Movement.fs`, `MovementLogic.fs`, `PlayerMovement.fs`, `UnitMovement.fs`
- `Projectile.fs`, `Collision.fs`
- `ResourceManager.fs`, `Combat.fs`, `AbilityActivation.fs`
- `AnimationSystem.fs`, `MotionStateAnimation.fs`
- `State.fs` (removed pattern matches for migrated events)

---

## What Remains

### Pending General Plan Items

1. **World.fs Dictionary Migration** - Convert `Positions`, `Velocities`, `Rotations` from `cmap` to `Dictionary<>` for better performance
2. **EntityExists HashSet** - Add tracking set for entity existence
3. **Projections.fs Update** - Read from `IReadOnlyDictionary` instead of `AMap.force`

### Pending State-Only Events

Current `StateChangeEvent` structure:
```fsharp
type StateChangeEvent =
  | EntityLifecycle of EntityLifecycleEvents  // Keep - complex spawn workflows
  | Input of InputEvents                      // 5 events - candidates
  | Physics of PhysicsEvents                  // 1 event (MovementStateChanged - has 2 subscribers, keep)
  | Combat of CombatEvents                    // 9 events remaining
  | Inventory of InventoryEvents              // 7 events - candidates
  | AI of AIStateChange                       // 1 event (ControllerUpdated - candidate)
  | CreateProjectile of ...                   // 1 event - candidate
```

**State-Only Candidates (verified via grep - only consumed by State.fs):**

| Event | Publisher | Migrate? |
|-------|-----------|----------|
| `FactionsChanged` | Not published anywhere | ✅ Can remove entirely |
| `BaseStatsChanged` | Not published anywhere | ✅ Can remove entirely |
| `StatsChanged` | Not published anywhere | ✅ Can remove entirely |
| `EffectApplied` | EffectApplication.fs | ✅ Migrate |
| `EffectExpired` | Check needed | Likely migrate |
| `EffectRefreshed` | Check needed | Likely migrate |
| `EffectStackChanged` | Check needed | Likely migrate |
| `PendingSkillCastSet/Cleared` | Check needed | Likely migrate |
| `ControllerUpdated` | AISystem.fs | ✅ Migrate |
| `InputEvents` (all 5) | InputSystem | ✅ Migrate |
| `InventoryEvents` (all 7) | Inventory systems | Needs verification |
| `CreateProjectile` | Combat.fs | ✅ Migrate |

**Events to Keep (multiple subscribers):**
- `MovementStateChanged` - AbilityActivation subscribes to clear pending casts
- `EntityLifecycleEvents` - Complex spawn/death workflows with multiple listeners

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

| File | Purpose |
|------|---------|
| `Pomo.Core/Environment.fs` | `IStateWriteService` interface definition |
| `Pomo.Core/Systems/State.fs` | StateWrite module + StateUpdateSystem handlers |
| `Pomo.Core/Domain/Events.fs` | Event type definitions |
| `Pomo.Core/CompositionRoot.fs` | Service injection |

---

## Build Command

```powershell
cd c:\Users\scyth\repos\Kipo
dotnet build Pomo.Core
```

All changes should compile before testing in-game.

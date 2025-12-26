# FDA Performance Improvements: Action Plan

This document outlines the implementation plan for fixing FDA collection anti-patterns identified in [`fda_antipattern_fixes.md`](./fda_antipattern_fixes.md).

---

## Phase 1: Quick Wins (Low Risk, High Impact)

### 1.1 Fix `Dictionary.toHashMap` Implementation
**File**: `Pomo.Core/Extensions.fs`
**Impact**: Reduces O(N log N) to O(N) for all HashMap conversions

```diff
- let inline toHashMap(dict: IReadOnlyDictionary<'Key, 'Value>) =
-   let mutable acc = HashMap.empty
-   for kv in dict do
-     acc <- HashMap.add kv.Key kv.Value acc
-   acc
+ let inline toHashMap(dict: IReadOnlyDictionary<'Key, 'Value>) =
+   dict |> Seq.map (fun kv -> kv.Key, kv.Value) |> HashMap.ofSeq
```

**Verification**: Build succeeds, run existing tests.

---

## Phase 2: Cache MovementSnapshot ~~(Critical Path)~~ **DEFERRED**

> [!WARNING]
> **Deferred to Phase 5 (Hybrid Architecture).** Caching the snapshot output breaks the game because `MovementSystem` writes projected positions during the frame, and later systems expect to see updated data. This is an architectural issue that requires the hybrid hot/warm/cold state separation.

---

## Phase 3: Optimize Internal Snapshot Building ✅ **COMPLETE**

### 3.1 Refactor `calculateMovementSnapshot`
**File**: `Pomo.Core/Projections.fs`

**Implemented:** Use mutable builders instead of immutable adds:
```fsharp
let calculateMovementSnapshotOptimized ... =
  let positionsBuilder = Dictionary<_, _>()
  let gridBuilder = Dictionary<_, ResizeArray<_>>()
  // ... build with O(1) mutations
  // Convert once at end with HashMap.ofSeq
```

### 3.2 Consider Changing Snapshot Type
**Optional**: Change `MovementSnapshot.SpatialGrid` from `HashMap<_, IndexList<_>>` to `Dictionary<_, Guid<EntityId>[]>` since it's ephemeral per-frame data.

---

## Phase 4: Optimize AnimationSystem

### 4.1 Use Mutable Builders for Poses
**File**: `Pomo.Core/Systems/AnimationSystem.fs`

```fsharp
let processEntityAnimations ... =
  let poseBuilder = Dictionary<string, Matrix>()
  let animBuilder = ResizeArray<AnimationState>()
  // Build with O(1) mutations, convert at end
```

---

## Phase 5: Hybrid Architecture (Long-term)

### 5.1 Separate Hot/Warm/Cold State
**Files**: `Pomo.Core/Domain/World.fs`, `Pomo.Core/Systems/State.fs`
**Risk**: High - touches core architecture

Move per-frame data out of FDA wrappers:
```fsharp
type MutableWorld = {
  // HOT - Plain mutable
  Positions: Dictionary<Guid<EntityId>, Vector2>
  Velocities: Dictionary<Guid<EntityId>, Vector2>
  Rotations: Dictionary<Guid<EntityId>, float32>

  // WARM/COLD - Keep reactive
  Resources: cmap<Guid<EntityId>, EntityResources>
  // ...
}
```

**This requires**:
- Updating all position/velocity read sites
- Removing `Dictionary.toHashMap` calls
- Updating `State.fs` write handlers

---

## Recommended Order of Execution

| Step | Task | Effort | Risk | Dependency |
|------|------|--------|------|------------|
| 1 | Fix `toHashMap` | 5 min | Low | None |
| 2 | Add frame counter | 10 min | Low | None |
| 3 | Add snapshot cache | 30 min | Low | Step 2 |
| 4 | Update snapshot callers | 1 hr | Low | Step 3 |
| 5 | Optimize snapshot building | 45 min | Low | Step 3 |
| 6 | Optimize AnimationSystem | 30 min | Low | None |
| 7 | Hybrid architecture | 4-8 hr | High | After profiling |

---

## Success Metrics

- **GC Allocations**: Measure with VS Profiler before/after Phase 2
- **Frame Time**: Should see reduction in CPU time for:
  - `ComputeMovementSnapshot` (from 17× to 1× per frame)
  - `HashMap.add` allocations
- **Target**: <5ms total for all movement systems at 100 entities

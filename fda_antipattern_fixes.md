# FDA Anti-Pattern Fixes

This document details the confirmed anti-patterns in our codebase and provides actionable fixes.

---

## Issue #1: `ComputeMovementSnapshot` Not Cached

### Root Cause
`ComputeMovementSnapshot` is a **regular method** (not an `aval`), so each call:
1. Calls `Dictionary.toHashMap` 3 times (O(N log N) each)
2. Runs `calculateMovementSnapshot` which loops with `HashMap.add` per entity

Called from **17+ systems per frame**.

### Evidence
```fsharp
// Projections.fs:467-482
member _.ComputeMovementSnapshot(scenarioId) =
  let velocities = world.Velocities |> Dictionary.toHashMap  // NEW HashMap each call
  let positions = world.Positions |> Dictionary.toHashMap    // NEW HashMap each call
  // ...
  calculateMovementSnapshot time velocities positions ...    // Loops with HashMap.add
```

### Fix Options

> [!WARNING]
> **Do NOT use `aval` or `amap` for movement snapshots.** Position/velocity data changes every frame, and FDA incurs overhead per change (delta allocation, `MarkOutdated` propagation). FDA is designed for event-driven changes, not per-frame data.

**Option A: Per-Frame Cache (Recommended)**

Cache snapshots per scenario per frame:

```fsharp
type ProjectionsImpl() =
  let mutable lastFrame = -1
  let mutable cachedSnapshots = Dictionary<Guid<ScenarioId>, MovementSnapshot>()

  member _.GetMovementSnapshot(scenarioId, currentFrame) =
    if currentFrame <> lastFrame then
      cachedSnapshots.Clear()
      lastFrame <- currentFrame

    match cachedSnapshots.TryGetValue scenarioId with
    | true, snapshot -> snapshot
    | false, _ ->
      let snapshot = computeSnapshot scenarioId  // Use mutable builders internally
      cachedSnapshots.[scenarioId] <- snapshot
      snapshot
```

**Option B: Batch System Updates (Architectural)**

Create a `MovementSystem` that computes the snapshot ONCE per frame and other systems read from it.

---

## Issue #2: `Dictionary.toHashMap` Uses O(N log N) Loop

### Root Cause
Current implementation uses `HashMap.add` in a loop, creating new tree nodes per element.

### Evidence
```fsharp
// Extensions.fs:17-23
let inline toHashMap(dict: IReadOnlyDictionary<'Key, 'Value>) =
  let mutable acc = HashMap.empty
  for kv in dict do
    acc <- HashMap.add kv.Key kv.Value acc  // O(log N) per iteration
  acc
```

### Fix

Use `HashMap.OfSeq` which uses `addInPlace` internally for O(N):

```fsharp
// Extensions.fs - FIXED
let inline toHashMap(dict: IReadOnlyDictionary<'Key, 'Value>) =
  dict
  |> Seq.map (fun kv -> struct (kv.Key, kv.Value))
  |> HashMap.ofSeq
```

Or if you need reference tuple compatibility:
```fsharp
let inline toHashMap(dict: IReadOnlyDictionary<'Key, 'Value>) =
  dict |> Seq.map (fun kv -> kv.Key, kv.Value) |> HashMap.ofSeq
```

---

## Issue #3: AnimationSystem Pose Building Per Entity

### Root Cause
Builds `HashMap<string, Matrix>` and `IndexList<AnimationState>` per entity per frame using immutable adds.

### Evidence
```fsharp
// AnimationSystem.fs:90-111
let mutable entityPose = HashMap.empty<string, Matrix>
for track in clip.Tracks do
  entityPose <- HashMap.add track.NodeName matrix entityPose  // O(log N) per track

updatedAnims <- IndexList.add newAnimState updatedAnims  // O(log N) per animation
```

### Fix

**For HashMap (poses)**: Build in mutable Dictionary, convert once:

```fsharp
let processEntityAnimations ... =
  let poseBuilder = System.Collections.Generic.Dictionary<string, Matrix>()
  let animBuilder = ResizeArray<AnimationState>()

  for animState in activeAnims do
    match animationStore.tryFind animState.ClipId with
    | ValueSome clip ->
      match updateAnimationState animState clip gameTimeDelta with
      | ValueSome newAnimState ->
        for track in clip.Tracks do
          let matrix = ...
          poseBuilder.[track.NodeName] <- matrix  // O(1) amortized
        animBuilder.Add(newAnimState)  // O(1) amortized
      | ValueNone -> ()
    | ValueNone -> ()

  if animBuilder.Count > 0 then
    let entityPose =
      poseBuilder
      |> Seq.map (fun kv -> struct (kv.Key, kv.Value))
      |> HashMap.ofSeq  // O(N) bulk construction
    let updatedAnims = IndexList.ofSeq animBuilder  // O(N) bulk construction
    ValueSome(updatedAnims, entityPose)
  else
    ValueNone
```

---

## Issue #4: `calculateMovementSnapshot` Internal Loops

### Root Cause
Even if cached, the function itself builds 4 HashMaps using `HashMap.add` in loops.

### Evidence
```fsharp
// Projections.fs:362-403
let mutable newPositions = HashMap.empty
let mutable newGrid = HashMap.empty
// ...
for (id, startPos) in positions do
  newPositions <- newPositions |> HashMap.add id currentPos
  newGrid <- newGrid |> HashMap.add cell (cellContent |> IndexList.add id)
```

### Fix

Use mutable builders, convert at end:

```fsharp
let calculateMovementSnapshot ... =
  let positionsBuilder = Dictionary<_, _>()
  let gridBuilder = Dictionary<_, ResizeArray<_>>()
  let rotationsBuilder = Dictionary<_, _>()
  let modelConfigBuilder = Dictionary<_, _>()

  for (id, startPos) in positions do
    match entityScenarios |> HashMap.tryFindV id with
    | ValueSome sId when sId = scenarioId ->
      let currentPos = ...
      positionsBuilder.[id] <- currentPos
      rotationsBuilder.[id] <- rotation

      match modelConfigIds |> HashMap.tryFindV id with
      | ValueSome configId -> modelConfigBuilder.[id] <- configId
      | ValueNone -> ()

      let cell = Spatial.getGridCell ...
      match gridBuilder.TryGetValue cell with
      | true, list -> list.Add id
      | false, _ -> gridBuilder.[cell] <- ResizeArray([id])
    | _ -> ()

  {
    Positions = positionsBuilder |> Seq.map (fun kv -> kv.Key, kv.Value) |> HashMap.ofSeq
    SpatialGrid = gridBuilder |> Seq.map (fun kv -> kv.Key, IndexList.ofSeq kv.Value) |> HashMap.ofSeq
    Rotations = rotationsBuilder |> Seq.map (fun kv -> kv.Key, kv.Value) |> HashMap.ofSeq
    ModelConfigIds = modelConfigBuilder |> Seq.map (fun kv -> kv.Key, kv.Value) |> HashMap.ofSeq
  }
```

---

## Issue #5: Using FDA Reactive Wrappers for High-Frequency Data

### Root Cause

FDA (FSharp.Data.Adaptive) reactive wrappers (`cmap`, `cval`, `amap`, `aval`) incur overhead **every time data changes**:

From `History.fs`:
```fsharp
// Every change triggers:
let append(op: 'Delta) =
  let s, op = t.tapplyDelta state op  // Apply delta to state
  lv.Value <- t.tmonoid.mappend lv.Value op  // Accumulate deltas

member x.Perform(op: 'Delta) =
  if changed then x.MarkOutdated()  // Propagates through dependency graph
```

**For 100 entities changing position every frame at 60 FPS:**
- 100 delta entries created per frame
- 6,000 delta allocations per second
- `MarkOutdated` propagation through all listeners
- Each forcing reader processes all deltas

### When FDA is Appropriate vs Not

| Data Type | Change Frequency | Use FDA? |
|-----------|------------------|----------|
| Positions, Velocities | Every frame | ❌ NO |
| Rotations | Every frame | ❌ NO |
| Health/Mana | On combat event | ✅ YES |
| Inventory | On pickup/drop | ✅ YES |
| Equipment | On equip/unequip | ✅ YES |
| Active Effects | On apply/expire | ✅ YES |
| AI Controllers | On spawn/destroy | ✅ YES |

### Fix: Hybrid Architecture

Separate "hot" (per-frame) from "warm/cold" (event-driven) data:

```fsharp
type MutableWorld = {
  // HOT PATH - Plain mutable, NO FDA wrapper
  Positions: Dictionary<Guid<EntityId>, Vector2>
  Velocities: Dictionary<Guid<EntityId>, Vector2>
  Rotations: Dictionary<Guid<EntityId>, float32>

  // WARM PATH - Reactive, changes on events
  Resources: cmap<Guid<EntityId>, EntityResources>
  ActiveEffects: cmap<Guid<EntityId>, ActiveEffect IndexList>
  MovementStates: cmap<Guid<EntityId>, MovementState>

  // COLD PATH - Reactive, changes rarely
  EntityInventories: cmap<Guid<EntityId>, HashSet<Guid<ItemInstanceId>>>
  EquippedItems: cmap<Guid<EntityId>, HashMap<EquipSlot, Guid<ItemInstanceId>>>
  Factions: cmap<Guid<EntityId>, Faction>
}
```

### Fix for MovementSnapshot Caching

> [!NOTE]
> See **Issue #1** above for the specific per-frame caching solution.

---

## Summary Priority

| Fix | Impact | Effort | Priority |
|-----|--------|--------|----------|
| Cache `ComputeMovementSnapshot` | 🔴 Critical | Medium | **1** |
| Fix `Dictionary.toHashMap` | 🟡 High | Easy | **2** |
| Fix `calculateMovementSnapshot` internals | 🟡 High | Medium | **3** |
| Fix AnimationSystem builders | 🟢 Medium | Medium | **4** |
| Hybrid Architecture (hot/warm/cold) | 🔴 Critical | High | **5** (long-term) |

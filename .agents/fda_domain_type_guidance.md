# Domain Type Collection Selection Guide

This guide helps you choose the right collection type for domain types. Domain types drive how data is used throughout systems, so picking the right collection is crucial for performance.

---

## Decision Framework

### Question 1: Does this data need reactive change propagation?

**YES** → Use adaptive wrappers (`cmap`, `cset`, `cval`, `amap`, `aset`, `aval`)
**NO** → Use immutable (`HashMap`, `HashSet`, `IndexList`) or mutable (.NET `Dictionary`, `HashSet<T>`)

### Question 2: How often does this data change?

| Change Frequency | Recommended Type |
|------------------|------------------|
| Never (config/static) | `HashMap`, `HashSet`, `IndexList`, or `Dictionary` |
| Rarely (per-session) | `cmap`, `cset` |
| Sometimes (per-event) | `cmap`, `cset`, `cval` |
| Every frame | **Mutable** `.NET Dictionary`/`HashSet<T>` NO FDA |

> [!WARNING]
> **FDA is NOT suitable for per-frame data.** Every change triggers delta allocation and `MarkOutdated` propagation through the dependency graph. For 100 entities at 60 FPS, that's 6,000 delta allocations per second per field.

### Question 3: How is this data accessed?

| Access Pattern | Recommended Type |
|----------------|------------------|
| By unique key | `HashMap<Key, Value>` or `Dictionary<Key, Value>` |
| By membership test | `HashSet<Value>` or `HashSet<T>` |
| Ordered sequence with stable identity | `IndexList<T>` |
| Ordered sequence, frequent rebuild | `ResizeArray<T>` + convert when needed |
| Random index access | `array<T>` or `ResizeArray<T>` |

---

## Specific Recommendations by Use Case

### Entity State (Positions, Velocities, Rotations)

```fsharp
// CURRENT (with issues):
Positions: cmap<Guid<EntityId>, Vector2>  // But accessed as Dictionary

// RECOMMENDED:
// If reactive propagation NOT needed (computed each frame):
Positions: Dictionary<Guid<EntityId>, Vector2>

// If reactive propagation IS needed:
Positions: cmap<Guid<EntityId>, Vector2>  // Keep as-is
```

**Guidance**: If you're already using `Dictionary.toHashMap` to convert, question whether `cmap` is needed at all for that field.

---

### Ephemeral Per-Frame Snapshots

```fsharp
// BAD: Building HashMap in loop every frame
type MovementSnapshot = {
  Positions: HashMap<Guid<EntityId>, Vector2>  // Built with loop adds
}

// GOOD: Use Dictionary during construction
type MovementSnapshot = {
  Positions: IReadOnlyDictionary<Guid<EntityId>, Vector2>
}
// Build with Dictionary<_,_>, expose as IReadOnlyDictionary
```

**Guidance**: Ephemeral types that exist only during frame processing should use mutable .NET collections internally.

---

### Entity-to-Many Relationships (Inventories, Effects, Animations)

```fsharp
// Option A: IndexList (stable identity, supports deltas)
ActiveAnimations: cmap<Guid<EntityId>, AnimationState IndexList>

// Option B: Array (if rebuilding entirely each update)
ActiveAnimations: cmap<Guid<EntityId>, AnimationState array>

// Option C: ResizeArray (if mutations are common)
ActiveAnimations: Dictionary<Guid<EntityId>, ResizeArray<AnimationState>>
```

**Guidance**:
- Use `IndexList` when you need reactive deltas (UI binds to changes)
- Use `array` when you rebuild entirely each frame
- Use `ResizeArray` when you frequently add/remove from the list

---

### Static Configuration (Loaded Once)

```fsharp
// GOOD: HashMap for static config (no reactive wrapper needed)
type ModelConfig = {
  Rig: HashMap<string, RigNode>
  AnimationBindings: HashMap<string, string[]>
}

// Build with HashMap.ofSeq at load time
let config = {
  Rig = rigData |> HashMap.ofSeq
  AnimationBindings = bindings |> HashMap.ofSeq
}
```

**Guidance**: Static data doesn't need `cmap`. Plain `HashMap` built once with bulk construction is ideal.

---

### AI Memory / Dynamic State

```fsharp
// RECOMMENDED: Use mutable Dictionary for frequently updated state
type AIController = {
  Memories: Dictionary<Guid<EntityId>, MemoryEntry>  // Updated often
}

// If you need reactive observation of AI state:
AIControllers: cmap<Guid<EntityId>, AIController>  // Wrapper is reactive
// But internal Memories is mutable Dictionary
```

**Guidance**: Nested collections inside reactive wrappers can be mutable. The outer `cmap` provides reactivity; inner doesn't need it.

---

### Poses (Bone Matrices)

```fsharp
// CURRENT:
Poses: cmap<Guid<EntityId>, HashMap<string, Matrix>>  // HashMap rebuilt per frame

// OPTION A: Fixed array with bone index
Poses: cmap<Guid<EntityId>, Matrix[]>  // Index = bone ID (requires enum/lookup table)

// OPTION B: Mutable Dictionary (if bone count varies)
Poses: Dictionary<Guid<EntityId>, Dictionary<string, Matrix>>

// OPTION C: Keep HashMap but build correctly
// Use Dictionary<string, Matrix> during animation update,
// then HashMap.ofSeq once at end
```

**Guidance**: If bone names are known at compile time, use fixed array. Otherwise, build in Dictionary and convert.

---

## Collection Type Summary Table

| Data Type | Update Frequency | Reactive Needed | Recommended |
|-----------|------------------|-----------------|-------------|
| Entity Positions | Every frame | No | `Dictionary<EntityId, Vector2>` |
| Entity Positions | On event | Yes | `cmap<EntityId, Vector2>` |
| Entity Inventory | On event | Yes | `cmap<EntityId, ItemId HashSet>` |
| Animation State | Every frame | Maybe | `Dictionary<EntityId, AnimationState[]>` |
| Entity Poses | Every frame | No | `Dictionary<EntityId, Matrix[]>` |
| Model Config | Load time | No | `HashMap<string, ModelConfig>` |
| AI Memories | Per decision | No | `Dictionary<EntityId, MemoryEntry>` |
| Spatial Grid | Every frame | No | `Dictionary<GridCell, EntityId list>` |
| Active Effects | On event | Yes | `cmap<EntityId, ActiveEffect IndexList>` |
| Derived Stats | On stat change | Yes | `amap` projection |

---

## Red Flags in Domain Types

🚩 **`cmap<_, HashMap<_, _>>`** - Inner HashMap rebuilt often? Use `Dictionary`

🚩 **`cmap<_, IndexList<_>>`** where list is rebuilt entirely each frame - Use `array` or `ResizeArray`

🚩 **`HashMap` in ephemeral snapshot types** - Use `Dictionary` or `IReadOnlyDictionary`

🚩 **Forcing `amap` then immediately converting** - Question if reactive is needed

🚩 **Multiple layers of adaptive wrappers** - `amap<_, aval<HashMap<_, _>>>` - Simplify if possible

---

## Migration Checklist

When changing a domain type's collection:

1. **[ ] Identify all read sites** - `grep` for the field name
2. **[ ] Identify all write sites** - Look for assignments and updates
3. **[ ] Check if reactive binding exists** - Is there UI or system subscribing to changes?
4. **[ ] Measure before/after** - Profile GC allocations and CPU
5. **[ ] Update related types** - Snapshots, projections, interfaces

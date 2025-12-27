# FSharp.Data.Adaptive Usage Reference

Use this document to review F# code for correct usage of FDA collections:
`HashMap`, `HashSet`, `IndexList`, `amap`, `aset`, `alist`, `aval`, `cmap`, `cset`, `clist`, `cval`.

---

## Quick Reference: Time Complexity

| Operation | HashMap/HashSet | IndexList | .NET Dictionary |
|-----------|-----------------|-----------|-----------------|
| `add` single | O(log₃₂ N) ≈ O(1) | O(log N) | O(1) amortized |
| `add` in loop (N items) | O(N log N) ⚠️ | O(N log N) ⚠️ | O(N) |
| `ofSeq`/`ofArray` bulk | O(N) ✅ | O(N) ✅ | O(N) |
| `tryFind`/`contains` | O(log₃₂ N) ≈ O(1) | O(log N) | O(1) |
| `force` (unchanged) | O(1) cached ✅ | O(1) cached ✅ | N/A |
| `force` (changed) | O(Δ) delta-based | O(Δ) delta-based | N/A |

---

## ✅ Good Patterns

### 1. Bulk Construction from Sequence
```fsharp
// GOOD: O(N) using internal addInPlace
let map = HashMap.ofSeq items
let map = HashMap.ofArray arr
let map = HashMap.ofList lst
let set = HashSet.ofSeq items
let list = IndexList.ofSeq items
let list = IndexList.ofArray arr
```

### 2. Building with Mutable, Converting Once
```fsharp
// GOOD: O(1) per add, O(N) final conversion
let builder = Dictionary<_, _>()
for item in items do
  builder.[item.Key] <- item.Value  // O(1) amortized
let result = builder |> Seq.map (fun kv -> kv.Key, kv.Value) |> HashMap.ofSeq
```

### 3. Single Force Per Frame on Unchanged AVal
```fsharp
// GOOD: O(1) returns cached reference when unchanged
let snapshot = myAMap |> AMap.force
for item in snapshot do
  // process item
```

### 4. Reactive Transformations (Run on Change Only)
```fsharp
// GOOD: Only recomputes when EntityInventories changes
let resolved = world.EntityInventories |> AMap.map (fun _ items -> processItems items)
```

### 5. Using tryFindV with ValueOption
```fsharp
// GOOD: Avoids Option allocation
match map |> HashMap.tryFindV key with
| ValueSome v -> useValue v
| ValueNone -> handleMissing()
```

---

## ⚠️ Anti-Patterns to Flag

### 1. Loop with Immutable Add
```fsharp
// BAD: O(N log N) - creates N tree spines
let mutable acc = HashMap.empty
for item in items do
  acc <- HashMap.add item.Key item.Value acc  // ⚠️ FLAG THIS

// BAD: Same issue with IndexList
let mutable list = IndexList.empty
for item in items do
  list <- IndexList.add item list  // ⚠️ FLAG THIS
```

**Fix**: Use mutable builder + bulk conversion (see Good Pattern #2).

### 2. Conversion to HashMap Using Loop
```fsharp
// BAD: O(N log N)
let inline toHashMap(dict: IReadOnlyDictionary<'Key, 'Value>) =
  let mutable acc = HashMap.empty
  for kv in dict do
    acc <- HashMap.add kv.Key kv.Value acc  // ⚠️ FLAG THIS
  acc

// GOOD: O(N)
let inline toHashMap(dict) =
  dict |> Seq.map (fun kv -> kv.Key, kv.Value) |> HashMap.ofSeq
```

### 3. Non-Cached Snapshot Function Called Multiple Times
```fsharp
// BAD: If called from multiple systems per frame
member _.ComputeSnapshot() =
  let data = world.Data |> SomeConversion  // ⚠️ Runs every call
  buildSnapshot data

// GOOD: Cache result or make reactive
let cachedSnapshot = lazy (buildSnapshot data)
member _.Snapshot = cachedSnapshot.Value

// BETTER: Make it an aval
let snapshot: aval<Snapshot> = adaptive { ... }
```

### 4. Force Inside Nested Loop
```fsharp
// BAD: Forces N times when once would suffice
for entity in entities do
  let data = world.SomeMap |> AMap.force  // ⚠️ Move outside loop
  match data |> HashMap.tryFind entity with ...

// GOOD: Force once
let data = world.SomeMap |> AMap.force
for entity in entities do
  match data |> HashMap.tryFind entity with ...
```

### 5. HashMap.map/filter/choose Creating New Collections in Hot Path
```fsharp
// CAUTION: Creates new HashMap - acceptable in reactive context, not in per-frame loops
let filtered = snapshot.Positions |> HashMap.filter predicate  // ⚠️ If called every frame

// BETTER: Filter during iteration
for (id, pos) in snapshot.Positions do
  if predicate id pos then ...
```

### 6. Nested IndexList.add in HashMap Update
```fsharp
// BAD: Double O(log N) penalty
newGrid <- newGrid |> HashMap.add cell (existingList |> IndexList.add id)
```

### 7. Using FDA Reactive Wrappers for Per-Frame Data
```fsharp
// BAD: FDA incurs overhead on EVERY change (delta allocation, MarkOutdated propagation)
Positions: cmap<Guid<EntityId>, Vector2>  // ⚠️ Changes every frame = 6000+ deltas/sec

// GOOD: Plain mutable for per-frame data
Positions: Dictionary<Guid<EntityId>, Vector2>
```

> [!WARNING]
> FDA is designed for **event-driven** changes, NOT per-frame updates. For data that changes every frame (positions, velocities, rotations), use plain `.NET Dictionary` or arrays.

---

## Checklist for Code Review

When reviewing a file, check:

1. **[ ] Search for `HashMap.add` or `HashSet.add` in loops**
   - Is it inside a `for`/`while` loop? → Flag as anti-pattern
   - Is it a one-time operation (e.g., load-time)? → Acceptable

2. **[ ] Search for `IndexList.add` in loops**
   - Same criteria as above

3. **[ ] Search for functions that call `AMap.force` or `Dictionary.toHashMap`**
   - Is this function called multiple times per frame from different systems?
   - Is the result cached?

4. **[ ] Check `HashMap.map`/`filter`/`choose` usage**
   - Is it inside an `AMap.map` (reactive)? → OK
   - Is it called every frame imperatively? → Consider inline filtering

5. **[ ] Look for conversion patterns**
   - `Dictionary` → `HashMap`: Should use `HashMap.ofSeq`
   - `seq { for ... }` → Should use bulk construction

6. **[ ] Check adaptive collection access patterns**
   - Multiple `force` calls on same `aval` in one function? → Cache locally
   - `force` inside a loop? → Move outside

---

## FDA Internal Implementation Notes

### HashMap/HashSet (HAMT)
- **Structure**: Hash Array Mapped Trie with 32-way branching
- **`addInPlace`**: Mutates during bulk construction (O(N))
- **`add`**: Creates new spine nodes (O(log₃₂ N) per call)
- Source: `HashCollections.fs`

### IndexList (Balanced Tree)
- **Structure**: Wraps `MapExt<Index, 'T>` (AVL tree)
- **Index**: Uses linked list with uint64 tags for ordering
- **`add`**: O(log N) - creates new path from root to leaf
- Source: `IndexList.fs`, `Index.fs`, `MapExt.fs`

### Adaptive Collections (amap, alist, aset)
- **History**: Maintains version chain with `RelevantNode` linked list
- **Caching**: `OutOfDate` flag controls recomputation
- **Delta propagation**: Only changed elements processed
- Source: `History.fs`, `AdaptiveMap.fs`, `AdaptiveIndexList.fs`

### AVal.force / AMap.force
- **When unchanged**: Returns cached state reference immediately (O(1))
- **When changed**: Processes delta chain (O(Δ))
- Source: `History.fs:37-39` (`if x.OutOfDate then ... else empty`)

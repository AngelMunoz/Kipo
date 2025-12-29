# System Compatibility Analysis for True 3D Architecture

> [!IMPORTANT]
> **Decision: True 3D.** The camera is just a view - world uses `WorldPosition` (X/Y/Z) with Y as vertical height. This analysis informed that decision.

---

## Executive Summary

All gameplay systems currently assume **2D coordinates** (`Vector2`). The codebase uses:
- **Position storage**: `Dictionary<EntityId, Vector2>`
- **Spatial grid**: `GridCell = { X: int; Y: int }` (2D)
- **Collision**: 2D SAT (Separating Axis Theorem) with 2D polygons
- **Pathfinding**: 2D A* with `NavGrid.IsBlocked: bool[,]`

**Good news**: The rendering pipeline already supports altitude as a **separate parameter** (`LogicRender.toRender(logicPos: Vector2, altitude: float32, ppu)`), meaning visual height can be added without changing the core coordinate system.

---

## System-by-System Analysis

### 🔴 Systems Requiring Changes for 3D

| System | File | 2D Assumption | Impact |
|--------|------|---------------|--------|
| **Position Storage** | `World.fs` | `Positions: Dictionary<EntityId, Vector2>` | Core data structure - all systems depend on this |
| **Velocity/Movement** | `State.fs` | `UpdatePosition(id, Vector2)` | State command uses `Vector2` |
| **Collision** | `Collision.fs` | 2D SAT/MTV, `GridCell {X;Y}` | Entire collision logic is 2D |
| **Spatial Partitioning** | `Spatial.fs` | `GridCell = {X:int; Y:int}` | No Z coordinate |
| **Pathfinding** | `Pathfinding.fs`, `Navigation.fs` | `NavGrid.IsBlocked: bool[,]` | 2D grid only |
| **AI Perception** | `AISystem.fs` | `Vector2.Distance`, 2D FOV cones | All distance checks are 2D |
| **Targeting** | `Targeting.fs` | Range checks use `Vector2.Distance` | 2D range validation |
| **Projectile** | `Projectile.fs` | `Vector2` positions/targets | Movement in 2D plane |
| **Combat/Skills** | `Combat.fs`, `AbilityActivation.fs` | AoE shapes are 2D | Circle/Cone/Line in 2D |
| **MovementSnapshot** | `Projections.fs` | `Positions: IReadOnlyDictionary<EntityId, Vector2>` | Used by rendering and all queries |

### 🟡 Systems Needing Minor Changes

| System | File | Current State | Required Change |
|--------|------|---------------|-----------------|
| **Camera** | `Camera.fs`, `CameraSystem.fs` | `ScreenToWorld` returns `Vector2` | Add optional height projection |
| **RenderMath** | `RenderMath.fs` | `toRender(Vector2, altitude, ppu)` | Already supports altitude! |
| **EntityEmitter** | `EntityEmitter.fs` | Takes `MovementSnapshot` | Extend to include height per entity |

### 🟢 Systems That Work Without Changes

| System | File | Why No Change Needed |
|--------|------|----------------------|
| **Animation** | `AnimationSystem.fs` | Works on model transforms, not positions |
| **Effects/Buffs** | `EffectApplication.fs` | Entity ID based, position-agnostic |
| **Inventory/Items** | `Inventory.fs`, `Equipment.fs` | No spatial logic |
| **UI/HUD** | `UISystem.fs`, `HUDComponents.fs` | Screen space, not world space |
| **Particles** | `ParticleSystem.fs` | Already uses 3D `Vector3` internally |

---

## Key Files with `Vector2.Distance` Usage (33+ occurrences)

```
Systems/AISystem.fs          - 6 uses (perception, targeting, patrol)
Systems/Collision.fs         - 1 use (entity-entity distance)
Systems/Combat.fs            - 1 use (AoE center distance)
Systems/MovementLogic.fs     - 2 uses (waypoint distance)
Systems/Navigation.fs        - 1 use (direct vs A* threshold)
Systems/Projectile.fs        - 2 uses (target acquisition, impact check)
Systems/Targeting.fs         - 2 uses (range validation)
Systems/AbilityActivation.fs - 1 use (skill range check)
Domain/Spatial.fs            - 14+ uses (geometry helpers)
Algorithms/Pathfinding.fs    - 2 uses (LOS, distance)
Projections.fs               - 1 use (nearby entity query)
```

---

## Recommended Architecture: Height as Separate Component

Instead of changing `Vector2` → `Vector3` everywhere, add height as a parallel system:

```fsharp
// NEW: Add to World.fs
Heights: Dictionary<Guid<EntityId>, float32>  // Y-layer elevation

// Position + Height compose to WorldPosition for 3D checks
type WorldPosition = { X: float32; Y: float32; Height: float32 }
```

### Why This Works

1. **Backward Compatible**: `Classic2D` maps ignore `Heights` (all 0.0f)
2. **Surgical Changes**: Only systems that need height check both dictionaries
3. **Rendering Ready**: `LogicRender.toRender` already accepts altitude
4. **Collision Opt-In**: 3D collision only for `BlockGrid3D` maps

---

## Dual-Mode Dispatch Pattern

```fsharp
// Example in Collision.fs
match map.WorldMode with
| Classic2D ->
    // Existing 2D SAT collision
    checkPolygonCollision pos entityPoly mapPolygons
| BlockGrid3D ->
    // NEW: Grid-based 3D collision
    let cell = toGridCell3D pos heights.[entityId]
    checkBlockCollision cell blockGrid
```

---

## Implementation Priority for 3D Support

### Phase A: Visual-Only Height (No Collision)
1. Add `Heights` dictionary to `MutableWorld`
2. Pass height to `LogicRender.toRender` in emitters
3. Block editor places blocks visually at heights
4. **Result**: Beautiful 3D maps, but entities walk through block stacks

### Phase B: Grid-Based Collision
1. Create `BlockGridCollision.fs` with 3D cell occupancy
2. Branch collision system based on `WorldMode`
3. **Result**: Entities blocked by solid blocks at same height

### Phase C: Height-Aware Movement
1. Extend `NavGrid` to 3D or create `BlockNavGrid`
2. Update pathfinding for layer transitions (stairs/ramps)
3. **Result**: Full 3D navigation

---

## Risk Assessment

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Breaking Classic2D | Medium | `WorldMode` dispatch ensures old code path unchanged |
| Performance regression | Low | Heights is O(1) lookup, grid collision is O(1) per cell |
| AI bugs in 3D | Medium | Extensive testing with 3D patrol routes |
| Projectile weirdness | Medium | Descending projectile already uses altitude |

---

## Conclusion: True 3D Architecture Chosen

After design discussion, the decision is **True 3D**:

1. ✅ Rendering already supports altitude - extend to full 3D
2. ✅ Particle system already uses `Vector3`
3. ✅ Clean system boundaries allow surgical updates
4. ⚠️ All gameplay systems use `Vector2` → migrate to `WorldPosition`
5. ⚠️ Pathfinding is 2D → needs 3D nav grid

**Chosen approach**: Replace `Vector2` positions with `WorldPosition(X, Y, Z)` where Y is vertical height. All systems updated consistently. Camera is just a view into 3D world - enables isometric, first-person, cutscenes.

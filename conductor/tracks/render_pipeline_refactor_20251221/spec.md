# Specification: Render Pipeline Redesign

## Overview

A **complete overhaul** of the rendering pipeline to establish a unified coordinate system where **subsystems never think about isometric corrections**. The core problem today: every new visual feature (billboard particles, mesh particles, orbitals) requires re-discovering and re-implementing isometric math, leading to bugs and friction.

> [!IMPORTANT]
> This is a **breaking change**. Existing render systems (`Render.fs`, `TerrainRenderSystem`, particle rendering in `Render.fs`) will be replaced, not incrementally refactored.

## Primary Goal

**One Rule:** If you're not inside `RenderMath`, you don't touch isometric matrices, squish factors, or coordinate corrections.

Subsystems work in **Logic Space** (simple 2D + altitude). The `RenderOrchestrator` handles all isometric conversion internally via `RenderMath`.

---

## Coordinate Spaces

| Space            | Type                                        | Who Uses It                                |
| ---------------- | ------------------------------------------- | ------------------------------------------ |
| **Logic Space**  | `Vector2(x, y)` pixels + `float32 altitude` | All gameplay systems, simulators, emitters |
| **Render Space** | `Vector3` with depth bias encoded           | Internal to `RenderMath` only              |
| **Screen Space** | Pixel coords after camera projection        | Input handling, UI                         |

**The Golden Rule:** Code outside `RenderOrchestrator` never sees `Matrix`, `squishFactor`, or isometric corrections.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    RenderOrchestrator : DrawableGameComponent           │
│                    (THE single render entry point)                      │
├─────────────────────────────────────────────────────────────────────────┤
│  Owns:                                                                  │
│    - Command queues (MeshQueue, BillboardQueue, TerrainQueue)           │
│    - Batch renderers (MeshBatch, BillboardBatch, QuadBatch)             │
│    - Simulation state (VisualEffectState for particles/orbitals)        │
│                                                                         │
│  Draw(gameTime):                                                        │
│    1. Update simulations (pure functions, logic space)                  │
│    2. Clear all command queues                                          │
│    3. For each camera:                                                  │
│       a. TerrainEmitter.emit() → TerrainQueue                           │
│       b. EntityEmitter.emit() → MeshQueue                               │
│       c. ParticleEmitter.emit() → BillboardQueue / MeshQueue            │
│       d. OrbitalEmitter.emit() → MeshQueue                              │
│    4. Sort queues by depth                                              │
│    5. Flush batches to GPU                                              │
└─────────────────────────────────────────────────────────────────────────┘
```

### Emitter Functions (Pure, No Isometric Knowledge)

Emitters are **module-level functions**, not classes. They receive:

- World state (entities, particles, map)
- A `RenderMath` service (for coordinate conversion)
- A command queue to push to

```fsharp
module EntityEmitter =
    let emit (snapshot: MovementSnapshot) (renderMath: RenderMath) (queue: MeshQueue) =
        for entityId, logicPos in snapshot.Positions do
            let cmd = renderMath.CreateMeshCommand(logicPos, altitude, facing, model)
            queue.Add(cmd)
```

Emitters call `renderMath.CreateMeshCommand(...)` or `renderMath.CreateBillboardCommand(...)`. They never compute matrices.

### RenderMath (The ONE Isometric Authority)

All isometric logic lives here and **only here**:

```fsharp
module RenderMath =
    // Camera helpers (used by CameraService)
    let GetViewMatrix (logicPos: Vector2) (ppu: Vector2) : Matrix
    let GetProjectionMatrix (viewport: Viewport) (zoom: float32) (ppu: Vector2) : Matrix
    let ScreenToLogic (screenPos: Vector2) (camera: Camera) : Vector2

    // Command creation (used by Emitters via RenderOrchestrator)
    let CreateMeshCommand (logicPos: Vector2) (altitude: float32) (facing: float32) (model: Model) : MeshCommand
    let CreateBillboardCommand (logicPos: Vector2) (altitude: float32) (size: float32) (texture: Texture) : BillboardCommand
    let CreateTerrainCommand (tilePos: Vector2) (texture: Texture) (size: Vector2) : TerrainCommand
```

### CameraService (Unchanged Role, Uses RenderMath)

```fsharp
type CameraService =
    abstract GetCamera: playerId -> Camera voption
    abstract GetAllCameras: unit -> struct(Guid<PlayerId> * Camera)[]
    abstract ScreenToWorld: screenPos * playerId -> Vector2 voption
```

`CameraService` delegates matrix computation to `RenderMath`:

- `GetViewMatrix(logicPos, ppu)` for the view matrix
- `GetProjectionMatrix(viewport, zoom, ppu)` for projection
- `ScreenToLogic(screenPos, camera)` for picking

---

## Simulation vs Rendering Split

### Current Problem

`Particles.ActiveEffect` mixes simulation state (positions, velocities) with render state (what to draw). Adding mesh particles required threading render concerns through simulation.

### New Design

| Layer          | Responsibility                                             | State                                                |
| -------------- | ---------------------------------------------------------- | ---------------------------------------------------- |
| **Simulation** | Update positions, velocities, lifetimes. Logic space only. | `VisualEffectState` (mutable, owned by orchestrator) |
| **Emitters**   | Read simulation state, emit commands via `RenderMath`      | Stateless functions                                  |
| **Batchers**   | Consume command queues, issue GPU draw calls               | Stateless                                            |

**Terminology change:** Rename `Particles.ActiveEffect` → `VisualEffect` to avoid confusion with `Skill.ActiveEffect`.

---

## Command Structs

Zero-allocation struct commands:

```fsharp
[<Struct>]
type MeshCommand = {
    Model: Model
    WorldMatrix: Matrix  // Pre-computed by RenderMath
    // Optional: tint, animation frame, etc.
}

[<Struct>]
type BillboardCommand = {
    Texture: Texture2D
    Position: Vector3     // Render space (pre-computed)
    Size: float32
    Color: Color
    // Billboard vectors computed at flush time from camera
}

[<Struct>]
type TerrainCommand = {
    Texture: Texture2D
    Position: Vector3     // Render space
    Size: Vector2
}
```

---

## Functional Requirements

1. **Single Entry Point:** `RenderOrchestrator.Draw()` is the only render call. No separate `TerrainRenderService`, `RenderService`.

2. **Isometric Isolation:** Subsystems (particle simulation, orbital calculation, entity transforms) work in logic space. They never import/use `Matrix` or squish factors.

3. **Command Queue Pattern:** All rendering goes through typed struct queues. Queues are cleared each frame.

4. **Depth Sorting:** Commands are sorted by Y-component (depth bias) before flushing. RenderGroup layering (terrain background < entities < terrain foreground) is preserved.

5. **Camera Consistency:** `CameraService` and emitters both use `RenderMath`, guaranteeing visual positions match picking positions.

---

## Non-Functional Requirements

- **Zero Allocations:** Struct commands, `ArrayPool` buffers, no boxing in hot path.
- **Buffer Management:** Auto-shrink logic for sustained low usage.
- **byref semantics:** if necessary, use byref semantics to avoid copying large structs.
- **No GC pressure:** No allocations in hot path.
- **high performance iterations:** where necessary use higher performance iteration techniques and single pass algorithms.
- **low level primitives:** where necessary use low level primitives to avoid allocations like `Span<T>` and `ReadOnlySpan<T>` or `byref` semantics.
- **Parallelization Ready:** Simulation uses pure functions over arrays (`SimulatedParticle[]`, `SimulatedOrbital[]`) enabling `Array.Parallel.map` for particle/orbital updates. Each particle/orbital update is independent with no shared mutable state.

---

## Acceptance Criteria

- [ ] Entities, particles, orbitals, terrain render correctly
- [ ] No isometric math exists outside `RenderMath` module
- [ ] Adding a new visual type requires only: (1) simulation logic, (2) an emitter function—no isometric knowledge needed
- [ ] `CameraService.ScreenToWorld` returns positions that exactly match where entities render
- [ ] Memory profiler shows zero GC pressure during steady-state gameplay

---

## Out of Scope

- Custom shaders (pipeline architecture focus)
- UI system migration (remains separate)

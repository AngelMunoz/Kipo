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

## Architecture (Implemented)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Per Frame Flow                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌──────────────────┐                                                       │
│  │ PoseResolver     │  Sequential: resolves rig hierarchies                 │
│  │ (uses shared     │  Output: ResolvedEntity[]                             │
│  │  nodeTransforms) │                                                       │
│  └────────┬─────────┘                                                       │
│           │                                                                 │
│           ▼                                                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    PARALLEL PHASE                                    │    │
│  ├─────────────────────────────────────────────────────────────────────┤    │
│  │  EntityEmitter.emit            → MeshCommand[]                       │    │
│  │  ParticleEmitter.emitBillboards → BillboardCommand[]                 │    │
│  │  ParticleEmitter.emitMeshes    → MeshCommand[]                       │    │
│  │  TerrainEmitter.emitLayer      → TerrainCommand[]                    │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  ┌──────────────────┐                                                       │
│  │ Batch Renderers  │  Consume command arrays, issue GPU draws              │
│  └──────────────────┘                                                       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Key Design Decision: Return Arrays, Not Mutate Queues

Emitters return `Command[]` instead of mutating queues. This enables:

- **Parallelization** via `Array.Parallel.collect` / `Array.Parallel.choose`
- **Purity** - no side effects, easier to test
- **Composability** - arrays can be concatenated, sorted, filtered

---

## Split Context Design (Implemented)

Instead of one "god object" `EmitterContext`, we use specialized contexts:

```fsharp
/// Shared by all emitters - minimal common dependencies
type RenderCore = {
  PixelsPerUnit: Vector2
}

/// Entity-specific render data
type EntityRenderData = {
  ModelStore: ModelStore
  GetModelByAsset: string -> Model voption
  EntityPoses: HashMap<Guid<EntityId>, HashMap<string, Matrix>>
  LiveProjectiles: HashMap<Guid<EntityId>, LiveProjectile>
  SquishFactor: float32
  ModelScale: float32
}

/// Particle-specific render data
type ParticleRenderData = {
  GetTexture: string -> Texture2D voption
  GetModelByAsset: string -> Model voption
  EntityPositions: HashMap<Guid<EntityId>, Vector2>
  SquishFactor: float32
  ModelScale: float32
}

/// Terrain-specific render data
type TerrainRenderData = {
  GetTileTexture: int -> Texture2D voption
}
```

---

## Pre-Computed Poses (Implemented)

Entity rig traversal is sequential (uses shared `nodeTransformsPool`). Separated into `PoseResolver`:

```fsharp
/// Pre-resolved rig node for command emission
[<Struct>]
type ResolvedRigNode = {
  ModelAsset: string
  WorldMatrix: Matrix
}

/// Pre-resolved entity ready for parallel command emission
type ResolvedEntity = {
  EntityId: Guid<EntityId>
  Nodes: ResolvedRigNode[]
}

module PoseResolver =
  /// Sequential pass - resolves all entity rig hierarchies
  let resolveAll
    (core: RenderCore)
    (data: EntityRenderData)
    (snapshot: MovementSnapshot)
    (nodeTransformsPool: Dictionary<string, Matrix>)
    : ResolvedEntity[]
```

---

## Emitter Signatures (Implemented)

All emitters are **pure functions** that return arrays:

```fsharp
module EntityEmitter =
  /// Parallel over entities via Array.Parallel.collect
  let emit (getModelByAsset: string -> Model voption) (entities: ResolvedEntity[]) : MeshCommand[]

module ParticleEmitter =
  /// Parallel over effects via Array.Parallel.collect
  let emitBillboards (core: RenderCore) (data: ParticleRenderData) (effects: VisualEffect[]) : BillboardCommand[]
  let emitMeshes (core: RenderCore) (data: ParticleRenderData) (effects: VisualEffect[]) : MeshCommand[]

module TerrainEmitter =
  /// Parallel over tile indices via Array.Parallel.choose
  let emitLayer (core: RenderCore) (data: TerrainRenderData) (map: MapDefinition) (layer: MapLayer) (viewBounds) : TerrainCommand[]
  let emitForeground (core: RenderCore) (data: TerrainRenderData) (map: MapDefinition) (layers: MapLayer IndexList) (viewBounds) : TerrainCommand[]
```

---

## RenderMath Functions (Implemented)

All isometric logic centralized:

```fsharp
module RenderMath =
  // Coordinate conversion
  let inline LogicToRender (logicPos: Vector2) (altitude: float32) (ppu: Vector2) : Vector3
  let inline GetSquishFactor (ppu: Vector2) : float32
  let inline GetViewBounds (cameraPos: Vector2) (viewportWidth: float32) (viewportHeight: float32) (zoom: float32) : struct(l,r,t,b)

  // World matrix creation
  let CreateMeshWorldMatrix (renderPos: Vector3) (facing: float32) (scale: float32) (squishFactor: float32) : Matrix
  let CreateProjectileWorldMatrix (renderPos: Vector3) (facing: float32) (tilt: float32) (scale: float32) (squishFactor: float32) : Matrix
  let CreateMeshParticleWorldMatrix (renderPos: Vector3) (rotation: Quaternion) (baseScale: float32) (scaleAxis: Vector3) (pivot: Vector3) (squishFactor: float32) : Matrix
  let inline ApplyRigNodeTransform (pivot: Vector3) (offset: Vector3) (animation: Matrix) : Matrix

  // Tile coordinate conversion
  let TileGridToLogic (orientation) (staggerAxis) (staggerIndex) (mapWidth) (x) (y) (tileW) (tileH) : struct(float32 * float32)

  // Billboard helpers
  let inline GetBillboardVectors (view: Matrix) : struct(Vector3 * Vector3)
  let inline ScreenToLogic (screenPos: Vector2) (viewport: Viewport) (zoom: float32) (cameraPosition: Vector2) : Vector2
  let inline LogicToScreen (logicPos: Vector2) (viewport: Viewport) (zoom: float32) (cameraPosition: Vector2) : Vector2

  // Isometric correction (exposed for edge cases)
  let IsometricCorrectionMatrix : Matrix
```

---

## GC Optimization (Implemented)

- **Direct HashMap iteration** - no `toArray` allocations
- **Reused buffers** - `ResizeArray` for intermediate results
- **`[<TailCall>]`** on recursive functions for stack safety
- **`struct` tuples** in hot paths to avoid boxing
- **Pre-allocated result buffers** with estimated capacity

---

## Command Structs

Zero-allocation struct commands:

```fsharp
[<Struct>]
type MeshCommand = {
  Model: Model
  WorldMatrix: Matrix
}

[<Struct>]
type BillboardCommand = {
  Texture: Texture2D
  Position: Vector3
  Size: float32
  Color: Color
}

[<Struct>]
type TerrainCommand = {
  Texture: Texture2D
  Position: Vector3
  Size: Vector2
}
```

---

## Files Implemented

| File                           | Purpose                                        |
| ------------------------------ | ---------------------------------------------- |
| `Graphics/RenderMath.fs`       | All isometric math, coordinate conversion      |
| `Graphics/RenderCommands.fs`   | Struct command definitions                     |
| `Rendering/EmitterContext.fs`  | Split context types (RenderCore, \*RenderData) |
| `Rendering/PoseResolver.fs`    | Sequential rig resolution → ResolvedEntity[]   |
| `Rendering/EntityEmitter.fs`   | Parallel entity command emission               |
| `Rendering/ParticleEmitter.fs` | Parallel particle command emission             |
| `Rendering/TerrainEmitter.fs`  | Parallel terrain command emission              |

---

## Acceptance Criteria

- [x] All matrix math centralized in `RenderMath`
- [x] Emitters return arrays (parallelizable)
- [x] No `Matrix.Create*` calls in emitters
- [x] Split context design (no god objects)
- [x] GC-optimized with direct iteration and buffer reuse
- [ ] RenderOrchestrator wired up (Phase 5)
- [ ] Visual regression testing
- [ ] Memory profiler validation

---

## Out of Scope

- Custom shaders (pipeline architecture focus)
- UI system migration (remains separate)

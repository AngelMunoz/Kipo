# Plan: Render Pipeline Redesign

> [!IMPORTANT]
> This is a **breaking change** implementation. Existing render systems will be replaced.

---

## Phase 1: RenderMath Foundation [COMPLETE]

_Goal: Create the single source of isometric truth._

- [x] **Create `Pomo.Core/Graphics/RenderMath.fs`**

  - [x] `LogicToRender(logicPos, altitude, ppu) -> Vector3`
  - [x] `GetViewMatrix(logicPos, ppu) -> Matrix`
  - [x] `GetProjectionMatrix(viewport, zoom, ppu) -> Matrix`
  - [x] `ScreenToLogic(screenPos, camera) -> Vector2`
  - [x] `CreateMeshWorldMatrix(renderPos, facing, scale, squishFactor) -> Matrix`
  - [x] `CreateProjectileWorldMatrix(renderPos, facing, tilt, scale, squishFactor) -> Matrix`
  - [x] `CreateMeshParticleWorldMatrix(...)` for mesh particles
  - [x] `ApplyRigNodeTransform(pivot, offset, animation) -> Matrix`
  - [x] `GetBillboardVectors(viewMatrix) -> (right, up)`
  - [x] `GetViewBounds(cameraPos, viewportW, viewportH, zoom)` for culling
  - [x] `TileGridToLogic(orientation, stagger, ...)` for terrain tiles
  - [x] Centralize squish factor: `ppu.X / ppu.Y`
  - [x] Expose `IsometricCorrectionMatrix` (renamed from `correction`)

- [x] **Verify no Matrix.Create\* calls outside RenderMath**

---

## Phase 2: Command Queues & Struct Definitions [COMPLETE]

_Goal: Define the data structures for the command pattern._

- [x] **Create `Pomo.Core/Graphics/RenderCommands.fs`**

  - [x] `[<Struct>] MeshCommand = { Model, WorldMatrix }`
  - [x] `[<Struct>] BillboardCommand = { Texture, Position, Size, Color }`
  - [x] `[<Struct>] TerrainCommand = { Texture, Position, Size }`

- [x] **Create `Pomo.Core/Graphics/CommandQueue.fs`**
  - [x] `ICommandQueue<'T>` interface for abstraction
  - [x] ArrayPool-based implementation

---

## Phase 3: Split Context Design [COMPLETE]

_Goal: Create specialized render contexts instead of a god object._

- [x] **Create `Pomo.Core/Rendering/EmitterContext.fs`**
  - [x] `RenderCore` - shared minimal dependencies (PixelsPerUnit)
  - [x] `EntityRenderData` - model store, poses, projectiles, squish/scale
  - [x] `ParticleRenderData` - textures, models, entity positions
  - [x] `TerrainRenderData` - tile texture lookup

---

## Phase 4: Parallelizable Emitter Architecture [COMPLETE]

_Goal: Emitters return arrays, enabling Array.Parallel.\* usage._

### 4.1 Pre-Computed Poses

- [x] **Create `Pomo.Core/Rendering/PoseResolver.fs`**
  - [x] `[<Struct>] ResolvedRigNode = { ModelAsset, WorldMatrix }`
  - [x] `ResolvedEntity = { EntityId, Nodes: ResolvedRigNode[] }`
  - [x] `resolveAll(core, data, snapshot, nodeTransformsPool) -> ResolvedEntity[]`
  - [x] Sequential pass with shared `nodeTransformsPool`
  - [x] Direct HashMap iteration (no `toArray`)
  - [x] Reused `ResizeArray` buffers
  - [x] `[<TailCall>]` on recursive functions

### 4.2 Entity Emitter

- [x] **Refactor `Pomo.Core/Rendering/EntityEmitter.fs`**
  - [x] Takes `ResolvedEntity[]` (pre-computed by PoseResolver)
  - [x] Returns `MeshCommand[]`
  - [x] Uses `Array.Parallel.collect`
  - [x] Pure function, no shared state

### 4.3 Particle Emitter

- [x] **Refactor `Pomo.Core/Rendering/ParticleEmitter.fs`**
  - [x] `emitBillboards(core, data, effects) -> BillboardCommand[]`
  - [x] `emitMeshes(core, data, effects) -> MeshCommand[]`
  - [x] Uses `Array.Parallel.collect`
  - [x] Returns `Array.empty` for inactive effects

### 4.4 Terrain Emitter

- [x] **Refactor `Pomo.Core/Rendering/TerrainEmitter.fs`**
  - [x] `emitLayer(core, data, map, layer, viewBounds) -> TerrainCommand[]`
  - [x] `emitForeground(core, data, map, layers, viewBounds) -> TerrainCommand[]`
  - [x] Uses `Array.Parallel.choose`
  - [x] Takes pre-computed `viewBounds` from `RenderMath.GetViewBounds`
  - [x] Uses `RenderMath.TileGridToLogic` for tile positions

### 4.5 Orbital Emitter [REMOVED]

- [x] **Deleted `OrbitalEmitter.fs`**
  - Orbitals are `VisualEffect`s rendered by `ParticleEmitter`
  - No separate emitter needed

---

## Phase 5: RenderOrchestrator [COMPLETE]

_Goal: Single entry point that calls emitters and flushes to GPU._

- [x] **Create `Pomo.Core/Systems/RenderOrchestratorV2.fs`**

  - [x] Object expression factory returning `DrawableGameComponent`
  - [x] Create contexts per frame (RenderCore, EntityRenderData, etc.)
  - [x] Call `PoseResolver.resolveAll` (sequential)
  - [x] Call emitters, collect command arrays
  - [x] Flush to batch renderers with BlendMode grouping
  - [x] Viewport save/restore

- [x] **Placeholder Emitters**
  - [x] `UIEmitter.fs` - Targeting indicator (deferred)
  - [x] `LightEmitter.fs` - Dynamic lighting (deferred)

---

## Phase 6: Cleanup & Delete Legacy [COMPLETE]

_Goal: Remove old render code._

- [x] Delete `Pomo.Core/Systems/Render.fs`
- [x] Delete `Pomo.Core/Systems/TerrainRender.fs`
- [x] Update scene setup to use new orchestrator

---

## Phase 7: Verification [COMPLETE]

_Goal: Confirm correctness and performance._

- [x] **Visual Verification**

  - [x] Entities, particles, terrain render correctly
  - [x] Depth sorting works
  - [x] No visual regressions

- [x] **Performance Verification**

  - [x] Memory profiler: minimal GC pressure
  - [x] Frame time acceptable

- [x] **Code Audit**
  - [x] No `Matrix.Create*` outside `RenderMath.fs`
  - [x] No `squishFactor` outside `RenderMath.fs`

---

## Summary

| Phase | Status   | Description             |
| ----- | -------- | ----------------------- |
| 1     | COMPLETE | RenderMath foundation   |
| 2     | COMPLETE | Command structs         |
| 3     | COMPLETE | Split context design    |
| 4     | COMPLETE | Parallelizable emitters |
| 5     | COMPLETE | RenderOrchestrator      |
| 6     | PENDING  | Cleanup legacy          |
| 7     | PENDING  | Verification            |

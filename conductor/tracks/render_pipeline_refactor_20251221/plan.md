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

## Phase 5: RenderOrchestrator [PENDING]

_Goal: Single entry point that calls emitters and flushes to GPU._

- [ ] **Create `Pomo.Core/Systems/RenderOrchestrator.fs`**

  - [ ] Inherit `DrawableGameComponent`
  - [ ] Create contexts per frame (RenderCore, EntityRenderData, etc.)
  - [ ] Call `PoseResolver.resolveAll` (sequential)
  - [ ] Call emitters in parallel, collect command arrays
  - [ ] Concatenate and sort commands by depth
  - [ ] Flush to batch renderers

- [ ] **Refactor Batch Renderers**
  - [ ] Update to consume `Command[]` instead of queues
  - [ ] Or: populate queues from arrays then flush

---

## Phase 6: Cleanup & Delete Legacy [PENDING]

_Goal: Remove old render code._

- [ ] Delete `Pomo.Core/Systems/Render.fs`
- [ ] Delete `Pomo.Core/Systems/TerrainRender.fs`
- [ ] Update scene setup to use new orchestrator

---

## Phase 7: Verification [PENDING]

_Goal: Confirm correctness and performance._

- [ ] **Visual Verification**

  - [ ] Entities, particles, terrain render correctly
  - [ ] Depth sorting works
  - [ ] No visual regressions

- [ ] **Performance Verification**

  - [ ] Memory profiler: minimal GC pressure
  - [ ] Frame time acceptable

- [ ] **Code Audit**
  - [ ] No `Matrix.Create*` outside `RenderMath.fs`
  - [ ] No `squishFactor` outside `RenderMath.fs`

---

## Summary

| Phase | Status   | Description             |
| ----- | -------- | ----------------------- |
| 1     | COMPLETE | RenderMath foundation   |
| 2     | COMPLETE | Command structs         |
| 3     | COMPLETE | Split context design    |
| 4     | COMPLETE | Parallelizable emitters |
| 5     | PENDING  | RenderOrchestrator      |
| 6     | PENDING  | Cleanup legacy          |
| 7     | PENDING  | Verification            |

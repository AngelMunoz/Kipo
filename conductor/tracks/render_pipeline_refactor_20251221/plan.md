# Plan: Render Pipeline Redesign

> [!IMPORTANT]
> This is a **breaking change** implementation. Existing render systems will be replaced.

---

## Phase 1: RenderMath Foundation

_Goal: Create the single source of isometric truth._

- [x] **Create `Pomo.Core/Graphics/RenderMath.fs` (new)**

  - [x] Define `LogicToRender(logicPos, altitude, ppu) → Vector3`
  - [x] Define `GetViewMatrix(logicPos, ppu) → Matrix`
  - [x] Define `GetProjectionMatrix(viewport, zoom, ppu) → Matrix`
  - [x] Define `ScreenToLogic(screenPos, camera) → Vector2`
  - [x] Define `CreateMeshWorldMatrix(renderPos, facing, scale, squishFactor) → Matrix`
  - [x] Define `GetBillboardVectors(viewMatrix) → (right, up)`
  - [x] Centralize squish factor calculation: `squishFactor = ppu.X / ppu.Y`
  - [x] Move isometric correction matrix (`isoRot`, `topDownRot`, `correction`) here from old `RenderMath.fs`

- [x] **Refactor `CameraSystem.fs` to use `RenderMath`**

  - [x] Replace inline View matrix computation (lines 88-98) with `RenderMath.GetViewMatrix`
  - [x] Replace inline Projection computation (lines 102-109) with `RenderMath.GetProjectionMatrix`
  - [x] Replace `ScreenToWorld` logic with `RenderMath.ScreenToLogic`
  - [x] Verify: ScreenToWorld now consistent with render positions

- [x] **Verification: RenderMath Unit Tests**
  - [x] Test `LogicToRender` ↔ `ScreenToLogic` round-trip
  - [x] Test mesh world matrix produces expected depth ordering

---

## Phase 2: Command Queues & Struct Definitions

_Goal: Define the data structures for the command queue pattern._

- [x] **Create `Pomo.Core/Graphics/RenderCommands.fs`**

  - [x] Define `[<Struct>] MeshCommand = { Model, WorldMatrix }`
  - [x] Define `[<Struct>] BillboardCommand = { Texture, RenderPosition, Size, Color }`
  - [x] Define `[<Struct>] TerrainCommand = { Texture, RenderPosition, Size }`

- [x] **Create `Pomo.Core/Graphics/CommandQueue.fs`**
  - [x] Define `CommandQueue<'T when 'T : struct>` using `ArrayPool`
  - [x] Implement `add: 'T → CommandQueue<'T> → unit` (inline, byref)
  - [x] Implement `clear: CommandQueue<'T> → unit`
  - [x] Implement `create: unit → CommandQueue<'T>`
  - [x] Implement `iterate: (byref<'T> → unit) → CommandQueue<'T> → unit`
  - [x] Implement auto-shrink logic for low sustained usage

---

## Phase 3: Simulation Layer Refactor

_Goal: Separate simulation state from render concerns. Work in logic space only._

- [x] **Rename `Particles.ActiveEffect` → `VisualEffect`**

  - [x] Update all references in domain and systems
  - [x] Rename `world.VisualEffects` type accordingly

- [x] **Create `Pomo.Core/Simulation/ParticleSimulator.fs` (pure module)**

  - [x] Define `VisualEffectState` (simulation state container, uses arrays not ResizeArray)
  - [x] Define `[<Struct>] SimulatedParticle = { LocalOffset: Vector3, Velocity, Color, Size, Life }`
  - [x] Implement `update: dt → VisualEffectState → VisualEffectState` (pure function)
  - [x] Design for `Array.Parallel.map` compatibility (no shared mutable state during update)
  - [x] Move simulation logic from `ParticleSystem.fs` here
  - [x] Remove all isometric/matrix code from simulation

- [x] **Create `Pomo.Core/Simulation/OrbitalSimulator.fs` (pure module)**
  - [x] Define `SimulatedOrbital = { LogicPosition: Vector2, Altitude: float32, ... }`
  - [x] Implement `update: dt → OrbitalState → OrbitalState`
  - [x] Move orbital position calculation from `OrbitalSystem.fs` here
  - [x] Remove direct `Effect.Position.Value` mutation

---

## Phase 4: Emitter Functions

_Goal: Create stateless emitter functions that convert simulation state to commands._

- [ ] **Create `Pomo.Core/Rendering/EntityEmitter.fs`**

  - [ ] Implement `emit: MovementSnapshot → RenderMath → MeshQueue → unit`
  - [ ] Iterate entities, call `RenderMath.CreateMeshWorldMatrix`, push to queue
  - [ ] Handle projectile altitude via `LiveProjectiles` lookup

- [ ] **Create `Pomo.Core/Rendering/ParticleEmitter.fs`**

  - [ ] Implement `emitBillboards: VisualEffectState → RenderMath → BillboardQueue → unit`
  - [ ] Implement `emitMeshes: VisualEffectState → RenderMath → MeshQueue → unit`
  - [ ] Group by (Texture, BlendMode) for batch efficiency

- [ ] **Create `Pomo.Core/Rendering/OrbitalEmitter.fs`**

  - [ ] Implement `emit: OrbitalState → RenderMath → MeshQueue → unit`

- [ ] **Create `Pomo.Core/Rendering/TerrainEmitter.fs`**
  - [ ] Implement `emitBackground: Map → Camera → RenderMath → TerrainQueue → unit`
  - [ ] Implement `emitForeground: Map → Camera → RenderMath → TerrainQueue → unit`
  - [ ] Respect RenderGroup layering (< 2 = background, >= 2 = foreground)
  - [ ] Implement tile culling

---

## Phase 5: RenderOrchestrator

_Goal: Single entry point that owns queues, calls emitters, flushes batches._

- [ ] **Create `Pomo.Core/Systems/RenderOrchestrator.fs` (new)**

  - [ ] Inherit `DrawableGameComponent`
  - [ ] Own: `MeshQueue`, `BillboardQueue`, `TerrainQueue`
  - [ ] Own: `MeshBatch`, `BillboardBatch`, `QuadBatch`
  - [ ] Own: `VisualEffectState`, `OrbitalState` (simulation state)
  - [ ] Implement `Draw(gameTime)`:
    1. Update simulations (call pure functions)
    2. Clear queues
    3. For each camera:
       - `TerrainEmitter.emitBackground(...)`
       - `EntityEmitter.emit(...)`
       - `ParticleEmitter.emitBillboards(...)` + `emitMeshes(...)`
       - `OrbitalEmitter.emit(...)`
       - `TerrainEmitter.emitForeground(...)`
    4. Sort queues by depth (Y component)
    5. Flush batches

- [ ] **Refactor Batch Renderers**
  - [ ] Update `BillboardBatch` to consume `CommandQueue<BillboardCommand>`
  - [ ] Create `MeshBatch` to consume `CommandQueue<MeshCommand>`
  - [ ] Update `QuadBatch` to consume `CommandQueue<TerrainCommand>`

---

## Phase 6: Cleanup & Delete Legacy

_Goal: Remove old render code._

- [ ] **Delete legacy files**

  - [ ] Delete `Pomo.Core/Systems/Render.fs` (replaced by emitters + orchestrator)
  - [ ] Delete old `Pomo.Core/Systems/RenderOrchestrator.fs`
  - [ ] Delete `Pomo.Core/Systems/TerrainRender.fs` (replaced by `TerrainEmitter`)
  - [ ] Delete `ParticleSystem.fs` GameComponent (replaced by `ParticleSimulator` module)
  - [ ] Delete `OrbitalSystem.fs` GameComponent (replaced by `OrbitalSimulator` module)

- [ ] **Update `PomoGame.fs` / scene setup**
  - [ ] Register new `RenderOrchestrator` as the single drawable component
  - [ ] Remove old render service registrations

---

## Phase 7: Verification

_Goal: Confirm correctness and performance._

- [ ] **Visual Verification**

  - [ ] Entities render at correct positions
  - [ ] Particles (billboard + mesh) render correctly
  - [ ] Orbitals track entity facing
  - [ ] Terrain layers respect RenderGroup ordering
  - [ ] No depth sorting artifacts (z-fighting)

- [ ] **Picking Verification**

  - [ ] Click on entity → ScreenToWorld returns position matching entity's logic position

- [ ] **Performance Verification**

  - [ ] Memory profiler: zero GC pressure during steady-state gameplay
  - [ ] Frame time remains acceptable

- [ ] **Code Audit**
  - [ ] Grep for `Matrix.Create` outside `RenderMath.fs` → should find none
  - [ ] Grep for `squishFactor` outside `RenderMath.fs` → should find none

# Plan: Render Pipeline Redesign

> [!IMPORTANT]
> This is a **breaking change** implementation. Existing render systems will be replaced.

---

## Phase 1: RenderMath Foundation

_Goal: Create the single source of isometric truth._

- [ ] **Create `Pomo.Core/Graphics/RenderMath.fs` (new)**

  - [ ] Define `LogicToRender(logicPos, altitude, ppu) → Vector3`
  - [ ] Define `GetViewMatrix(logicPos, ppu) → Matrix`
  - [ ] Define `GetProjectionMatrix(viewport, zoom, ppu) → Matrix`
  - [ ] Define `ScreenToLogic(screenPos, camera) → Vector2`
  - [ ] Define `CreateMeshWorldMatrix(renderPos, facing, scale, squishFactor) → Matrix`
  - [ ] Define `GetBillboardVectors(viewMatrix) → (right, up)`
  - [ ] Centralize squish factor calculation: `squishFactor = ppu.X / ppu.Y`
  - [ ] Move isometric correction matrix (`isoRot`, `topDownRot`, `correction`) here from old `RenderMath.fs`

- [ ] **Refactor `CameraSystem.fs` to use `RenderMath`**

  - [ ] Replace inline View matrix computation (lines 88-98) with `RenderMath.GetViewMatrix`
  - [ ] Replace inline Projection computation (lines 102-109) with `RenderMath.GetProjectionMatrix`
  - [ ] Replace `ScreenToWorld` logic with `RenderMath.ScreenToLogic`
  - [ ] Verify: ScreenToWorld now consistent with render positions

- [ ] **Verification: RenderMath Unit Tests**
  - [ ] Test `LogicToRender` ↔ `ScreenToLogic` round-trip
  - [ ] Test mesh world matrix produces expected depth ordering

---

## Phase 2: Command Queues & Struct Definitions

_Goal: Define the data structures for the command queue pattern._

- [ ] **Create `Pomo.Core/Graphics/RenderCommands.fs`**

  - [ ] Define `[<Struct>] MeshCommand = { Model, WorldMatrix }`
  - [ ] Define `[<Struct>] BillboardCommand = { Texture, RenderPosition, Size, Color }`
  - [ ] Define `[<Struct>] TerrainCommand = { Texture, RenderPosition, Size }`

- [ ] **Create `Pomo.Core/Graphics/CommandQueue.fs`**
  - [ ] Define `CommandQueue<'T when 'T : struct>` using `ArrayPool`
  - [ ] Implement `add: 'T → CommandQueue<'T> → unit` (inline, byref)
  - [ ] Implement `clear: CommandQueue<'T> → unit`
  - [ ] Implement `create: unit → CommandQueue<'T>`
  - [ ] Implement `iterate: (byref<'T> → unit) → CommandQueue<'T> → unit`
  - [ ] Implement auto-shrink logic for low sustained usage

---

## Phase 3: Simulation Layer Refactor

_Goal: Separate simulation state from render concerns. Work in logic space only._

- [ ] **Rename `Particles.ActiveEffect` → `VisualEffect`**

  - [ ] Update all references in domain and systems
  - [ ] Rename `world.VisualEffects` type accordingly

- [ ] **Create `Pomo.Core/Simulation/ParticleSimulator.fs` (pure module)**

  - [ ] Define `VisualEffectState` (simulation state container, uses arrays not ResizeArray)
  - [ ] Define `[<Struct>] SimulatedParticle = { LocalOffset: Vector3, Velocity, Color, Size, Life }`
  - [ ] Implement `update: dt → VisualEffectState → VisualEffectState` (pure function)
  - [ ] Design for `Array.Parallel.map` compatibility (no shared mutable state during update)
  - [ ] Move simulation logic from `ParticleSystem.fs` here
  - [ ] Remove all isometric/matrix code from simulation

- [ ] **Create `Pomo.Core/Simulation/OrbitalSimulator.fs` (pure module)**
  - [ ] Define `SimulatedOrbital = { LogicPosition: Vector2, Altitude: float32, ... }`
  - [ ] Implement `update: dt → OrbitalState → OrbitalState`
  - [ ] Move orbital position calculation from `OrbitalSystem.fs` here
  - [ ] Remove direct `Effect.Position.Value` mutation

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

# Implementation Plan - Visual Debug Tools

> [!IMPORTANT]
> **Incremental Delivery:** Once Phase 0 (Foundation) and Phase 1 (UI) are complete, each subsequent phase is **independently shippable** as a separate PR. The building blocks (`Debug` module, `LineBatch`, `DebugWorld`) enable any debug subsystem to be added without depending on other subsystems.

---

## Phase 0: Architecture & Zero-Cost Foundation
- [ ] Task: Create `Debug` module in `Pomo.Core`
    - Implement inverted pattern: always-callable functions that are no-ops in RELEASE.
    - Define `#if DEBUG` / `#else` pattern for all debug functions.
    - Mutable `enabled` flag that starts as `false`.
    - Toggle function bound to F3 key.
- [ ] Task: Create `DebugWorld` state container
    - Boolean flags for each visualization subsystem.
    - Transient command buffer for temporary debug visuals.
    - Performance metrics storage (frame times, entity counts).
- [ ] Task: Implement `LineBatch` primitive renderer
    - Batch renderer for lines using `VertexPositionColor` in `Pomo.Core.Graphics`.
    - Helper functions to tessellate circles, boxes, cones into line segments.
    - Efficient single draw call per batch.
- [ ] Task: Conductor - User Manual Verification 'Phase 0: Foundation'

---

## Phase 1: Debug UI (Myra Panel)
- [ ] Task: Implement `DebugUISystem`
    - Myra `Window` with CheckBoxes for each debug flag.
    - Bind CheckBox events to `DebugWorld` toggle flags.
    - F3 key toggles panel visibility.
    - Only created in DEBUG builds via `#if DEBUG` in `CompositionRoot`.
- [ ] Task: Implement Performance Overlay
    - FPS counter rendered via SpriteBatch (not Myra).
    - Entity count display.
    - Render to main viewport only (not per-camera).
- [ ] Task: Conductor - User Manual Verification 'Phase 1: UI'

---

## Phase 2: Physics & Spatial Visualization
- [ ] Task: Implement `CollisionDebugEmitter`
    - Emit bounding circles for all live entities.
    - Emit map object polygons/polylines.
    - Emit spatial grid cell lines.
- [ ] Task: Implement `CollisionDebugSystem`
    - Check `DebugWorld.ShowCollisionBounds` flag.
    - Call emitter and render via `LineBatch`.
    - Transient MTV lines on collision events.
- [ ] Task: Register in `CompositionRoot` (DEBUG builds only)
- [ ] Task: Conductor - User Manual Verification 'Phase 2: Collision'

---

## Phase 3: AI & Navigation Visualization
- [ ] Task: Implement `AIDebugEmitter`
    - Emit path lines from `AIController` waypoints.
    - Emit FOV cones from perception config.
    - Emit target lines to current targets.
- [ ] Task: Implement `AIDebugSystem`
    - Check `DebugWorld.ShowAIPaths` and `ShowAIState` flags.
    - Render paths and FOV via `LineBatch`.
    - Render state labels via SpriteBatch text (per-camera).
- [ ] Task: Register in `CompositionRoot` (DEBUG builds only)
- [ ] Task: Conductor - User Manual Verification 'Phase 3: AI'

---

## Phase 4: Skills & Combat Visualization
- [ ] Task: Implement `SkillDebugEmitter`
    - Emit range circles for selected skills.
    - Emit AoE shapes during targeting (Circle, Cone, Line).
    - Emit hit detection highlights (transient).
- [ ] Task: Implement `SkillDebugSystem`
    - Check `DebugWorld.ShowSkillAoE` flag.
    - Read from `TargetingService` for active skill targeting.
    - Render shapes via `LineBatch`.
- [ ] Task: Register in `CompositionRoot` (DEBUG builds only)
- [ ] Task: Conductor - User Manual Verification 'Phase 4: Skills'

---

## Phase 5: Projectile & Effect Visualization
- [ ] Task: Implement `ProjectileDebugEmitter`
    - Emit trajectory lines from `LiveProjectiles`.
    - Emit collision bounds for projectiles.
    - Emit impact zone previews.
- [ ] Task: Implement `EffectDebugEmitter`
    - Emit effect duration bars/indicators.
    - Emit stat modifier text overlays.
- [ ] Task: Implement `ProjectileDebugSystem` and `EffectDebugSystem`
    - Check respective `DebugWorld` flags.
    - Render via `LineBatch` and SpriteBatch.
- [ ] Task: Conductor - User Manual Verification 'Phase 5: Projectiles & Effects'

---

## Phase 6: Camera & Particle Visualization
- [ ] Task: Implement `CameraDebugEmitter`
    - Emit camera viewport bounds.
    - Emit active zone margins.
- [ ] Task: Implement `ParticleDebugEmitter`
    - Emit emission direction vectors.
    - Emit spawn area shapes (distinct from skill AoE).
- [ ] Task: Implement `CameraDebugSystem` and `ParticleDebugSystem`
- [ ] Task: Conductor - User Manual Verification 'Phase 6: Camera & Particles'

---

## Phase 7: Integration & Verification
- [ ] Task: Final Integration
    - Verify all debug systems update and draw correctly.
    - Verify Z-ordering (debug overlay on top).
    - Verify multi-camera rendering (player debug per-camera, global to main).
- [ ] Task: RELEASE Build Verification
    - Build in RELEASE mode.
    - Verify zero debug code in output (no F3 response, no overhead).
- [ ] Task: Code Cleanup & Documentation
    - Ensure `Debug` module pattern is consistent.
    - Document usage in code comments.
- [ ] Task: Conductor - User Manual Verification 'Phase 7: Final Integration'

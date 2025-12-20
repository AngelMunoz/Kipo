# Implementation Plan - Seraphic Bombardment

## Phase 1: Domain & Data Modeling
- [x] Task: Create Orbital Domain
- [x] Task: Extend Skill Domain
- [x] Task: Enhance Particle Domain
- [x] Task: Implement Serialization
- [x] Task: Verification
- [x] Task: Conductor - User Manual Verification 'Domain & Data Modeling' (Protocol in workflow.md)

## Phase 2: Core Systems Implementation
- [ ] Task: Implement Orbital Logic
  - Create `OrbitalSystem.fs` (or equivalent logic module).
  - Implement the math for calculating orbital positions: `center + radius * (cos(angle), sin(angle))` with acceleration.
- [ ] Task: Update Combat System
  - Modify `Combat.fs` to handle the `ChargedProjectile` flow: Start Charge -> Update Orbitals -> Launch Projectile.
- [ ] Task: Implement Mesh Particle Rendering
  - Update `Pomo.Core/Systems/Render.fs` to support `RenderMode.Mesh`.
  - Implement `renderMeshParticles` using `drawModel`, handling position, rotation, scale, and color tinting.
- [ ] Task: Verify Particle Overrides for Orbitals
  - Check if `OrbitalConfig` needs to pass dynamic properties (Color, Size) to the particle system.
  - If so, ensure `OrbitalSystem` uses `EffectOverrides` when spawning/updating orbital particles, similar to how Skills override Area/Rotation.
- [ ] Task: Verification
  - Create a test to verify the state transition from "Charging" to "Launched".
- [ ] Task: Conductor - User Manual Verification 'Core Systems Implementation' (Protocol in workflow.md)

## Phase 3: Visuals & Content Integration
- [ ] Task: Prepare 3D Assets
  - Verify `Coni_A.obj` exists or use `Coin_A.obj`/`Cone` as placeholder.
  - Identify or add `Sphere` (for Dome) and `Cylinder` (for Column) models to `Models.json`.
- [ ] Task: Create Particle Effects
  - Define `JudgementCharge`, `OrbitalGlow` (using `Particles/jellyfish0-masks/3`), and `LightColumn` (using `Particles/jellyfish0-masks/8`) in `Particles.json`.
  - **Dome Effect:** Define a generic `LightDome` particle using `RenderMode: Mesh` (Sphere model).
  - **Column Effect:** Define `LightColumnMesh` using `RenderMode: Mesh` (Cylinder/Beam model) if the texture version is insufficient.
- [ ] Task: Define Skill Content
  - Add the "Seraphic Bombardment" entry to `Skills.json` with the new `ChargedProjectile` configuration.
- [ ] Task: Verification
  - Launch the game and manually verify the visual sequence: Charge -> Orbit -> Launch -> Impact -> **Dome/Column Visuals**.
- [ ] Task: Conductor - User Manual Verification 'Visuals & Content Integration' (Protocol in workflow.md)

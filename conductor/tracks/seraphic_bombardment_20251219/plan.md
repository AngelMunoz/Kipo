# Implementation Plan - Seraphic Bombardment

## Phase 1: Domain & Data Modeling
- [ ] Task: Extend Domain Types
  - Define `OrbitalConfig` and `ChargeConfig` in `Domain/Skill.fs`.
  - Extend `Delivery` DU with `ChargedProjectile`.
  - Update `Domain/Combat.fs` or relevant files to support tracking active orbitals.
- [ ] Task: Verification
  - Create a unit test to verify that the new JSON structure for `ChargedProjectile` can be deserialized correctly.
- [ ] Task: Conductor - User Manual Verification 'Domain & Data Modeling' (Protocol in workflow.md)

## Phase 2: Core Systems Implementation
- [ ] Task: Implement Orbital Logic
  - Create `OrbitalSystem.fs` (or equivalent logic module).
  - Implement the math for calculating orbital positions: `center + radius * (cos(angle), sin(angle))` with acceleration.
- [ ] Task: Update Combat System
  - Modify `Combat.fs` to handle the `ChargedProjectile` flow: Start Charge -> Update Orbitals -> Launch Projectile.
- [ ] Task: Verification
  - Create a test to verify the state transition from "Charging" to "Launched".
- [ ] Task: Conductor - User Manual Verification 'Core Systems Implementation' (Protocol in workflow.md)

## Phase 3: Visuals & Content Integration
- [ ] Task: Create Particle Effects
  - Define `JudgementCharge`, `OrbitalGlow`, and `LightColumn` in `Particles.json`.
- [ ] Task: Define Skill Content
  - Add the "Seraphic Bombardment" entry to `Skills.json` with the new `ChargedProjectile` configuration.
- [ ] Task: Verification
  - Launch the game and manually verify the visual sequence: Charge -> Orbit -> Launch -> Impact.
- [ ] Task: Conductor - User Manual Verification 'Visuals & Content Integration' (Protocol in workflow.md)

# Implementation Plan - Seraphic Bombardment

## Phase 1: Domain & Data Modeling

- [x] Task: Create Orbital Domain (`Orbital.fs` with `OrbitalConfig`, `OrbitalCenter`, `ActiveOrbital`)
- [x] Task: Extend Skill Domain (`ChargeConfig`, `ChargePhase` field on `ActiveSkill`)
- [x] Task: Enhance Particle Domain
- [x] Task: Implement Serialization (decoders for OrbitalConfig, ChargeConfig)
- [x] Task: Verification
- [x] Task: Conductor - User Manual Verification 'Domain & Data Modeling' (Protocol in workflow.md)

## Phase 2: Core Systems Implementation

- [x] Task: Implement Orbital Logic (`OrbitalSystem.fs`)
- [x] Task: Update Combat System (`handleAbilityIntent` for `ChargePhase`, `handleChargeCompleted`)
- [x] Task: Implement Mesh Particle Rendering
- [x] Task: Verify Particle Overrides for Orbitals
- [x] Task: Verification
- [x] Task: Conductor - User Manual Verification 'Core Systems Implementation' (Protocol in workflow.md)

## Phase 3: Visuals & Content Integration

- [x] Task: Prepare 3D Assets
- [x] Task: Create Particle Effects
- [x] Task: Define Skill Content (Skills.json ID 25)
- [x] Task: Verification
- [x] Task: Conductor - User Manual Verification 'Visuals & Content Integration' (Protocol in workflow.md)

## Phase 4: Refactoring & Optimization (Post-Implementation Review)

- [x] Task: Decouple `ChargePhase` from `Delivery` DU (orthogonalize)
- [x] Task: Add `OrbitalCenter` DU for generic orbital positioning
- [x] Task: Add `ChargeCompleted` lifecycle event
- [x] Task: Consolidate `ChargeSystem` into `OrbitalSystem`
- [x] Task: Add entity cleanup for `ActiveCharges`/`ActiveOrbitals` in `removeEntity`
- [x] Task: Fix `Seq.groupBy` performance with imperative dictionary
- [x] Task: Add `handleChargeCompleted` handler in Combat
- [x] Task: Delete redundant `ChargeSystem.fs`
- [x] Task: Final verification build

## Files Modified (Final State)

### Domain

- `Orbital.fs` - Added `OrbitalCenter` DU, updated `ActiveOrbital.Center`
- `Skill.fs` - Removed `ChargedProjectile`, added `ChargePhase` field to `ActiveSkill`
- `Events.fs` - Added `ChargeCompleted` struct and lifecycle event case

### Content

- `Skills.json` - Skill 25 uses top-level `Charge` field with `Projectile` delivery

### Systems

- `OrbitalSystem.fs` - Handles orbital visuals AND charge expiry, publishes `ChargeCompleted`
- `Combat.fs` - `handleAbilityIntent` checks `ChargePhase`, `handleChargeCompleted` spawns projectiles
- `State.fs` - `removeEntity` cleans up `ActiveOrbitals` and `ActiveCharges`

### Deleted

- `ChargeSystem.fs` - Logic merged into `OrbitalSystem`

### Config

- `Pomo.Core.fsproj` - Removed ChargeSystem.fs entry
- `CompositionRoot.fs` - Removed ChargeSystem registration

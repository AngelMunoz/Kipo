# Feature Specification: Seraphic Bombardment Skill

## 1. Overview

Implement the "Seraphic Bombardment" skill, a complex offensive ability where the caster summons orbiting spheres of light that charge up and then bombard enemies in a target area. This feature requires extending the core combat and rendering systems to support orbital visual effects, charged projectiles, and vertical descent mechanics.

## 2. Core Requirements

### 2.1 Domain Model Extensions

- **Orbital Domain:** New `Domain/Orbital.fs` module to define generic orbital systems, located before `Skills.fs`.
- **Orbital Configuration:** `OrbitalConfig` struct to define orbital behavior (radius, speed, visuals).
- **Orbital Center:** `OrbitalCenter` DU to support both entity-centered and position-centered orbitals.
- **Charge Phase:** Optional `ChargePhase: ChargeConfig voption` field on `ActiveSkill` to support skills with a charge-up phase. This is _orthogonal_ to the `Delivery` type, allowing any delivery (Instant, Projectile) to have an optional charge.
- **Projectile Logic:** Logic to handle the transition from "orbiting" to "launched" state via `ChargeCompleted` lifecycle event.
- **Mesh Particles:** Extend `ParticleSystem` to support rendering 3D Models (`RenderMode: Mesh`) for effects like Domes and Columns.

### 2.2 Visual Effects

- **Charge Phase:** Particle effects for the gathering energy.
- **Orbital Visuals:** Spheres (models) that rotate around the caster, accelerating over time.
- **Impact:** Vertical light column effect upon impact.
- **Explosion:** A "Dome of Light" (expanding hemisphere/sphere) symbolizing the explosion upon impact, implemented via Mesh Particles.

### 2.3 Game Content

- **Skill Definition:** JSON configuration for "Seraphic Bombardment" (ID 25) in `Skills.json`.
- **Assets:**
  - **Model:** `Coin_A.obj` (Placeholder for both Orbitals and Projectiles).
  - **Orbital Particles:** Texture `Particles/jellyfish0-masks/3` (from `3.png`).
  - **Projectile Particles:** Texture `Particles/jellyfish0-masks/8` (from `8.png`).
  - **Note:** Ensure Orbitals and Projectiles can define their own particle effects, potentially using an override system if dynamic properties (like color/size) need to come from the Skill/Orbital config rather than the static Particle definition.

## 3. Technical Design

### 3.1 Data Structures

```fsharp
// Domain/Orbital.fs

open Microsoft.Xna.Framework

[<Struct>]
type OrbitalConfig = {
  Count: int
  /// Base radius of the orbit
  Radius: float32
  /// The local center of the orbit relative to the entity (e.g., Y=1.0 for chest height)
  CenterOffset: Vector3
  /// The axis around which the orbitals rotate (allows for tilted orbits, e.g., Vector3.Up for horizontal)
  RotationAxis: Vector3
  /// Scaling factors for the orbit shape relative to the rotation plane.
  /// Use (1.0, 1.0) for a perfect circle, other values for ellipses.
  PathScale: Vector2
  StartSpeed: float32
  EndSpeed: float32
  Duration: float32
  Visual: VisualManifest
}

[<Struct>]
type OrbitalCenter =
  | EntityCenter of entityId: Guid<EntityId>
  | PositionCenter of position: Vector2

[<Struct>]
type ActiveOrbital = {
  Center: OrbitalCenter
  Config: OrbitalConfig
  StartTime: float32
}

// Domain/Skill.fs extensions
[<Struct>]
type ChargeConfig = {
  Duration: float32
  ChargeVisuals: VisualManifest
  Orbitals: OrbitalConfig voption
}

// IMPORTANT: ChargePhase is orthogonal to Delivery, not coupled to it
[<Struct>]
type Delivery =
  | Instant
  | Projectile of projectile: ProjectileInfo

[<Struct>]
type ActiveSkill = {
  // ... other fields ...
  Delivery: Delivery
  ChargePhase: ChargeConfig voption  // Optional charge phase for ANY delivery
  // ... other fields ...
}
```

### 3.2 Lifecycle Events

```fsharp
// Domain/Events.fs

[<Struct>]
type ChargeCompleted = {
  CasterId: Guid<EntityId>
  SkillId: int<SkillId>
  Target: SkillTarget
}

type LifecycleEvent =
  | EntityDied of died: SystemCommunications.EntityDied
  | ProjectileImpacted of impact: SystemCommunications.ProjectileImpacted
  | ChargeCompleted of charge: SystemCommunications.ChargeCompleted
```

### 3.3 Systems

- **OrbitalSystem:** Manages both orbital visuals AND charge expiry logic. When a charge expires, publishes `ChargeCompleted` event and cleans up the orbital/charge state.
- **Combat System:**
  - `handleAbilityIntent`: Checks `skill.ChargePhase` - if present, creates `ActiveOrbital` and `ActiveCharge`; if absent, executes delivery immediately.
  - `handleChargeCompleted`: Subscribes to `ChargeCompleted` event and spawns projectiles or executes instant delivery.

### 3.4 State Cleanup

When an entity dies (in `State.fs removeEntity`), both `ActiveOrbitals` and `ActiveCharges` are automatically cleaned up to prevent orphaned state.

## 4. Acceptance Criteria

- [x] Skill "Seraphic Bombardment" exists in `Skills.json`.
- [x] Casting the skill triggers a charge-up phase with visual feedback.
- [x] During charge, 5 spheres appear and orbit the caster, speeding up.
- [x] After charge, spheres launch toward the target area.
- [x] Projectiles fall from the sky (descending logic) and create a light column on impact.
- [x] Damage is calculated correctly based on the formula.
- [x] Killing caster mid-charge cleans up orbitals and charges properly.

## 5. Coordinate System Notes

- **World Orientation:** The game world is isometric, but the 3D camera is positioned top-down (looking down the Y-axis).
- **Axis Mapping:**
  - Logic `X` (East-West) -> 3D `X`.
  - Logic `Y` (North-South) -> 3D `Z`.
  - Altitude (Up-Down) -> 3D `Y`.
- **Orbital Implications:** A horizontal orbit should occur in the **X-Z plane** (3D coordinates). Due to the isometric projection, a perfectly circular orbit in 3D space will appear as an ellipse on screen. The `OrbitalSystem` should use 3D `Vector3` math, and the `Render` system will handle the projection.
- **Descending Logic:** Projectiles using `Descending` logic move along the 3D `Y` axis toward `Y=0`.

## 6. Design Decisions & Lessons Learned

### 6.1 Decoupling Charge from Delivery

**Problem:** Initial design used `ChargedProjectile of ChargeConfig * ProjectileInfo` in the `Delivery` DU, tightly coupling charge to projectile delivery.

**Solution:** Made `ChargePhase` an optional field on `ActiveSkill`, orthogonal to `Delivery`. This allows:

- Charge → Projectile (Seraphic Bombardment)
- Charge → Instant (future: channeled AoE)
- No Charge → Projectile (normal projectile skills)
- Charge → Just particles (future: cosmetic channels)

### 6.2 System Consolidation

Merged `ChargeSystem` into `OrbitalSystem` to follow the existing `ProjectileSystem` pattern where:

- System manages both state update AND lifecycle event publishing
- Combat system subscribes to lifecycle events and handles consequences

### 6.3 Performance Optimization

Replaced `Seq.groupBy` with imperative dictionary in OrbitalSystem to avoid allocations during the update loop.

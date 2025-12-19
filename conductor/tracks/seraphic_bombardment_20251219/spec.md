# Feature Specification: Seraphic Bombardment Skill

## 1. Overview
Implement the "Seraphic Bombardment" skill, a complex offensive ability where the caster summons orbiting spheres of light that charge up and then bombard enemies in a target area. This feature requires extending the core combat and rendering systems to support orbital visual effects, charged projectiles, and vertical descent mechanics.

## 2. Core Requirements

### 2.1 Domain Model Extensions
- **Orbital Domain:** New `Domain/Orbital.fs` module to define generic orbital systems, located before `Skills.fs`.
- **Orbital Configuration:** `OrbitalConfig` struct to define orbital behavior (radius, speed, visuals).
- **Delivery Types:** New `ChargedProjectile` case in the `Delivery` Discriminated Union (DU) to support skills with a charge-up phase containing visuals.
- **Projectile Logic:** Logic to handle the transition from "orbiting" to "launched" state.

### 2.2 Visual Effects
- **Charge Phase:** Particle effects for the gathering energy.
- **Orbital Visuals:** Spheres (models) that rotate around the caster, accelerating over time.
- **Impact:** Vertical light column effect upon impact.

### 2.3 Game Content
- **Skill Definition:** JSON configuration for "Seraphic Bombardment" (ID 20) in `Skills.json`.
- **Assets:** Placeholder or procedural assets for the light spheres and column particles.

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

// Domain/Skill.fs extensions

type ChargeConfig = {
  Duration: float32
  ChargeVisuals: VisualManifest
  Orbitals: OrbitalConfig voption
}

type Delivery =
  | ...
  | ChargedProjectile of charge: ChargeConfig * projectile: ProjectileInfo
```

### 3.2 Systems
- **OrbitalSystem:** A new system (or extension to `Combat`/`ParticleSystem`) responsible for updating the position of active orbitals based on time and acceleration parameters.
- **Combat System:** Updated to handle the `ChargedProjectile` delivery type, initiating the charge phase before launching the actual projectiles.

## 4. Acceptance Criteria
- [ ] Skill "Seraphic Bombardment" exists in `Skills.json`.
- [ ] Casting the skill triggers a charge-up phase with visual feedback.
- [ ] During charge, 5 spheres appear and orbit the caster, speeding up.
- [ ] After charge, spheres launch toward the target area.
- [ ] Projectiles fall from the sky (descending logic) and create a light column on impact.
- [ ] Damage is calculated correctly based on the formula.

## 5. Coordinate System Notes
- **World Orientation:** The game world is isometric, but the 3D camera is positioned top-down (looking down the Y-axis).
- **Axis Mapping:**
  - Logic `X` (East-West) -> 3D `X`.
  - Logic `Y` (North-South) -> 3D `Z`.
  - Altitude (Up-Down) -> 3D `Y`.
- **Orbital Implications:** A horizontal orbit should occur in the **X-Z plane** (3D coordinates). Due to the isometric projection, a perfectly circular orbit in 3D space will appear as an ellipse on screen. The `OrbitalSystem` should use 3D `Vector3` math, and the `Render` system will handle the projection.
- **Descending Logic:** Projectiles using `Descending` logic move along the 3D `Y` axis toward `Y=0`.

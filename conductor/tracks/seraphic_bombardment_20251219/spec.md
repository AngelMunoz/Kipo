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

[<Struct>]
type OrbitalConfig = {
  Count: int
  OrbitRadius: float32
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

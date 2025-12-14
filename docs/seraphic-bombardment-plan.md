# Seraphic Bombardment - Complete Implementation Analysis

## Skill Vision

Based on the reference images:

1. **Cast Start** - Caster raises arm, magic circle appears
2. **Sphere Summoning** - 5-8 glowing spheres materialize behind/around caster
3. **Rotation Charge** - Spheres orbit caster, accelerating from slow → fast
4. **Launch** - Spheres streak outward to random target positions
5. **Column Impact** - Each sphere impacts, creating a vertical light column dealing AOE damage

---

## Current System Capabilities (Verified from Codebase)

### ✅ ActiveSkill Fields (Domain/Skill.fs:189-215)

```fsharp
type ActiveSkill = {
  CastingTime: TimeSpan voption      // ✅ Casting delay
  CastVisuals: VisualManifest        // ✅ Can spawn VFX during cast
  ImpactVisuals: VisualManifest      // ✅ VFX at impact
  Area: SkillArea                    // ✅ MultiPoint area exists
  Delivery: Delivery                 // ✅ Projectile with variations
  ...
}
```

### ✅ SkillArea.MultiPoint (Domain/Skill.fs:167)

```fsharp
| MultiPoint of radius: float32 * maxPointTargets: int
```

Spawns multiple projectiles to random points within radius.

### ✅ Projectile Variations (Domain/Projectile.fs:18-22)

```fsharp
type ExtraVariations =
  | Chained of jumpsLeft: int * maxRange: float32
  | Bouncing of bouncesLeft: int
  | Descending of currentAltitude: float32 * fallSpeed: float32  // ✅ Fall from sky
```

### ✅ VisualManifest (Domain/Core.fs:169-175)

```fsharp
type VisualManifest = {
  ModelId: string voption       // 3D model
  VfxId: string voption         // Particle effect
  AnimationId: string voption   // Animation clip
  AttachmentPoint: string voption
}
```

---

## What's MISSING for Full Seraphic Bombardment

| Feature | Description | Why It's Needed |
|---------|-------------|-----------------|
| **Orbital Visuals** | Objects that orbit an entity over time | Spheres rotating around caster during charge |
| **Accelerating Rotation** | Animation speed ramp (slow → fast) | Charge-up visual escalation |
| **Delayed Launch** | Projectiles spawn after charge, not immediately | Spheres exist during cast, then launch |
| **Column Particle** | Vertical beam/pillar particle effect | Light eruption at impact |

---

## Required New Systems

### 1. Orbital Effect System

**Purpose:** Visual objects that orbit an entity during casting

**Domain Type:**
```fsharp
type OrbitalConfig = {
  Count: int                    // Number of orbitals (5)
  OrbitRadius: float32          // Distance from center
  StartSpeed: float32           // Initial rotation speed
  EndSpeed: float32             // Final rotation speed (acceleration)
  Duration: float32             // How long before launch
  Visual: VisualManifest        // What each orbital looks like
}
```

**Runtime State:**
```fsharp
type ActiveOrbital = {
  EntityId: Guid<EntityId>      // Owner entity
  Orbitals: OrbitalInstance[]   // Individual sphere states
  Timer: float32 ref            // Elapsed time
  Config: OrbitalConfig         // Config reference
}
```

### 2. Extended Delivery Type

Add a new delivery type that combines cast phase with projectile launch:

```fsharp
type Delivery =
  | Instant
  | Projectile of projectile: ProjectileInfo
  | ChargedProjectile of charge: ChargeConfig * projectile: ProjectileInfo  // NEW
```

```fsharp
type ChargeConfig = {
  Duration: float32             // Charge time (same as CastingTime)
  ChargeVisuals: VisualManifest // Particle burst during charge
  Orbitals: OrbitalConfig voption // Optional orbiting spheres
}
```

### 3. Column/Pillar Particle Effect

Use existing particle system with:
- **Cone shape** pointed upward (90° rotation)
- **EdgeOnly emission mode** for hollow column
- **High vertical speed** for upward streak

---

## Final Skill JSON Definition

```json
{
  "Kind": "Active",
  "Id": 20,
  "Name": "Seraphic Bombardment",
  "Description": "Summons spheres of divine light that orbit the caster before raining down upon enemies.",
  "Intent": "Offensive",
  "DamageSource": "Magical",
  "Cost": { "Type": "MP", "Amount": 35 },
  "Cooldown": 12.0,
  "CastingTime": 2.5,
  "Targeting": "TargetPosition",
  "Range": [16, 20],
  "Area": {
    "Type": "MultiPoint",
    "Radius": 160.0,
    "Count": 5
  },
  "Delivery": {
    "Type": "ChargedProjectile",
    "Charge": {
      "Duration": 2.5,
      "ChargeVisuals": { "Vfx": "JudgementCharge" },
      "Orbitals": {
        "Count": 5,
        "OrbitRadius": 32.0,
        "StartSpeed": 0.5,
        "EndSpeed": 4.0,
        "Visual": { "Model": "LightSphere", "Vfx": "OrbitalGlow" }
      }
    },
    "Speed": 250.0,
    "CollisionMode": "IgnoreTerrain",
    "Kind": {
      "Type": "Descending",
      "StartAltitude": 100.0,
      "FallSpeed": 300.0
    }
  },
  "ImpactVisuals": { "Vfx": "LightColumn" },
  "ElementFormula": {
    "Element": "Light",
    "Formula": "(MA * 2.0) + (LightA * 2500)"
  },
  "Origin": "Caster",
  "Effects": []
}
```

---

## Implementation Checklist

### Phase 1: Domain Types
- [ ] Add `OrbitalConfig` to Domain/Skill.fs
- [ ] Add `ChargeConfig` to Domain/Skill.fs
- [ ] Add `ChargedProjectile` case to `Delivery` DU
- [ ] Add JSON decoders for new types

### Phase 2: Orbital System
- [ ] Create `OrbitalSystem.fs`
- [ ] Track active orbitals in World state
- [ ] Render orbitals as positioned models
- [ ] Update orbital positions each frame (circular motion + acceleration)

### Phase 3: Skill Execution Integration
- [ ] Modify `Combat.fs` to handle `ChargedProjectile`
- [ ] Spawn orbitals when skill cast starts
- [ ] Convert orbitals → projectiles when cast completes
- [ ] Remove orbitals from world state

### Phase 4: Visual Effects
- [ ] Create `JudgementCharge` particle effect
- [ ] Create `OrbitalGlow` particle effect (attach to orbital models)
- [ ] Create `LightColumn` impact particle effect

### Phase 5: Assets
- [ ] `LightSphere` 3D model or use existing sphere primitive
- [ ] Particle textures for glow effects

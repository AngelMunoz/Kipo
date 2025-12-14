# Skills Configuration Guide

This guide explains how to create and customize abilities in `Skills.json`.

---

## What is a Skill?

A skill is an action a character can perform - casting a fireball, swinging a sword, or buffing an ally. Each skill defines:
- **Who can be targeted** (self, enemies, ground)
- **How it reaches the target** (instant, projectile)
- **What it does** (damage, healing, status effects)
- **How it looks** (visual effects on impact)

---

## Basic Setup

Every skill needs these essential properties:

```json
{
  "1": {
    "Kind": "Active",
    "Id": 1,
    "Name": "Fireball",
    "Description": "Hurls a fiery ball that explodes on impact.",
    "Intent": "Offensive",
    "DamageSource": "Magical"
  }
}
```

- **Name**: What players see in the UI
- **Description**: Tooltip text (use `\n` for new lines)
- **Intent**: `Offensive` (hurts enemies) or `Supportive` (helps allies)
- **DamageSource**: `Physical` (affected by armor) or `Magical` (affected by magic resistance)

---

## Casting Costs

What does it cost to use this ability?

```json
"Cost": { "Type": "MP", "Amount": 20 },
"Cooldown": 3.0,
"CastingTime": 0.5
```

- **Cost**: Resource spent (usually `MP` for mana, or `HP` for health)
- **Cooldown**: Seconds before the ability can be used again
- **CastingTime**: Channel time before the ability fires (omit for instant cast)

---

## Targeting Modes

How does the player aim this ability?

| Mode | Player Action | Good For |
|------|---------------|----------|
| `Self` | Just press the button | Buffs, shields, heals |
| `TargetEntity` | Click on a character | Single-target attacks, debuffs |
| `TargetPosition` | Click on the ground | Area attacks, summons |
| `TargetDirection` | Aim with mouse | Cones, lines, breath attacks |

```json
"Targeting": "TargetEntity",
"Range": [16, 10]
```

**Range** controls how far the ability reaches. The two numbers multiply together (16 × 10 = 160 pixels).

---

## Area of Effect

How big is the impact zone?

### Single Target
```json
"Area": "Point"
```
Only hits the targeted enemy.

### Circle (Explosion)
```json
"Area": { "Type": "Circle", "Radius": 68.0, "MaxTargets": 5 }
```
Hits everyone within a circular area.

### Cone (Breath/Fan)
```json
"Area": { "Type": "Cone", "Angle": 45.0, "Length": 208.0, "MaxTargets": 10 }
```
Hits everyone in a wedge shape spreading outward. Great for breath attacks or shotgun-style abilities.

### Line (Beam/Pierce)
```json
"Area": { "Type": "Line", "Width": 32.0, "Length": 304.0, "MaxTargets": 10 }
```
Hits everyone in a straight line. Perfect for laser beams or piercing arrows.

---

## Delivery: How It Reaches the Target

### Instant
```json
"Delivery": { "Type": "Instant" }
```
Damage happens immediately - like a melee swing or explosion centered on the target.

### Projectile
```json
"Delivery": {
  "Type": "Projectile",
  "Speed": 100.0,
  "CollisionMode": "IgnoreTerrain",
  "Visuals": { "Vfx": "FireballProjectile" }
}
```

A visible object flies toward the target:
- **Speed**: How fast it travels (higher = harder to dodge)
- **CollisionMode**:
  - `IgnoreTerrain` - Passes through walls (magic bolts)
  - `BlockedByTerrain` - Stops at walls (thrown knives)
- **Visuals.Vfx**: Particle effect trailing the projectile

### Special Projectile Variations

**Falling from Sky** (meteor, boulder):
```json
"Kind": { "Type": "Descending", "StartAltitude": 200.0, "FallSpeed": 300.0 }
```

**Bouncing Between Targets** (chain lightning):
```json
"Kind": { "Type": "Chained", "JumpsLeft": 4, "MaxRange": 80.0 }
```

---

## Damage Formulas

How much damage does it deal?

### Simple Physical Damage
```json
"Formula": "AP * 1.5"
```
Uses the caster's Attack Power, multiplied by 1.5.

### Elemental Damage
```json
"ElementFormula": {
  "Element": "Fire",
  "Formula": "(MA * 1.0) + (FireA * 1500)"
}
```
Uses Magic Attack plus the caster's Fire affinity. Elemental damage is reduced by the target's matching resistance.

**Common formula variables**:
- `AP` = Attack Power (physical)
- `MA` = Magic Attack
- `FireA`, `WaterA`, `DarkA`, etc. = Element affinities

---

## Visual Effects

What does it look like when it hits?

```json
"ImpactVisuals": { "Vfx": "FireballImpact" }
```

References a particle effect from `Particles.json`. The particles automatically shape themselves to match the skill's area (circles, cones, lines).

---

## Status Effects

What happens after the initial hit?

```json
"Effects": [
  {
    "Name": "Burning",
    "Kind": "DamageOverTime",
    "Duration": { "Type": "Loop", "Interval": 1.0, "Duration": 8.0 },
    "Visuals": { "Vfx": "BurningEffect" },
    "Modifiers": [
      { "Type": "AbilityDamageMod", "AbilityDamageValue": "MA * 0.5", "Element": "Fire" }
    ]
  }
]
```

### Effect Types
| Type | What It Does |
|------|--------------|
| `Buff` | Makes the target stronger |
| `Debuff` | Makes the target weaker |
| `DamageOverTime` | Deals damage every few seconds (poison, burning) |
| `ResourceOverTime` | Restores health/mana over time |
| `Stun` | Target can't move or act |
| `Silence` | Target can't cast spells |

### Duration Options
- **Instant**: Happens once and done
- **Timed**: Lasts for X seconds, then expires
- **Loop**: Triggers every X seconds for Y total seconds

### What Happens on Reapply?
- `NoStack` - Ignores if already applied
- `RefreshDuration` - Resets the timer
- `AddStack` - Stacks up for stronger effect

---

## Quick Reference Examples

### Basic Melee Attack
```json
{
  "Name": "Melee Attack",
  "Targeting": "TargetEntity",
  "Range": [16, 2],
  "Area": "Point",
  "Delivery": { "Type": "Instant" },
  "Formula": "AP * 1.0"
}
```

### Fireball with DoT
```json
{
  "Name": "Fireball",
  "Targeting": "TargetEntity",
  "Delivery": { "Type": "Projectile", "Speed": 100.0 },
  "ImpactVisuals": { "Vfx": "FireballImpact" },
  "ElementFormula": { "Element": "Fire", "Formula": "MA * 1.0" },
  "Effects": [
    { "Name": "Burning", "Kind": "DamageOverTime", "Duration": { "Type": "Loop", "Interval": 1.0, "Duration": 5.0 } }
  ]
}
```

### Cone Breath Attack
```json
{
  "Name": "Dragon's Breath",
  "Targeting": "TargetDirection",
  "Area": { "Type": "Cone", "Angle": 45.0, "Length": 208.0 },
  "Delivery": { "Type": "Instant" },
  "ImpactVisuals": { "Vfx": "FireBreath" }
}
```

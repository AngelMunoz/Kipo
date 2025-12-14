# Particle Configuration Reference

This guide explains how to configure `Particles.json` to create visual effects. It connects the JSON properties to their artistic impact and code behavior.

> [!IMPORTANT]
> **Isometric Rendering Note**: Particles use the **Y-axis for altitude** in 3D space. The camera uses an orthographic projection looking down at an isometric angle. Particles are rendered as **camera-facing billboards** (quads that always face the camera) using the camera's right and up vectors from `BillboardBatch.fs`.

---

## Emitter Properties

These control _how_ and _where_ particles are born.

### `Name` (String)

**Code Usage**: `ParticleStore` uses this ID to look up configurations. Multiple emitters can share the same name (they all spawn together).
**Artistic**: Use descriptive names like `FireballProjectile` or `IceExplosion`.

### `Texture` (String)

**Code Usage**: Path relative to Content root. Loaded as a `Texture2D`. Rendered as billboards that always face the camera.
**Artistic**:

- **Soft/Blurry** (e.g. `jellyfish0-masks/15`): Good for gas, fire, magic auras. Blends well.
- **Sharp/Defined** (e.g. `jellyfish0-masks/40`): Good for debris, shrapnel, sparks.

### `BlendMode` (String: `AlphaBlend` | `Additive`)

**Code Usage**: Sets `GraphicsDevice.BlendState` in `Render.fs`.

- `AlphaBlend`: Standard transparency. Dark colors block light behind them.
  - **Artistic**: Creating opaque clouds or "dirty" smoke.
- `Additive`: Adds color to the background. Overlapping particles become brighter.
  - **Artistic**: Makes things look **HOT** or **GLOWING**. Black is invisible.

### `SimulationSpace` (String: `World` | `Local`)

**Code Usage** (`ParticleSystem.fs` line 318-322):

- `World`: Particle spawns at `worldPos + localOffsetRotated + spawnOffset`. Position is absolute.
- `Local`: Particle spawns at `localOffsetRotated + spawnOffset`. During rendering, position is `ParticlePos + EffectPos`.

**Artistic**:

- **Trail / Smoke**: Use `World`. If a rocket flies, the smoke stays behind in the air.
- **Aura / Shield / Engulf**: Use `Local`. If the character moves, the aura moves _with_ them perfectly.

### `Rate` (Int)

**Code Usage**: Particles spawned per second via accumulator (`1.0 / Rate`). An Int, not Float.
**Artistic**:

- **Continuous Fire/Steam**: Use Rate (`60+` for smooth streams).
- **Thick Smoke**: High Rate (e.g., `150`).

### `Burst` (Int)

**Code Usage**: Particles spawned _once_ when the effect is created.
**Artistic**:

- **Explosion/Impact**: Use Burst (high number like `100-500`).

### `Shape` (String: `Point` | `Sphere` | `Cone` | `Line`)

**Code Usage**: Determines the initial `Position` offset and `Direction` vector in `ParticleSystem.fs`.

- `Point`: Spawns at origin (0,0,0). Direction is random on unit sphere.
- `Sphere`: Spawns on a disk of `Radius` (XZ plane). Direction depends on `EmissionMode`.
  - **Property**: `Radius` (Float).
- `Cone`: Spawns within a cone pointed at +Y. Direction is within cone angle.
  - **Property**: `Angle` (Float, degrees). Total cone spread (half-angle used internally).
  - **Property**: `Radius` (Float). Treated as length for cone calculations.
- `Line`: Spawns along a line/rectangle. Direction is +Y.
  - **Property**: `Width` (Float). Cross-width of line.
  - **Property**: `Length` (Float). Length along Y axis.

### `LocalOffset` (Vector3)

**Code Usage**: Offset applied to spawn position, rotated by owner rotation.
**Artistic**:

- `{ "Y": 1.0 }`: Particles spawn 1 unit above the entity's feet.
- Useful for placing fire at chest height, magic at hand level, etc.

### `InheritVelocity` (Float, default: 0.0)

**Code Usage**: `inheritedVelocity = ownerVelocity * InheritVelocity`.
**Artistic**:

- **Trailing Smoke**: Use `0.0`. Smoke hangs in the air.
- **Attached Fire**: Use `1.0`. Fire streaks behind moving object.

### `EmissionRotation` (Vector3, Euler degrees)

**Code Usage**: Euler angles (X=Pitch, Y=Yaw, Z=Roll) converted to quaternion and applied to emission direction.
**Artistic**:

- Rotate a cone to point sideways instead of up.
- Create angled jets or sprays.

### `EmissionMode` (String: `Uniform` | `Outward` | `Inward` | `EdgeOnly`)

**Code Usage**: Controls spawn distribution and initial direction within shaped areas.

- `Uniform`: Fill area with uniform distribution (sqrt random). Random directions.
- `Outward`: Spawn near origin (0-85% of length), direction points away from center.
- `Inward`: Spawn near edge (85-100% of length), direction points toward center.
- `EdgeOnly`: Spawn at edge (95-100% of length), random directions.

**Artistic**:

- **Fire Breath**: `Outward` + `Cone`. Particles shoot outward from mouth.
- **Convergence/Implosion**: `Inward` + `Sphere`. Particles flow inward.
- **Ring**: `EdgeOnly` + `Sphere`. Creates hollow ring effects.

### `FloorHeight` (Float, default: 0.0)

**Code Usage**: Y position where particles stop falling and apply ground friction.
**Artistic**:

- `0.0`: Ground level.
- Use higher values for elevated surfaces.

---

## Particle Properties

These control the _individual life_ of each particle. Nested under `"Particle": { ... }`.

### `Lifetime` (Float Range: `[Min, Max]`)

**Code Usage**: Randomly picked from range. Particle dies when `Life <= 0`. Used for Lerp interpolation.
**Artistic**:

- **Short (0.1 - 0.5s)**: Sparks, fast magic hits. Snappy.
- **Long (2.0 - 5.0s)**: Smoke, lingering fog, feathers.
- **Variation**: Large range `[0.5, 2.0]` looks natural/messy. Small range `[1.0, 1.1]` looks manufactured.

### `Speed` (Float Range: `[Min, Max]`)

**Code Usage**: `Velocity = Direction * Speed`. Added to position every frame (`P + V*dt`).
**Artistic**:

- **Explosion**: High speed (`50-400`).
- **Lazy Smoke**: Low speed (`0.5-5`).

### `Gravity` (Float)

**Code Usage** (`ParticleSystem.fs` line 367): `Velocity.Y -= Gravity * dt`.

> [!WARNING]
> **Isometric Gotcha**: In our coordinate system, **positive Y is UP** in 3D space. Because the formula _subtracts_ gravity:
> - **Positive Gravity** = Particles fall **DOWN** (toward floor). Use for debris, rocks.
> - **Negative Gravity** = Particles rise **UP** (into the sky). Use for fire, smoke, heat.
> - **Zero Gravity** = Particles float evenly. Use for magic.

**Artistic**:

- **Falling Debris**: Positive (`100-500`). Falls to ground.
- **Fire/Smoke**: Negative (`-10` to `-50`). **Heat Rises!** Crucial for realistic fire.
- **Magic**: Zero (`0.0`). Floats or expands evenly.

### `SizeStart` & `SizeEnd` (Float)

**Code Usage**: Linear interpolation (Lerp) over lifetime. `t = 1 - (Life / MaxLife)`.
**Artistic**:

- **Smoke/Fire**: `Start Small -> End Large`. Gas expands as it dissipates.
- **Sparks/Magic**: `Start Large -> End Small`. Energy burns out and shrinks.
- **Fade-Only**: Same size, different colors/alpha.

### `ColorStart` & `ColorEnd` (Hex String)

**Code Usage**: Lerp over lifetime. Format `#RRGGBBAA` (8 chars) or `#RRGGBB` (6 chars).
**Artistic**:

- **Fire**: `#FFD880FF` (bright yellow) -> `#FF400000` (dark red, transparent).
- **Magic**: Bright Cyan -> Dark Blue -> Transparent.
- **Tip**: End with `00` alpha for smooth fade-out.

### `RandomVelocity` (Vector3)

**Code Usage**: Adds random vector `[-X, X], [-Y, Y], [-Z, Z]` to initial velocity.
**Artistic**:

- **High Chaos** (`{ "X": 15, "Y": 15, "Z": 15 }`): Debris, unpredictable explosions.
- **Vertical Only** (`{ "Y": 10 }`): Jitter in height without horizontal chaos.

### `Drag` (Float, default: 0.0)

**Code Usage** (`ParticleSystem.fs` line 374-384): `dragFactor = 1 - (Drag * dt)`. Applied to X and Z velocity.
**Artistic**:

- **No Drag (0.0)**: Particles maintain speed. Projectile trails.
- **Light Drag (0.5-2.0)**: Particles slow down. More natural smoke/debris.
- **Heavy Drag (4.0+)**: Particles stop quickly. Dust settling.

---

## Skills.json Integration

Particle effects are spawned by skills via the `Visuals` and `ImpactVisuals` properties:

```json
"Delivery": {
  "Type": "Projectile",
  "Visuals": { "Vfx": "FireballProjectile" }  // Spawns while projectile flies
},
"ImpactVisuals": { "Vfx": "FireballImpact" }  // Spawns on hit
```

**Effect Overrides**: When skills spawn particles, they can override emitter properties:
- `Rotation`: Skill facing direction is applied to emission.
- `Area`: Skill's `Circle`, `Cone`, or `Line` area overrides emitter's `Shape` dimensions.
- `EmissionMode`: Can be overridden at spawn time.

This allows cone AOE skills to automatically shape their particles to match the skill area.

---

## Rendering Pipeline

### Billboarding

Particles are rendered as **camera-facing billboards** (`BillboardBatch.fs`):

1. Get camera's inverse view matrix
2. Extract `viewInv.Right` and `viewInv.Up` as billboard axes
3. Each particle is a quad centered at `position`, sized by `particle.Size * ModelScale`
4. Quad corners: `position ± (right * halfSize) ± (up * halfSize)`

### Coordinate Transform (`Render.fs` lines 354-366)

```
LogicPos (screen pixels) -> RenderPos (3D units)
X = LogicX / PixelsPerUnit.X
Z = LogicY / PixelsPerUnit.Y
Y = (ParticleY / PixelsPerUnit.Y) * 2.0  // Altitude
```

The altitude doubles because of the isometric squish factor compensation.

---

## Recipes / Common Effects

### 1. Realistic Explosion ("The Mushroom Cloud")

- **Shape**: `Sphere` (Radius 0.5-1.0).
- **Burst**: High (`300-500`).
- **Speed**: High (`50-400`).
- **Gravity**: Positive (`500-1000`). Debris falls.
- **Size**: `Start Small (0.5)` -> `End Large (2.5)`. Expansion.
- **Color**: White/Yellow -> Dark Red -> Transparent.
- **Drag**: `2.0-4.0`. Slows debris.

### 2. Magic Projectile Trail ("The Comet")

- **Space**: `World` (Critical! So particles stay behind).
- **Rate**: High (`100-200`) for a smooth line.
- **Speed**: Low (`10-30`). They shouldn't move much.
- **Gravity**: `0.0` or slight negative (`-3`). Magic defies physics.
- **Size**: `Start Medium` -> `End Small` (Shrink).
- **Texture**: Soft Glow.

### 3. Fire Breath / Cone Attack

- **Shape**: `Cone` (Angle: 45, Radius: 0.5).
- **EmissionMode**: `Outward`. Particles shoot outward.
- **Burst**: High (`300-500`).
- **Speed**: High (`200-400`).
- **Gravity**: `0.0`. Fire breath doesn't fall.
- **Drag**: Light (`0.5`). Slows as it expands.

### 4. Falling Debris / Rocks

- **Shape**: `Sphere` or `Cone` (pointing up).
- **Texture**: Sharp rock texture.
- **Gravity**: Positive (`300-500`). Needs to feel heavy.
- **Drag**: High (`4.0`). Slows quickly.
- **Lifetime**: Short, so particles die near the floor.

### 5. Rising Smoke / Heat

- **Shape**: `Sphere`.
- **Gravity**: Negative (`-30` to `-50`). Heat rises.
- **Size**: `Start Small` -> `End Large`. Smoke expands.
- **Color**: Dark opaque -> Lighter transparent.
- **BlendMode**: `AlphaBlend`. Smoke blocks background.

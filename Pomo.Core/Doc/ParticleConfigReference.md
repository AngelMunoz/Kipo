# Particle Configuration Reference

This guide explains how to configure `Particles.json` to create various visual effects. It connects the JSON properties to their artistic impact and how they are used in the code.

## Emitter Properties

These control _how_ and _where_ particles are born.

### `Name` (String)

**Code Usage**: `ParticleSystem.fs` uses this ID to look up the configuration when `ParticleSystem.Spawn("Name", ...)` is called.
**Artistic**: Use descriptive names like `FireballProjectile` or `IceExplosion`.

### `Texture` (String)

**Code Usage**: Path relative to Content root. Loaded as a `Texture2D`. In `Render.fs`, this is the image drawn on every billboard faces the camera (unless Rotation is used).
**Artistic**:

- **Soft/Blurry** (e.g. `smoke_01`): Good for gas, fire, magic auras. Blends well.
- **Sharp/Defined** (e.g. `spark_01`, `rock_04`): Good for debris, shrapnel, sparks. clearly distinct.
- **Directional** (e.g. `beam_02`): Needs rotation to work well, otherwise creates "fan" patterns.

### `BlendMode` (String: `AlphaBlend` | `Additive`)

**Code Usage**: Sets `GraphicsDevice.BlendState` in `Render.fs`.

- `AlphaBlend`: Standard transparency. Good for smoke, dust, debris. Dark colors block light behind them.
- **Artistic**: creating opaque clouds or "dirty" smoke.
- `Additive`: Adds color to the background. Good for fire, light, magic, energy.
- **Artistic**: Makes things look **HOT** or **GLOWING**. Overlapping particles become brighter (white hot). Black is invisible.

### `SimulationSpace` (String: `World` | `Local`)

**Code Usage**:

- `World`: Particles spawn at the emitter's position but processed independently. Their position is absolute.
- `Local`: Particles spawns relative to the emitter. In `Render.fs`, their render position is `ParticlePos + EmitterPos`.
  **Artistic**:
- **Trail / Smoke**: Use `World`. If a rocket flies, the smoke stays behind in the air.
- **Aura / Shield / Engulf**: Use `Local`. If the character moves, the shield moves _with_ them perfectly.

### `Rate` (Float) & `Burst` (Int)

**Code Usage**: `ParticleSystem.fs` accumulates time. `Rate` = Particles per second. `Burst` = Particles spawned _once_ when created.
**Artistic**:

- **Continuous Fire/Steam**: Use `Rate`.
- **Explosion/Impact**: Use `Burst` (high number like 100-300).
- **Thick Smoke**: High Rate (e.g., 60+).

### `Shape` (String: `Point` | `Sphere` | `Cone`)

**Code Usage**: Determines the initial `Position` offset and `Direction` vector in `ParticleSystem.fs`.

- `Point`: Spawns at 0,0,0. Random direction. Good for sparkles.
- `Sphere`: Spawns on surface or volume of sphere `Radius`. Direction is outward from center.
  - **Property**: `Radius` (Float).
- `Cone`: Spawns at base circle. Direction is Up (Y) + Spread.
  - **Property**: `Angle` (Float, degrees). Wider angle = Fountain/Spray. Narrow = Jet/Laser.
  - **Property**: `Radius` (Float). Width of the nozzle.

---

## Particle Properties

These control the _individual life_ of a speck of dust.

### `Lifetime` (Float Range: `[Min, Max]`)

**Code Usage**: Randomly picked from range. Particle dies when `Age > Lifetime`. Used for Lerp (fade out).
**Artistic**:

- **Short (0.1 - 0.5s)**: Sparks, fast magic hits. Snappy.
- **Long (2.0 - 5.0s)**: Smoke, lingering fog, feathers.
- **Variation**: Large range `[0.5, 2.0]` looks natural/messy. Small range `[1.0, 1.1]` looks manufactured/pulsing.

### `Speed` (Float Range)

**Code Usage**: `Velocity = Direction * Speed`. Added to position every frame `P + V*dt`.
**Artistic**:

- **Explosion**: High speed (`10+`).
- **Lazy Smoke**: Low speed (`0.5`).
- **Negative Speed**: Implosion / Black Hole (sucks in).

### `Gravity` (Float)

**Code Usage**: `Velocity.Y -= Gravity * dt`.
**Important**: In 3D physics, positive gravity pulls DOWN. Negative gravity pulls UP.
**Artistic**:

- **Falling Debris**: Positive (`10.0`). Falls to ground.
- **Fire/Smoke**: Negative (`-5.0` to `-20.0`). **Heat Rises!** This is crucial for realistic fire. Large negative gravity creates a "Pyre" or "Mushroom Cloud" effect.
- **Magic**: Zero (`0.0`). Floats or expands evenly.

### `SizeStart` & `SizeEnd` (Float)

**Code Usage**: Linear interpolation (Lerp) over lifetime.
**Artistic**:

- **Smoke/Fire**: `Start Small -> End Large`. Gas expands as it cools/dissipates.
- **Sparks/Magic**: `Start Large -> End Small`. Energy burns out and shrinks.
- **Pop**: `Start Large -> End Large`. Toon style.

### `ColorStart` & `ColorEnd` (Hex String)

**Code Usage**: Lerp over lifetime. Format `#RRGGBBAA`.
**Artistic**:

- **Fire**: Yellow/White (`#FFFF80`) -> Red/Orange (`#FF4000`) -> Dark/Transparent (`#00000000`).
- **Magic**: Bright Cyan -> Dark Blue -> Transparent.
- **Tip**: Always assume `ColorEnd` has `00` alpha (transparent) if you want it to fade out smoothly.

### `RandomVelocity` (Vector3)

**Code Usage**: Adds a random vector `[-X, X]` to the initial velocity. `Velocity += RandomVector`.
**Artistic**: Adds chaos/turbulence. Breaking up perfect spheres.

- **High Chaos**: Debris, unpredictable explosions.

---

## Recipes / Common Effects

### 1. Realistic Explosion ("The Mushroom Cloud")

- **Concept**: A violent burst that expands fast and produces hot gas that rises rapidly.
- **Shape**: `Sphere` (Radius 0.5).
- **Burst**: High (`300`).
- **Speed**: High (`10.0`). Just get it out there.
- **Gravity**: **Very High Negative** (`-500.0`). The heat creates massive lift. This stretches the sphere into a pillar.
- **Size**: `Start Small (1.0)` -> `End Giant (10.0)`. Expansion.
- **Color**: White/Yellow -> Dark Red -> Black Smoke.
- **Blend**: `Additive` (if mostly fire) or `AlphaBlend` (if mostly smoke).

### 2. Magic Projectile Trail ("The Comet")

- **Concept**: A stream of particles left behind a moving object.
- **Space**: `World` (Critical! So particles stay behind).
- **Rate**: High (`60+`) for a smooth line.
- **Speed**: Low (`0.5`). They shouldn't move much, just hang there.
- **Gravity**: `0.0`. Magic usually defies physics.
- **Size**: `Start Medium` -> `End Small` (Shrink).
- **Texture**: Soft Glow.

### 3. Shockwave / Nova

- **Concept**: A flat ring expanding on the ground.
- **Shape**: `Sphere` or `Cylinder` (if supported), or just `Sphere` with flattened gravity?
- **Trick**: Use `Gravity: 0`. Use `Speed: High`.
- **Trick**: If you want a _Ring_, you need a Shape that spawns _only_ on the edge (e.g. `HollowSphere` - not currently implemented, but `Sphere` with `Speed` helps).
- **Size**: `Start Huge` -> `End Huge` (Fade out alpha only).

### 4. Falling Debris / Rocks

- **Concept**: Chunks of rock flying out and hitting the ground.
- **Shape**: `Cone` (pointing up) or `Sphere`.
- **Texture**: Sharp rock texture.
- **Gravity**: Positive (`20.0`). Needs to feel heavy.
- **Collision**: (Note: Simple particles usually don't collide, so they will fall through the floor. Keep lifetime short so they die "at" the floor).

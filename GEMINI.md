# F# and MonoGame Project Structure

This is a cross-platform game built with F# and MonoGame. The project is structured to share core logic across multiple platforms (Windows, DesktopGL, Android, iOS).

> [!IMPORTANT] > **You MUST read and follow [AGENTS.md](AGENTS.md)** — it contains the authoritative guidelines for this project. This file provides Gemini-specific customizations and quick reference. Also check [.agents/fsharp_conventions.md](.agents/fsharp_conventions.md) for F# code style.

# Assistant Guidelines.

There's two ways to work with the user:

- Planing, Design, Back and forth until the user is satisfied with the design.
- Implementing the design.

When the user poses questions, You MUST answer these questions rather than "Updating Planning" or "Updating Design" or "Updating Implementation".

Unless the user explicitly grants you "freedom" to decide yourself what to do, user consent is required before you can proceed to the next step.

THERE IS NO IMPLICIT PERMISSION TO "GO AHEAD" OR "CONTINUE" OR "PROCEED".

## Code sugestions

When you're suggesting code to the user, your proposed code should avoid living in a single place.

- Large function bodies should be refactored into smaller functions.
- Logic that can be reused should be moved to module-level functions.
- Avoid putting large amounts of logic directly inside class methods.

**Game Systems:**

Systems inherit from `GameSystem` (which extends `GameComponent`). The class must be a **thin wrapper** that:

1. Stores dependencies in `let` bindings
2. Delegates all logic to module-level functions
3. Calls `AVal.force` only inside `Update()` or `Draw()` to resolve values

**Default to non-reactive iteration**: Most systems iterate over `HashMap` directly rather than using adaptive transformations. Use adaptive patterns only when caching derived computations is beneficial.

### Memory Optimization

- Use `System.Buffers.ArrayPool<T>.Shared` for frequently-resized arrays (see `State.fs`, `EventBus.fs`)
- Use `[<Struct>]` DUs for high-frequency commands
- Auto-shrink buffers after sustained low usage

## Animation System & 3D Assets

### 1. Model Coordinate Space & Pivots

- **Origin Point:** KayKit assets have their origin `(0,0,0)` at the **feet/floor**, not at the geometric center or joint.
- **Rotation Issue:** Rotating these models directly causes them to swing in a wide arc around the floor.
- **The Fix (Pivots):** We use a `Pivot` property in `RigNode` (defined in `Models.json`).
  - `Pivot` represents the local coordinate of the joint (e.g., Shoulder) relative to the model's origin.
  - **Render Logic:** The system applies `Translate(-Pivot) * Rotation * Translate(Pivot)` to force rotation around the joint.
  - **Typical Shoulder Pivot:** `{ "X": 0.0, "Y": 1.5, "Z": 0.0 }` (for a standard humanoid).

### 2. Animation Axes (Isometric Context)

- **Y-Axis (Yaw):** Controls **Forward/Backward** swing.
  - Left Arm Forward: Negative Y (`-30`).
  - Right Arm Forward: Positive Y (`+30`) (Due to symmetry/mirroring).
- **Z-Axis (Pitch):** Controls **Up/Down** flapping.
  - Up: Positive Z (`+20`).
  - Down: Negative Z (`-20`).
- **X-Axis (Roll):** Controls twisting (Drill motion).

### 3. Data Structures

- **Animations:** Stored in `Content/Animations.json`. Defined by `Keyframes` (Time, Rotation Quaternion).
- **Rigs:** Stored in `Content/Models.json`. Defines hierarchy (`Root` -> `Body` -> `Arm_L`) and Pivots.

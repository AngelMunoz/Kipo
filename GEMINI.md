# F# and MonoGame Project Structure

This is a cross-platform game built with F# and MonoGame. The project is structured to share core logic across multiple platforms (Windows, DesktopGL, Android, iOS).

- The general guidelines of this project are in [AGENTS.md](AGENTS.md) and [.agents/fsharp_conventions.md](.agents/fsharp_conventions.md)

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

**Game Systems and Drawable Game Systems:**

When implementing Systems, avoid inheriting from `GameComponent` or `DrawableGameComponent` unless strictly necessary for MonoGame integration (like `GraphicsDeviceManager`).
Instead, prefer simple F# types (classes or functions) that expose an `Update` (and optional `Draw`) method.
This allows strict control over execution order and better testability.

The system logic itself should still delegate to module-level functions.

Data used for updates and draws should be stored in let bindings within the class to allow FSharp.Data.Adaptive to start tracking and caching data.
Only at evaluation time (e.g. inside the Update or Draw methods) should we call `AVal.force` to get the current value.

```fsharp

module System =
   let nonAdaptiveLogic data currentValue =
      // non-adaptive logic here
      let transformedData =
         External.processData data currentValue
      transformedData

   let transformation (data: _ aval) (otherData: amap<_,_>): _ aval =
      otherData
      |> AMap.mapA(fun key value -> adaptive {
         let! data = data
         // adaptive logic here
         let transformedValue = nonAdaptiveLogic data value
         // use transformedValue in further adaptive computations
         let adaptiveResult = Module.adaptiveLogic transformedValue
         return! adaptiveResult
      })

   let publishEventsFromTransformation data eventBus =
      for data in data do
         eventBus.Publish(data)

   // Preferred: Simple F# Class
   type System(world: World, eventBus: EventBus) =
      interface IDisposable with member _.Dispose() = ()

      let adaptiveData =
         transformation
            Projections.SomeAdaptiveValue world
            Projections.SomeAdaptiveMap world

      member _.Update(gameTime) =
         let currentValue = AVal.force adaptiveData
         // use currentValue for update logic

         // for example to trigger events:
         publishEventsFromTransformation currentValue eventBus
```

**Scene Architecture:**

We use an abstract base class `Scene` (implementing `IDisposable` with virtual methods) to define game states (MainMenu, Gameplay).
Implementations should prefer **F# Object Expressions** over creating named subclasses, returning them from factory functions in `CompositionRoot` or `SceneFactory`.

```fsharp
// Prefer this:
let createGameplayScene (deps) =
    { new Scene() with
        override _.Initialize() = ...
        override _.Update(gameTime) = ...
        override _.Dispose() = ...
    }
```

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

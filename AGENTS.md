# AI Agent Instructions

## Quick Start

- Read this file completely before making any code changes
- Tasks and plans are handled in github, use the `gh` CLI to interact with issues and gather context
- Follow F# conventions and Data Oriented Programming principles strictly
- When in doubt, ask clarifying questions

## General Guidelines

- **Do not add extra comments to the code**
- Follow the coding style and conventions used in the existing codebase
- Avoid aggressive refactors; always do small, methodical, incremental, and verifiable changes
- If working on an implementation that is part of the current plan (either outlined in the current `gh` cli pulled issue or by the user prompt), always update the corresponding document to reflect the current progress

**IMPORTANT**: You can find suplementary guidelines and conventions in the `.agents` folder in the project root. See [./.agents/README.md](./.agents/README.md) for details.

## 🤖 PROGRAMMING PARADIGM HIERARCHY 🤖

**MANDATORY PARADIGM ORDER - STRICTLY ENFORCE:**

1. **PRIMARY: Data Oriented Programming (DOP)** - Default programming style

   - Data structures are first-class citizens
   - Immutable data with pure transformations
   - Separation of data and logic
   - Functions operate on data, not encapsulated within objects

2. **SECONDARY: Interface-based Abstraction** - For service boundaries only

   - Use interfaces for `EngineServices` and external dependencies
   - Abstractions must be minimal and focused

3. **TERTIARY: Imperative Programming** - Limited, controlled usage

   - Only for performance-critical sections
   - Must be clearly documented and justified

4. **QUATERNARY: Mutable Imperative** - Exceptional cases only
   - Must be self-contained within single functions
   - Requires explicit documentation of mutation scope
   - Never expose mutable state outside function boundaries

## Domain Type Modifications

## Data Oriented Programming with FSharp.Data.Adaptive (DOP + FDA)

### Core DOP Principles

- **Data as Primary Organizing Principle**: All game logic organized around immutable data structures
- **Pure Data Transformations**: Functions transform data without side effects
- **Immutable Data Structures**: FDA collections (`cmap`, `amap`, `cset`, `aset`, `clist`, `alist`) enforce immutability
- **Reactive Data Flow**: FDA extends DOP with automatic incremental computation

### FDA Implementation

- **Authoritative State**: The game's core state is maintained in a central, immutable `GameState` record containing adaptive values (`cval`, `cmap`, `clist`)
- **Derived Data**: All other game views and stats should be derived from this authoritative state using adaptive computations. For example, a set of "alive" entities or derived character stats should be `aset` or `amap` projections
- **Adaptive Collections**:
  - **cmap/amap**: Entity databases, component storage
  - **cset/aset**: Active entities, selected units, collision groups
  - **clist/alist**: Ordered collections like inventory, turn order
  - **Index**: Stable references for list elements that survive reordering
  - **cmap, cset, clist**: Read/write interfaces for mutations in adaptive collections
  - **HashMap, HashSet, IndexList**: Non-adaptive data structures that allow tracking of modifications, power up adaptive collections internally, and are preferred when converting between adaptive and non-adaptive values (e.g., `myAList |> AList.toAVal` and `myUpdatedList |> AList.ofAVal`)
  - Use incremental adaptive collections for dynamic state to ensure efficient, fine-grained updates
  - For static or rarely changing data, favor FDA's HashSet, HashMap, IndexList collections; otherwise, standard BCL collections are appropriate
- **Adaptive Computations**:
  - Keep all calculations within the "adaptive realm" for as long as possible. Only resolve to a concrete value when absolutely necessary (e.g., for rendering)
  - Use `adaptive { ... }` computation expressions to compose multiple adaptive values
  - When transforming adaptive collections, prefer efficient mapping functions like `AList.mapA` to avoid unnecessary conversions
  - Using `AVal.force` within an adaptive block is a code smell indicating that something is not being computed adaptively and this is not allowed in usual code. `AVal.force` is reserved to `transact` blocks for the majority of times

**Disallow comments in the agent-generated code.**

## Performance Guidelines

**Pomo.Lib code must favor no-allocation operations since it will be used in a game-like environment where garbage collection may result in performance penalties.**

- **Domain and value-like types must be decorated as a struct**
- **Discriminated unions that represent domain concepts must be decorated as a struct DU**
- **Value tuples** (`struct(v1,v2)`) are favored over reference tuples
- **ValueOption** is favored over Option unless necessary (convert `Option.toValueOption` or `ValueOption.ofOption` when necessary as some libraries do not provide value options)

## Testing Strategy

- **Unit Tests with Fakes**: Test core logic modules in isolation by providing fake implementations of the `EngineServices` interfaces
- **Property-Based Tests**: Use libraries like FsCheck to verify the mathematical correctness of rules, such as stat composition and effect stacking
- **Deterministic Simulation**: Leverage the deterministic nature of the core logic by using a fixed seed for the random number generator in tests to reproduce complex scenarios
- **FsCheck**: We need to ensure that we're using the right features of the library besides just property testing

## Code Conventions

**CRITICAL**: Please review the general F# coding conventions defined in [./.agents/fsharp_conventions.md](./.agents/fsharp_conventions.md) before proceeding.

**IMPORTANT**: Guidelines below are particular opinions that take priority for this codebase, anything else not mentioned here should follow the general F# conventions.

### Functions Must Be Focused

When you're suggesting code to the user, your proposed code should avoid living in a single place.

- Large function bodies should be refactored into smaller functions.
- Logic that can be reused should be moved to module-level functions.
- Avoid putting large amounts of logic directly inside class methods.

Each function should be descriptive of what it does. If a function is doing too much, it can either use:

**Local functions:**

```fsharp
let calculateDamage attacker defender =
   let computeBaseDamage attacker defender = ...
   let applyModifiers baseDamage attacker defender = ...

   let baseDamage = computeBaseDamage attacker defender
   let modifiedDamage = applyModifiers baseDamage attacker defender
   modifiedDamage
```

**Module-level functions:**

```fsharp
module Combat =
   let computeBaseDamage attacker defender = ...
   let applyModifiers baseDamage attacker defender = ...

   let calculateDamage attacker defender =
      let baseDamage = computeBaseDamage attacker defender
      let modifiedDamage = applyModifiers baseDamage attacker defender
      modifiedDamage
```

Functions and modules do not need to be private/internal, that is up to the developer's discretion.

### Modules Must Be Cohesive

Group related functions and types into modules that represent a single concept or area of functionality.

### Match Expressions Body Should Be Small

Each branch of a match expression should be concise. If a branch is complex, consider extracting it into a separate function.

```fsharp
match someValue with
| Case1 -> handleCase1 someValue
| Case2 -> handleCase2 someValue
| Case3 -> handleCase3 someValue
```

### Match Expressions Should Be Exhaustive

For user authored types, try to ensure that all cases are handled explicitly.

DO: ✅

```fsharp
match someValue with
| Case1 -> ...
| Case2 -> ...
| Case3 -> ...
```

DO NOT: ❌

```fsharp
match someValue with
| Case1 -> ...
| Case2 -> ...
| _ -> ...
```

In special cases we may need to use a wildcard pattern, if we truly know there's no logical way for other cases to occur.

```fsharp
match someValue with
| ValueSome Case1 when someCondition -> ...
| ValueSome Case2 -> ...
| _ -> () // logically unreachable
```

However, avoid this pattern when possible.

### Avoid Deep Nesting

Where possible use inline'able Active patterns, Partial Active Patterns and function composition to flatten nested logic.

**Example with Active Patterns:**

```fsharp
let inline (|IsEven|IsOdd|) x =
   if x % 2 = 0 then IsEven else IsOdd

let processNumber x =
   match x with
   | IsEven -> handleEven x
   | IsOdd -> handleOdd x
```

**Example with Partial Active Patterns:**

```fsharp
[<return: Struct>]
let inline (|ActiveEffect|_|) (effectType: EffectType) (effect: Effect) =
   if effect.EffectType = effectType then ValueSome effect else ValueNone

let processEffect effect =
   match effect with
   | ActiveEffect EffectType.Damage dmgEffect -> handleDamageEffect dmgEffect
   | ActiveEffect EffectType.Heal healEffect -> handleHealEffect healEffect
   | effect -> handleOtherEffect effect
```

**Game Systems and Drawable Game Systems:**

When implementing Sytems, whether they're a GameComponent (or our GameSystem class) or a DrawableGameComponent. The component class itself should be a slim wrapper that delegates all logic to module-level functions.

Data used for updates and draws should be stored in let bindings within the class to allow FSharp.Data.Adaptive to start tracking and caching data.
only at evaluation time (e.g. inside the Update or Draw methods) should we call `AVal.force` to get the current value.

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


   type System(game: Game) =
      inherit GameSystem(game) =

      let adaptiveData =
         transformation
            Projections.SomeAdaptiveValue this.World
            Projections.SomeAdaptiveMap this.World

      override _.Update(gameTime) =
         let currentValue = AVal.force adaptiveData
         // use currentValue for update logic

         // for example to trigger events:
         publishEventsFromTransformation currentValue this.EventBus
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

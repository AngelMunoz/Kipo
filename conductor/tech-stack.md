# Technology Stack

## Core Language & Runtime

- **Language:** F# (Functional-first)
- **Framework:** MonoGame (Cross-platform game framework)
- **Runtime:** .NET

## State & Data Management

- **State Management:** `FSharp.Data.Adaptive` (DOP + FDA) for efficient change propagation and derived computations.
- **Data Architecture:** Data-Oriented Programming (DOP).
- **Serialization:** JSON (used for all game content: skills, AI, models, animations, etc.).
- **VFX Pipeline:** Hybrid system supporting billboard particles and custom 3D mesh rendering for complex visual effects.

## Target Platforms

- **Desktop:** DesktopGL (Cross-platform), WindowsDX.
- **Mobile:** Android.
- _(Note: iOS project exists but is not a primary target for development)_

## Engineering Standards

- **Performance:** Strict focus on low-allocation and no-allocation operations (Structs, `ArrayPool`, `ValueOption`).
- **Logic:** Separation of immutable data from pure transformation logic.

# Technology Stack

## Core Language & Runtime
- **Language:** F# (Functional-first)
- **Framework:** MonoGame (Cross-platform game framework)
- **Runtime:** .NET

## State & Data Management
- **State Management:** `FSharp.Data.Adaptive` (DOP + FDA) for efficient change propagation and derived computations.
- **Data Architecture:** Data-Oriented Programming (DOP).
- **Serialization:** JSON (used for all game content: skills, AI, models, animations, etc.).

## Target Platforms
- **Desktop:** DesktopGL (Cross-platform), WindowsDX.
- **Mobile:** Android.
- *(Note: iOS project exists but is not a primary target for development)*

## Engineering Standards
- **Performance:** Strict focus on low-allocation and no-allocation operations (Structs, `ArrayPool`, `ValueOption`).
- **Logic:** Separation of immutable data from pure transformation logic.

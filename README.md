# Kipo

A production-grade RPG engine prototype, built as a playground for exploring functional game development patterns in F# and MonoGame.

![Demo](./2025-12-23%2009-10-54.gif)

## What is This?

Kipo is an isometric action-RPG engine written entirely in F#. It's a technical sandbox where serious engineering meets game development curiosity. If you've ever wondered what an RPG engine looks like built from the ground up with functional principles—here it is.

This isn't a finished game. It's a working prototype with real systems: combat, AI, skills, projectiles, visual effects, and more. The architecture is designed for performance and maintainability, but the scope is still evolving.

## Technical Highlights

### Data-Oriented Programming

State is separated from logic. Immutable data structures flow through pure transformation functions. The result is code that's easier to reason about, test, and parallelize.

### Performance Engineering

Critical paths are allocation-free. Struct discriminated unions, pooled array buffers, and explicit command queues minimize GC pressure. State writes are batched and flushed once per frame.

### Custom Isometric Render Pipeline

2D game logic is projected into 3D isometric space via a parallelized command-based renderer. Entities, terrain, and particles are emitted as render commands, then sorted and drawn efficiently.

### Data-Driven AI

Enemy behaviors are defined using archetypes and decision trees—all in JSON. Perception, memory, and skill selection are modular systems. No hardcoded enemy logic.

### Hybrid VFX System

Particles aren't just billboards. The VFX system supports both 2D textures and 3D mesh particles (spinning coins, rising pillars, tumbling debris) with full physics integration.

### Everything is JSON

Skills, items, AI archetypes, animations, model rigs, and particle effects are all configured in JSON files. Designers can iterate without touching code.

### Cross-Platform

The same `Pomo.Core` library runs on Windows (DirectX), Linux/macOS (DesktopGL), and Android. iOS scaffolding exists as well.

## Project Structure

```
Kipo/
├── Pomo.Core/           # Shared game logic (F#)
│   ├── Domain/          # Types and data models
│   ├── Systems/         # Game systems (AI, Combat, Rendering, etc.)
│   ├── Rendering/       # Emitters and render math
│   └── Content/         # JSON configs and assets
├── Pomo.DesktopGL/      # Desktop runner (Linux/macOS/Windows)
├── Pomo.WindowsDX/      # Windows DirectX runner
├── Pomo.Android/        # Android runner
├── Pomo.Core.Tests/     # Unit tests
└── docs/                # Additional documentation
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://get.dot.net) or later
- [MonoGame](https://monogame.net/) (pulled via NuGet)

### Build & Run (Desktop)

```bash
# Clone the repository
git clone https://github.com/AngelMunoz/Kipo.git
cd Kipo

# Restore dependencies
dotnet restore

# Run the DesktopGL version
dotnet run --project Pomo.DesktopGL/Pomo.DesktopGL.fsproj
```

### Run Tests

```bash
dotnet test Pomo.Core.Tests
```

### IDE Setup

**Rider / Visual Studio**: Open `Pomo.slnx`. Should work out of the box for desktop targets.

**VS Code**: Install the Ionide extension for F# support. Press F5 to debug.

## License

This project is **not open source**, it is **source available**. The code is provided for viewing and educational purposes only. See [LICENSE](LICENSE) for details.

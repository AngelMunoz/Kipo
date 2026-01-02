# Initial Concept

This is an RPG-like Game written in F#, the main audience for this repository is F# and game developers.

For the moment this is not intended to be a fully fledged videogame that one can buy or download from a store.

# Product Guide

## Target Audience

- **Primary:** F# and Game Developers interested in functional and data-oriented programming patterns in game development.
- **Secondary:** Enthusiasts looking for a robust reference implementation of a high-performance RPG engine.

## Core Goals

- **Technical Showcase:** Demonstrate best practices for Data-Oriented Programming (DOP) and functional principles in a high-performance game context.
- **Production Prototype:** Build a production-grade working prototype that serves as a foundation for a commercial-ready RPG engine.
- **Learning Resource:** Provide complex implementations of systems like adaptive AI, pathfinding, and combat as learning resources.

## Key Features

- **Advanced AI System:** Data-driven AI using archetypes and decision trees for complex entity behaviors.
- **Adaptive Game State:** Utilization of `FSharp.Data.Adaptive` for efficient change propagation and derived computations.
- **Core RPG Mechanics:** Deep implementation of stats, skills, damage calculations, and inventory systems.
- **True 3D World Architecture:** Full 3D logic for physics, collision, and pathfinding, decoupled from the camera view.
- **Integrated Map Editor:** Built-in 3D block map editor.
- **High-Performance Rendering:** Parallelized, command-based rendering pipeline separating logic from isometric presentation.
- **Advanced VFX System:** Support for hybrid particle systems including billboard textures and 3D mesh particles (e.g., domes, columns).
- **Cross-Platform Architecture:** Shared core logic across Windows, Android, iOS, and DesktopGL.

## Visual Style

- **Developer-Focused:** Priority is on mechanics and code structure. The visual style is currently functional, intended to be replaced or refined as the project matures.

## Architectural Principles

- **Data-Oriented Programming (DOP):** Data is the primary organizing principle, separated from logic via pure transformations.
- **Functional Domain Logic:** Heavy emphasis on immutability and stateless transformations for core logic.
- **Strict Performance Enforcement:** Zero-allocation or low-allocation operations in critical paths using structs, `ArrayPool` buffers, and `ValueOption` to minimize GC pressure.

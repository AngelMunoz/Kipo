# Refactoring Plan: Scene-Based Architecture

**Objective:** Move from a global, singleton `World` state to a managed `Scene` architecture. This separates "Game Application" logic from "Gameplay Session" logic, allowing for Main Menus, map transitions, and proper resource management without hard restarts.

**Core Philosophy:** 
- **Scenes** (`IScene`) manage high-level application states (e.g., Main Menu, Gameplay, Pause).
- **GameplayScene** persists across Map transitions, preserving the HUD and Player State.
- **Map Switching** handles unloading/loading entities and terrain within the active GameplayScene.
- **UI** is owned by the Scene, not a global system.

**General Rule:**
- Each completed step in this plan must be manually tested before moving to the next.

## Phase 1: Infrastructure

- [x] **Define `Scene` Abstract Class:** Create the base class with virtual `Initialize`, `Update`, `Draw` and `Dispose` methods.
    -   *Note:* Implementations will use F# Object Expressions.
- [x] **Create `SceneManager`:** A mechanism in `PomoGame` to switch active scenes.
- [x] **Refactor `CompositionRoot`:**
    -   Split into `GlobalScope` (Services, Stores) and `SceneFactory` (Methods to create specific scenes via object expressions).
    -   Ensure `World` creation is decoupled from `Game` startup.

## Phase 2: The Main Menu Scene

- [x] **Create `MainMenuScene`:**
    -   Implement `IScene`.
    -   Move `MainMenuUI` logic from `UISystem` into this scene.
    -   Remove `World` dependencies (Menus don't need physics).
- [x] **Wire up Transition:**
    -   "New Game" button triggers `SceneManager.LoadScene(GameplayScene)`.

## Phase 3: The Gameplay Scene (The "Scenario")

- [x] **Create `GameplayScene`:**
    -   This becomes the new home for `World`, `EventBus`, and Gameplay Systems (`Combat`, `Movement`, `Render`).
    -   Move initialization logic from `PomoGame.Initialize` to `GameplayScene.Initialize`.
- [x] **Migrate UI:**
    -   Move `GameplayUI` (HUD) logic into `GameplayScene`.
    -   Ensure HUD binds to `World` data using FSharp.Data.Adaptive.
- [x] **Implement `MapManager` Logic:**
    -   Create logic within `GameplayScene` to handle "Unload Map A -> Load Map B".
    -   *Note:* This replaces the idea of "Destroying the Scene" for map changes.

## Phase 4: Cleanup & Enabling

- [x] **Remove Global Component List:** `PomoGame` should no longer add 15 components in its constructor. It should only add `SceneManager` (and maybe global services like `Input`).
- [ ] **System Decoupling:** Refactor Systems to inherit from `PomoSystem` (abstract class with default `Initialize`, `Update`, `Draw`, `Dispose`) instead of `GameComponent`.
    -   *Goal:* Remove MonoGame component dependency for cleaner architecture.
    -   [ ] Define `PomoSystem` abstract class.
    -   [ ] `RawInputSystem`
    -   [ ] `InputMappingSystem`
    -   [ ] `PlayerMovementSystem`
    -   [ ] `UnitMovementSystem`
    -   [ ] `AbilityActivationSystem`
    -   [ ] `CombatSystem`
    -   [ ] `ResourceManagerSystem`
    -   [ ] `ProjectileSystem`
    -   [ ] `CollisionSystem`
    -   [ ] `MovementSystem`
    -   [ ] `NotificationSystem`
    -   [ ] `EffectProcessingSystem`
    -   [ ] `EntitySpawnerSystem`
    -   [ ] `RenderOrchestratorSystem`
    -   [ ] `DebugRenderSystem`
    -   [ ] `AISystem`
    -   [ ] `StateUpdateSystem`
    -   [ ] `TerrainRenderSystem`

## Future Considerations (Post-Refactor)
- **Map Transitions:** Implementing the actual trigger (portals) to call `GameplayScene.ChangeMap()`.
- **Pause State:** Implementing a `PauseScene` or a state within `GameplayScene`.

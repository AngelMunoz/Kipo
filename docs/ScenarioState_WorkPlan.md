# Work Plan: Implement Scenario State

This document tracks the work required to implement independent Scenario States in the game.

## Overview
We are introducing `ScenarioId` to decouple the game world from a single global map. This allows multiple scenarios (maps) to run simultaneously with their own entities and physics.

## Step-by-Step Plan

### Phase 1: Domain & World Data Structures
- [ ] **Step 1.1**: Define `ScenarioId` and `Scenario` struct in `World.fs`.
    - *Action*: Add `ScenarioId` UMX tag. Add `[<Struct>] type Scenario`.
    - *Verification*: Build project (`dotnet build`). Ensure no compilation errors.
- [ ] **Step 1.2**: Update `World` to track Scenarios.
    - *Action*: Add `Scenarios` and `EntityScenario` fields to `MutableWorld` and `World` interface. Update `create` function.
    - *Verification*: Build project. Fix any broken `World.create` calls in `CompositionRoot`.

### Phase 2: Core Systems Refactoring
- [ ] **Step 2.1**: Refactor `EntitySpawner` to support Scenarios.
    - *Action*: Update `SpawnEntityIntent` to include `ScenarioId`. Update `EntitySpawnerSystem` to assign `EntityScenario` component.
    - *Verification*: Run game. Verify Player and Enemies still spawn (using a default/hardcoded scenario ID for now if needed).
- [ ] **Step 2.2**: Refactor `CollisionSystem` for Scenarios.
    - *Action*: Remove `mapKey` dependency. Update loop to iterate `World.Scenarios`. Filter entities by scenario.
    - *Verification*: Run game. Move player into a wall. Verify collision still works.

### Phase 3: Integration & Cleanup
- [ ] **Step 3.1**: Update `CompositionRoot` Scenario Initialization.
    - *Action*: In `createGameplay`, create the initial "Lobby" scenario and add it to `World`.
    - *Verification*: Run game. Ensure smooth transition from Main Menu to Gameplay.
- [ ] **Step 3.2**: Refactor `Navigation` (Optional/If needed).
    - *Action*: Ensure pathfinding uses the correct map for the entity's scenario.
    - *Verification*: Click to move. Verify pathfinding works.

### Phase 4: Final Verification
- [ ] **Step 4.1**: Code Review & Cleanup.
    - *Action*: Review all changes. Remove temporary hardcoded values.
    - *Verification*: Full regression test of gameplay loop.

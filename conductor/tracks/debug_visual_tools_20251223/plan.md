# Implementation Plan - Visual Debug Tools

## Phase 1: Foundation & UI (Myra)
- [ ] Task: Create `DebugState` in `Pomo.Core`
    - Define adaptive boolean flags for each subsystem (Collisions, AI, Skills, Grid, etc.).
    - Expose `DebugState` service in `PomoEnvironment`.
- [ ] Task: Implement `PrimitiveBatch`
    - Create a simple batch renderer for lines and shapes (Circle, Rect) in `Pomo.Core.Graphics`.
    - Ensure it supports efficient line drawing (VertexPositionColor).
- [ ] Task: Implement `DebugUISystem`
    - Create a Myra `Window` or `Panel` containing CheckBoxes for each debug flag.
    - Bind CheckBox events to `DebugState` adaptive values.
    - Toggle visibility of the panel with a hotkey (F3).
- [ ] Task: Conductor - User Manual Verification 'Phase 1: Foundation & UI' (Protocol in workflow.md)

## Phase 2: Physics & Spatial Visualization
- [ ] Task: Implement `CollisionDebugSystem`
    - Subscribe to `DebugState` flags (Collisions, Grid).
    - Render Entity Bounding Circles/Boxes using `PrimitiveBatch`.
    - Render Map Object Polygons/Polylines.
    - Render Spatial Grid lines overlay.
- [ ] Task: Integrate Collision Visuals
    - Register `CollisionDebugSystem` in `CompositionRoot` (Debug build only or generally available).
- [ ] Task: Conductor - User Manual Verification 'Phase 2: Physics & Spatial Visualization' (Protocol in workflow.md)

## Phase 3: Logic Visualization (AI & Skills)
- [ ] Task: Implement `AIDebugSystem`
    - Subscribe to `DebugState` flags (AI).
    - Render text labels (Myra `Label` or `SpriteBatch.DrawString`) above entities for State/Behavior.
    - Render FOV cones and Pathfinding waypoints using `PrimitiveBatch`.
- [ ] Task: Implement `SkillDebugSystem`
    - Subscribe to `DebugState` flags (Skills).
    - Render Range Circles for selected skills.
    - Render AoE shapes (Cone/Line) during targeting/casting.
- [ ] Task: Conductor - User Manual Verification 'Phase 3: Logic Visualization' (Protocol in workflow.md)

## Phase 4: Integration & Verification
- [ ] Task: Final Integration
    - Ensure all debug systems are correctly updated and drawn in the game loop.
    - Verify Z-ordering (Debug overlay should be on top).
- [ ] Task: Code Cleanup & Documentation
    - Ensure debug code is properly isolated.
- [ ] Task: Conductor - User Manual Verification 'Phase 4: Integration & Verification' (Protocol in workflow.md)

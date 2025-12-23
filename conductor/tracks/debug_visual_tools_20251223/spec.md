# Visual Debug Tools Specification

## Overview
This track aims to implement a comprehensive visual debugging system for the Pomo engine. The goal is to provide real-time, in-game visualizations of invisible game logic (collisions, pathfinding, AI states, etc.) to assist developers in verifying correctness and diagnosing issues.

## Functional Requirements

### 1. Debug Toggle & UI
-   **Debug Mode:** A global toggle (e.g., F3 or console command) to enable/disable all debug visuals.
-   **Sub-System Toggles:** Ability to toggle specific visualizations independently via a GUI panel:
    -   Show Entity Bounds
    -   Show Map Collisions
    -   Show Spatial Grid
    -   Show AI State/Path
    -   Show Skill AoE
-   **UI Framework:** Use **Myra** (consistent with existing UI) to render the debug control panel.

### 2. Collision & Physics Visualization
-   **Entity Boundaries:** Render the bounding box/circle for all entities.
-   **Map Objects:** Render the collision polygons/polylines for map objects (walls, obstacles).
-   **Spatial Grid:** Overlay the collision grid cells to show spatial partitioning.
-   **Collision Response:** Draw lines representing the MTV (Minimum Translation Vector) when a collision occurs (transient visual).

### 3. Skill & Combat Visualization
-   **Skill AoE:** Draw the effective area (Circle, Cone, Line) of a skill *before* and *during* execution to verify targeting logic.
-   **Range Indicators:** Show max range circles for the selected skill.
-   **Hit Detection:** Briefly highlight entities or areas that register a "hit" from a skill.

### 4. AI & Navigation Visualization
-   **Pathfinding:** Render the current A* path (waypoints) for moving entities.
-   **State Labels:** Render text above entities showing their current AI State (e.g., "Idle", "Chasing") and active Behavior Tree node.
-   **Perception:** Draw the Field of View (FOV) cone and lines to current targets.
-   **Navigation Mesh/Grid:** (Optional/Advanced) Visualize the walkable/blocked nodes of the navgrid.

### 5. Camera & Culling
-   **Camera Bounds:** Draw the viewport bounds in world space.
-   **Active Zones:** Visualize the culling margin/active zone where entities are updated.

## Non-Functional Requirements
-   **Performance:** Debug visuals should not significantly degrade performance when disabled. Use efficient batch rendering (lines/primitives).
-   **Isolation:** Debug code should be isolated in separate modules/systems where possible to avoid polluting core game logic.
-   **Toggle-able:** Visuals must be easily toggled at runtime.

## Out of Scope
-   Full-featured in-game editor or level builder.
-   Network debugging tools (game is currently local).
-   Advanced performance profiling graphs (basic frame time is fine).

## Acceptance Criteria
-   [ ] Pressing the debug toggle key (F3) opens/closes the Myra debug panel.
-   [ ] Individual toggles in the panel enable/disable specific debug layers.
-   [ ] Entities and map objects show correct collision shapes when enabled.
-   [ ] Casting a skill displays its expected AoE shape on the ground when enabled.
-   [ ] Moving AI entities show their path and current state label when enabled.

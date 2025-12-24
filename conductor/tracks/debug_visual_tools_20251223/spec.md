# Visual Debug Tools Specification

## Overview

This track implements a comprehensive visual debugging system for the Pomo engine. The goal is to provide real-time, in-game visualizations of invisible game logic (collisions, pathfinding, AI states, etc.) to assist developers in verifying correctness and diagnosing issues.

## Architecture Principles

### Zero-Cost Design
-   **RELEASE builds:** All debug code is compiled out via `#if DEBUG`. Zero runtime cost.
-   **DEBUG builds:** Debug is OFF by default. User must press F3 to toggle on.
-   **Inverted pattern:** `Debug` module is always callable from game code, but functions become no-ops in RELEASE builds.

### Rendering Strategy
-   **Line-based primitives:** Batch lines, tessellate shapes into line segments.
-   **SpriteBatch text:** For text overlays (AI state labels, performance metrics).
-   **Myra UI:** Only for the debug toggle panel.
-   **Multi-camera:** Player-dependent debug (AI, collision) renders per-camera. Global info (FPS, entity count) renders to main overlay only.

---

## Functional Requirements

### 1. Debug Toggle & UI
-   **Debug Mode:** F3 key toggles the debug panel visibility (DEBUG builds only).
-   **Sub-System Toggles:** Checkboxes for each visualization layer:
    -   Show Entity Bounds
    -   Show Map Collisions
    -   Show Spatial Grid
    -   Show AI State/Path
    -   Show Skill AoE
    -   Show Projectiles
    -   Show Effects/Buffs
    -   Show Camera Bounds
-   **UI Framework:** Myra for the toggle panel only.

### 2. Collision & Physics Visualization
-   **Entity Boundaries:** Render bounding circles for all live entities.
-   **Map Objects:** Render collision polygons/polylines for map objects (walls, obstacles).
-   **Spatial Grid:** Overlay the collision grid cells to show spatial partitioning.
-   **Collision Response:** Draw transient MTV (Minimum Translation Vector) lines on collision.

### 3. Skill & Combat Visualization
-   **Skill AoE:** Draw effective area (Circle, Cone, Line) during targeting/casting.
-   **Range Indicators:** Show max range circles for selected skills.
-   **Hit Detection:** Briefly highlight entities/areas that register a "hit".

### 4. AI & Navigation Visualization
-   **Pathfinding:** Render current A* path (waypoints) for moving entities.
-   **State Labels:** Text above entities showing AI State and active decision node.
-   **Perception:** Draw FOV cones and lines to current targets.
-   **Memory (optional):** Visualize AI memory entries and confidence levels.

### 5. Projectile Visualization
-   **Live Trajectories:** Show predicted paths for homing/chain projectiles.
-   **Collision Bounds:** Render projectile hitboxes.
-   **Impact Zones:** Pre-visualize AoE impact areas.

### 6. Effect/Buff Visualization
-   **Active Effects:** Show duration bars, stacks, and refresh timing.
-   **Stat Modifiers:** Display computed stats vs base stats.
-   **Combat Status:** Visual markers for Stunned/Silenced/Rooted.

### 7. Particle/VFX Visualization
-   **Emission Direction:** Visualize velocity/direction vectors.
-   **Spawn Areas:** Show particle spawn areas (distinct from skill AoE).

### 8. Camera & Culling
-   **Camera Bounds:** Draw viewport bounds in world space.
-   **Active Zones:** Visualize the culling margin/active zone for entity updates.

### 9. Performance Overlay
-   **FPS Counter:** Basic frame time display.
-   **Entity Count:** Total entities and entities per scenario.
-   **Event Bus Metrics:** Events per frame, buffer utilization (optional).

---

## Non-Functional Requirements
-   **Zero-cost in RELEASE:** Completely compiled out, no runtime overhead.
-   **Isolation:** Debug code in separate `Debug` module. Call sites remain clean.
-   **Batched rendering:** Use efficient line batching for primitives.
-   **Toggle-able at runtime:** All visuals can be enabled/disabled via F3 panel.

## Out of Scope
-   Full-featured in-game editor or level builder.
-   Network debugging tools (game is currently local).
-   Debug event recording/replay.
-   Debug console (REPL) for runtime commands.

---

## Acceptance Criteria
-   [ ] F3 key opens/closes the Myra debug panel (DEBUG builds only).
-   [ ] Panel contains checkboxes that enable/disable specific debug layers.
-   [ ] Entities and map objects show correct collision shapes when enabled.
-   [ ] Casting a skill displays its expected AoE shape when enabled.
-   [ ] Moving AI entities show their path and current state label when enabled.
-   [ ] Projectiles show trajectories and impact zones when enabled.
-   [ ] Active effects display duration/stack info when enabled.
-   [ ] Performance overlay shows FPS and entity count.
-   [ ] RELEASE builds have zero debug code (verified via build output).

# Pomo 3D Integration Plan

This plan outlines the steps to integrate 3D model rendering into the existing Pomo engine. We will transition from a 2D sprite-based system to a hybrid 2D/3D system (2.5D) using an isometric perspective.

## Phase 1: Data Structures & Domain Logic
**Goal:** Update the core World definition to support 3D visuals and rotation by storing them in the World state.

1.  **Update World State (`Pomo.Core/Domain/World.fs`)**
    *   Add `Rotations: amap<Guid<EntityId>, float32>` to `World` and `MutableWorld`.
    *   Add `ModelIds: amap<Guid<EntityId>, string>` to `World` and `MutableWorld`.
    *   *Verification:* Project builds.

2.  **Update Projection Logic (`Pomo.Core/Projections.fs`)**
    *   Update `MovementSnapshot` struct to include `Rotations: HashMap<Guid<EntityId>, float32>` and `Models: HashMap<Guid<EntityId>, string>`.
    *   Update `ComputeMovementSnapshot` to populate these new fields from the World state.
    *   *Verification:* Manual Check. Add a temporary `printfn` in `Render.fs` to print the rotation/model of the player entity. Run the game; it should print default values (0.0 / "None" or similar).

3.  **Update Camera Domain (`Pomo.Core/Domain/Camera.fs`)**
    *   Introduce `IsometricCamera` type (or update existing `Camera` struct) to include `View` (Matrix) and `Projection` (Matrix).
    *   *Verification:* Project builds.

## Phase 2: Camera System Implementation
**Goal:** Create a camera system that provides the correct matrices for 3D isometric rendering.

4.  **Implement 3D Camera Logic (`Pomo.Core/Systems/CameraSystem.fs`)**
    *   Update `create` to initialize `View` (LookAt) and `Projection` (Orthographic) matrices.
    *   Ensure `View` matrix matches the standard isometric angle (e.g., look from 20,20,20 to 0,0,0).
    *   *Verification:* Manual Check. Run the game with a breakpoint or log in `Render.fs`. Inspect the Camera object passed to Draw. The View and Projection matrices should be valid (not Identity).

## Phase 3: Movement & Orientation
**Goal:** Ensure entities face the direction they are moving.

5.  **Calculate Rotation in Projections (`Pomo.Core/Projections.fs`)**
    *   In `ComputeMovementSnapshot`, calculate rotation angle from the calculated velocity: `atan2(vel.X, vel.Y)`.
    *   This is a *derived* value for now (calculated per frame based on movement), so we don't strictly *need* to store it in World yet, but populating the Snapshot is essential.
    *   *Verification:* Manual Check. Run the game. Move the character left/right/up/down. The `printfn` added in Phase 1 should now show changing rotation values (e.g., 0, 1.57, 3.14).

## Phase 4: Rendering Pipeline Overhaul
**Goal:** Replace the 2D entity rendering with 3D model rendering.

6.  **Refactor Render Service (`Pomo.Core/Systems/Render.fs`)**
    *   **Step 6a:** Inject `ContentManager` to load models.
    *   **Step 6b:** Implement model caching (HashMap<string, Model>) to avoid loading per frame.
    *   **Step 6c:** Remove `SpriteBatch` logic for `DrawPlayer` / `DrawEnemy`.
    *   **Step 6d:** Implement `Draw3D` pass:
        *   Set `DepthStencilState.Default`.
        *   Iterate entities in the Snapshot.
        *   Retrieve Model based on `Models` map (fallback to a default "Dummy" if missing).
        *   Calculate World Matrix: `Scale * RotationY(snapshot.Rotation) * Translation`.
        *   Call `model.Draw(world, view, projection)`.
    *   **Step 6e:** Restore `DepthStencilState.None` after 3D pass (if needed for subsequent UI).
    *   *Verification:* Visual Check. Run the game. You should see 3D models (e.g., generic "Box" or "Dummy") moving around instead of colored rectangles. The models should rotate to face their movement direction.

## Phase 5: Model Assignment
**Goal:** Ensure entities use the correct specific 3D models instead of a generic placeholder.

7.  **Assign Models to Entities**
    *   Locate where entities are spawned (e.g., `EntitySpawner.fs` or `Scenes.fs`).
    *   When creating the entity's initial state in `World`, set the `ModelId` (e.g., "Player" -> "KayKit_Prototype_Bits_1.1_FREE/Assets/obj/Dummy_Base").
    *   *Verification:* Visual Check. The Player should look like the specific player model, and enemies should look like enemy models (if different).

## Phase 6: Polish & Hybrid Layering
**Goal:** Ensure 3D entities sit correctly within the 2D map layers.

8.  **Update Terrain Rendering (`Pomo.Core/Systems/TerrainRender.fs`)**
    *   **Map Structure Review:** Analyze the current map layers (`Terrain`, `Decoration`, `Collision`, `Zones`, `Gameplay`, `Triggers`) to determine the correct rendering order.
    *   The "Gameplay" layer (where entities logically exist) effectively becomes the split point.
    *   Split rendering into "Background" (likely `Terrain`) and "Foreground" (likely `Decoration` - walls, houses, etc. that act as overlays).
    *   Ensure Background draws *before* the 3D entity pass.
    *   Ensure Foreground draws *after* the 3D entity pass.
    *   *Verification:* Visual check. Player should walk "on" the ground (`Terrain`) but render "behind" high objects (`Decoration`). Adjust layer definitions in the map file if necessary to support this split.

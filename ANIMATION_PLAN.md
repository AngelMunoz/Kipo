# Animation System Implementation Plan

This plan details the steps to move from a hardcoded model list to a hierarchical, data-driven animation system.

## Phase 1: Domain & State (The Foundation)

Define the core types for hierarchy and animation data.

- [ ] **Create `Pomo.Core/Domain/Animation.fs`**
    - Define `RigNode` (Struct), `Keyframe` (Struct), `Track`, `AnimationClip`, `AnimationState`.
- [ ] **Update `Pomo.Core/Domain/World.fs`**
    - Add `Poses`: `HashMap<Guid<EntityId>, HashMap<string, Matrix>>`.
    - Add `ActiveAnimations`: `HashMap<Guid<EntityId>, AnimationState IndexList>`.
- [ ] **Verification:** `dotnet build` (Should compile).

## Phase 2: Content Data & Serialization (The Assets)

Refactor data files and update decoders to support Rigs and Animation Clips.

- [ ] **Create `Pomo.Core/Content/Animations.json`**
    - Define the "Test_Windmill" clip.
- [ ] **Update `Pomo.Core/Content/Models.json`**
    - Convert `HumanoidBase` and `Projectile` to the new Rig structure (Map of Nodes).
- [ ] **Update Serialization Logic**
    - Update `ModelStore` / Decoders to parse `HashMap<string, RigNode>` instead of `string[]`.
    - Add decoders for `Animations.json`.
- [ ] **Verification:** `dotnet build` (Should compile).

## Phase 3: The Animation System (The Brain)

Implement the system that calculates rotations based on time.

- [ ] **Create `Pomo.Core/Systems/AnimationSystem.fs`**
    - Implement logic to advance time for active animations.
    - Implement Slerp logic for keyframes.
    - Update `World.Poses` with calculated matrices.
- [ ] **Register System**
    - Add `AnimationSystem` to `CompositionRoot.fs` (or equivalent system registration).
- [ ] **Verification:** `dotnet build`.

## Phase 4: The Renderer Update (The Eyes)

Update the renderer to traverse the hierarchy and apply poses.

- [ ] **Update `Pomo.Core/Systems/Render.fs`**
    - Update `ModelStore` usage to handle the `Rig` type.
    - Implement topological sort or recursive traversal for drawing.
    - Calculate World Matrix: `Local (from Pose) * Parent World`.
- [ ] **Verification:** `dotnet build`.

## Phase 5: Integration & Verification (The Test)

Hook everything up to the Player.

- [ ] **Update `Pomo.Core/Systems/EntitySpawner.fs`**
    - In `finalizeSpawn` for Player, attach `ActiveAnimations` component.
    - Add "Test_Windmill" clip.
- [ ] **Verification:** Run the game. Player's arms should rotate like a windmill.

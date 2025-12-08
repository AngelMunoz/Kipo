# Animation System Implementation Plan

This plan details the steps to move from a hardcoded model list to a hierarchical, data-driven animation system.

## Phase 1: Domain & State (The Foundation) ✅

Define the core types for hierarchy and animation data.

- [x] **Create `Pomo.Core/Domain/Animation.fs`**
  - Define `RigNode` (Struct), `Keyframe` (Struct), `Track`, `AnimationClip`, `AnimationState`.
- [x] **Update `Pomo.Core/Domain/World.fs`**
  - Add `Poses`: `HashMap<Guid<EntityId>, HashMap<string, Matrix>>`.
  - Add `ActiveAnimations`: `HashMap<Guid<EntityId>, AnimationState IndexList>`.
- [x] **Verification:** `dotnet build` (Should compile).

## Phase 2: Content Data & Serialization (The Assets) ✅

Refactor data files and update decoders to support Rigs and Animation Clips.

- [x] **Create `Pomo.Core/Content/Animations.json`**
  - Define `Arm_Swing` clip (arm rotation while running).
  - Define `Run_Bounce` clip (vertical root bobbing while running).
  - Define `Projectile_Spin` clip (spinning projectiles).
- [x] **Update `Pomo.Core/Content/Models.json`**
  - Convert `HumanoidBase` and `Projectile` to the new Rig structure (Map of Nodes).
  - Add `Pivot` properties for rotation correction (e.g., shoulder joints).
- [x] **Update Serialization Logic**
  - Update `ModelStore` / Decoders to parse `HashMap<string, RigNode>` instead of `string[]`.
  - Add decoders for `Animations.json`.
- [x] **Verification:** `dotnet build` (Should compile).

## Phase 3: The Animation System (The Brain) ✅

Implement the system that calculates rotations based on time.

- [x] **Create `Pomo.Core/Systems/AnimationSystem.fs`**
  - Implement logic to advance time for active animations.
  - Implement Slerp logic for keyframes.
  - Update `World.Poses` with calculated matrices.
- [x] **Create `Pomo.Core/Systems/AnimationStateSystem.fs`**
  - Determines which animations to trigger based on game state (e.g., velocity).
  - Currently triggers `Arm_Swing` when moving above speed threshold.
- [x] **Register System**
  - Add `AnimationSystem` and `AnimationStateSystem` to `CompositionRoot.fs`.
- [x] **Verification:** `dotnet build`.

## Phase 4: The Renderer Update (The Eyes) ✅

Update the renderer to traverse the hierarchy and apply poses.

- [x] **Update `Pomo.Core/Systems/Render.fs`**
  - Update `ModelStore` usage to handle the `Rig` type.
  - Implement recursive `resolveNode` traversal for drawing.
  - Calculate World Matrix: `Local (from Pose) * Parent World`.
  - Apply Pivot correction: `Translate(-Pivot) * Rotation * Translate(+Pivot)`.
- [x] **Verification:** `dotnet build`.

## Phase 5: Integration & Verification (The Test) ✅

Hook everything up to the Player.

- [x] **Animation triggers automatically via `AnimationStateSystem`**
  - When player velocity exceeds threshold, `Arm_Swing` animation starts.
  - When player stops, animation is removed.
- [x] **Verification:** Run the game. Player's arms should swing when moving.

---

## Current Animation Clips

| Clip Name         | Duration | Loop | Description                              |
| ----------------- | -------- | ---- | ---------------------------------------- |
| `Arm_Swing`       | 0.8s     | Yes  | Arms swing forward/back while running    |
| `Run_Bounce`      | 0.4s     | Yes  | Vertical bobbing of root while running   |
| `Projectile_Spin` | 1.0s     | Yes  | Spinning rotation for thrown projectiles |

## Future Work

- [x] **Data-Driven Animation Bindings**: Animation clips are now configured per-model in `Models.json`.
- [ ] **Animation Layering**: Play multiple animations simultaneously (e.g., `Arm_Swing` + `Run_Bounce`). ✅ Currently working via IndexList.
- [ ] **Idle Animation**: Define and trigger a default idle pose.
- [ ] **Attack Animations**: Arm animations for skill usage.
- [ ] **Blend Transitions**: Smooth transitions between animation states.

---

## Architecture Notes

### ModelConfig Structure

Each model configuration in `Models.json` now contains:

- **`Rig`**: The bone hierarchy (`HashMap<string, RigNode>`)
- **`AnimationBindings`**: State-to-clips mapping (`HashMap<string, string[]>`)

Example:

```json
{
  "HumanoidBase": {
    "Rig": { ... },
    "AnimationBindings": {
      "Run": ["Arm_Swing", "Run_Bounce"]
    }
  }
}
```

### AnimationStateSystem Flow

1. Fetch entity's `ModelConfigId` from World
2. Look up `ModelConfig` from `ModelStore`
3. Get animation clips from `config.AnimationBindings["Run"]`
4. Trigger those animations when entity is moving

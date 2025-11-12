# Skill System Refactoring Plan

This document outlines the plan for refactoring the skill system to support a wider variety of skill behaviors, including different targeting modes, delivery mechanisms, and areas of effect. The goal is to create a flexible and composable system that can model all desired skill types for both player and AI entities.

## Core Principles

*   **Separation of Concerns**: Clearly distinguish between the player's targeting action, the skill's delivery mechanism, and its area of effect.
*   **Composability**: Allow mixing and matching of targeting, delivery, and area components to create diverse skills.
*   **Leverage Existing Structures**: Utilize and extend existing domain models (`ProjectileInfo`, `ExtraVariations`) where appropriate.

## Data Model Refinements

The following changes will be applied to the domain models, primarily in `Pomo.Core/Domain/Skill.fs` and `Pomo.Core/Domain/Projectile.fs`.

### 1. `Targeting` (Pomo.Core/Domain/Skill.fs)

Defines how the player (or AI) selects the initial point or entity for the skill.

**Current State (already implemented by user):**
```fsharp
[<Struct>]
type Targeting =
  | Self           // No selection needed. Example: An AoE centered on the caster.
  | TargetEntity   // Player selects a single entity.
  | TargetPosition // Player selects a point on the ground.
```

### 2. `SkillArea` (Pomo.Core/Domain/Skill.fs)

Defines the shape and characteristics of the skill's effect at its destination. This is a new field on the `ActiveSkill` record.

**Current State (already implemented by user):**
```fsharp
[<Struct>]
type SkillArea =
  | Point          // Affects only the single point of impact.
  | Circle of radius: float32
  | Cone of angle: float32 * length: float32
  | Line of width: float32 * length: float32
  | MultiPoint of radius: float32 * count: int // For skills like meteor swarm
```

### 3. `Delivery` (Pomo.Core/Domain/Skill.fs)

Defines how the skill's effect travels from the caster to the target area.

**Proposed Change:**
The `Delivery` type will remain simple, focusing on whether the effect is instant or involves a projectile. Multi-projectile launches will be handled by `SkillArea.MultiPoint` or new `ExtraVariations` in `Projectile.fs`.

**Revised `Delivery`:**
```fsharp
[<Struct>]
type Delivery =
  | Instant
  | Projectile of projectile: ProjectileInfo
```
*(Note: The `Melee` case will not be re-added, as `Instant` delivery combined with range checks can cover melee skills.)*

### 4. `ProjectileInfo` (Pomo.Core/Domain/Projectile.fs)

Defines the static properties of a projectile. This will *not* include a direct `Count` field for initial spawning, as multi-projectile behaviors will be handled by `SkillArea.MultiPoint` or new `ExtraVariations`.

**Current State:**
```fsharp
[<Struct>]
type ProjectileInfo = {
  Speed: float32
  Collision: CollisionMode
  Variations: ExtraVariations voption
}
```

### 5. `ExtraVariations` (Pomo.Core/Domain/Projectile.fs)

Defines complex behaviors for individual projectiles. This is where new multi-projectile behaviors (e.g., splitting) could be added if needed.

**Current State:**
```fsharp
[<Struct>]
type ExtraVariations =
  | Chained of jumpsLeft: int * maxRange: float32
  | Bouncing of bouncesLeft: int
```
*(Future consideration: Add `SplitIntoMultiple` here if a single projectile needs to split into many.)*

## Implementation Steps

The following steps will be executed sequentially.

### Step 1: Finalize Data Model in `Pomo.Core/Domain/Skill.fs` and `Pomo.Core/Domain/Projectile.fs`

*   **Action**: Ensure `Pomo.Core/Domain/Skill.fs` reflects the `Targeting`, `SkillArea`, and `Delivery` types as defined above. Specifically, ensure `Delivery` does not contain `MultiProjectile` or `Melee`.
*   **Action**: Ensure `Pomo.Core/Domain/Projectile.fs` `ProjectileInfo` does *not* contain a `Count` field.
*   **Action**: Update JSON decoders in `Pomo.Core/Domain/Skill.fs` and `Pomo.Core/Domain/Projectile.fs` to match the finalized data models.

### Step 2: Update Skill Definitions in `Pomo.Core/Content/Skills.json`

*   **Action**: Modify existing skill definitions to utilize the new `Targeting`, `Delivery`, and `Area` fields. This will serve as a proof of concept and ensure the JSON deserialization works correctly.

### Step 3: Adapt `Pomo.Core/Systems/Targeting.fs`

*   **Action**: Update the logic in `Targeting.fs` to handle the new `Targeting` modes (`Self`, `TargetEntity`, `TargetPosition`).
*   **Action**: Modify `handleTargetSelected` to correctly interpret the selected target based on the skill's `Targeting` and `Area` properties.
*   **Action**: Ensure that `Targeting.fs` correctly initiates the `SystemCommunications.SetMovementTarget` and `CombatEvents.PendingSkillCastSet` events when a target is selected and movement is required.

### Step 4: Adapt `Pomo.Core/Systems/AbilityActivation.fs`

*   **Action**: Modify the `Update` loop to handle `Self` targeting skills by immediately publishing an `AbilityIntent` without entering a targeting mode.
*   **Action**: Update `handleMovementStateChanged` to correctly interpret `SkillArea.MultiPoint` for spawning multiple projectiles if the `Delivery` is `Projectile`.
*   **Action**: Refine the `tryCast` function to consider the `SkillArea` when determining if a skill can be cast (e.g., checking if a `MultiPoint` skill has enough valid points in range).
*   **Action**: Implement logic for `Proximity` targeting (if added as a `Targeting` mode later) to automatically find the nearest valid target.

## Next Steps for Agent

After this plan is approved, the agent will proceed with **Step 1: Finalize Data Model**.
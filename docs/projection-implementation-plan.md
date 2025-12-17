# Projection Implementation Plan

This document tracks projections in `Projections.fs` and the systems that can use them.

---

## 1. `EntityScenarioContext`

**Status:** ✅ USABLE - High Value

Joins `EntityScenario` → `Scenario` → `Map`. Many systems force both maps separately then lookup.

### Systems That Can Use It

| System                  | Current Pattern                          | Usable? |
| ----------------------- | ---------------------------------------- | ------- |
| `Navigation.fs:47-53`   | Forces both, uses `scenario.Map.Key`     | ✅ Yes  |
| `CameraSystem.fs:64-82` | Forces both, uses `TileWidth/TileHeight` | ✅ Yes  |
| `Render.fs:798-799`     | Forces both                              | ✅ Yes  |
| `Collision.fs:271-272`  | Forces both                              | ✅ Yes  |

---

## 2. `EffectOwnerTransform`

**Status:** ✅ USABLE - High Value

Joins ActiveEffects + Position + Velocity + Rotation per entity.

### Systems That Can Use It

| System                      | Current Pattern                              | Usable?        |
| --------------------------- | -------------------------------------------- | -------------- |
| `ParticleSystem.fs:596-599` | Forces 4 maps separately, lookups per-entity | ✅ PERFECT FIT |

---

## Removed Projections

The following were removed as unsuitable:

| Projection                | Reason for Removal                                                                                                    |
| ------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `CombatReadyContext`      | Combat.fs needs HashMap-based lookups across arbitrary entities (targets), not per-entity joined data                 |
| `ProjectileFlightContext` | Semantic mismatch: uses `world.Positions` but systems use `ComputeMovementSnapshot` (velocity-interpolated positions) |
| `MovingEntities`          | UnitMovement also handles Idle→0 velocity transitions, filtering to non-Idle loses that                               |

---

## Next Steps

1. Refactor `Navigation.fs` to use `EntityScenarioContexts`
2. Refactor `ParticleSystem.fs` to use `EffectOwnerTransforms`
3. Consider `CameraSystem.fs`, `Render.fs`, `Collision.fs` for `EntityScenarioContexts`

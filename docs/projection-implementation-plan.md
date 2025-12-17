# Projection Implementation Plan

This document tracks new projections to implement in `Projections.fs` and the systems that will be refactored to use them.

---

## 1. `EntityScenarioContext`

**Priority:** ⭐ Highest

Joins entity with its scenario and map definition. Currently every system does this lookup separately.

### Projection Definition

```fsharp
[<Struct>]
type EntityScenarioContext = {
    ScenarioId: Guid<ScenarioId>
    Scenario: Scenario
    MapKey: string
}

let entityScenarioContexts: amap<Guid<EntityId>, EntityScenarioContext> =
    (world.EntityScenario, world.Scenarios)
    ||> AMap.choose2V (fun _ scenarioId scenarios ->
        scenarioId |> ValueOption.bind (fun sid ->
            scenarios |> HashMap.tryFindV sid |> ValueOption.map (fun s ->
                { ScenarioId = sid; Scenario = s; MapKey = s.Map.Key })))
```

### Systems to Refactor

- [ ] `AISystem.fs` - Lines 1216-1217
- [ ] `Combat.fs` - Line 875
- [ ] `Collision.fs` - Lines 271-272
- [ ] `UnitMovement.fs` - Lines 68, 108
- [ ] `Projectile.fs` - Line 391
- [ ] `Render.fs` - Lines 798-799
- [ ] `Navigation.fs` - Lines 47-48
- [ ] `AbilityActivation.fs` - Lines 330, 375, 455
- [ ] `ActionHandler.fs` - Line 174
- [ ] `PlayerMovement.fs` - Line 100
- [ ] `DebugRender.fs` - Lines 865, 915, 996

---

## 2. `CombatReadyContext`

**Priority:** High

Joins combat-related data: Resources, DerivedStats, Cooldowns, Factions.

### Projection Definition

```fsharp
[<Struct>]
type CombatReadyContext = {
    Resources: Entity.Resource
    DerivedStats: Entity.DerivedStats
    Cooldowns: HashMap<int<SkillId>, TimeSpan>
    Factions: Faction HashSet
}

let combatReadyContexts: amap<Guid<EntityId>, CombatReadyContext> =
    (world.Resources, derivedStats)
    ||> AMap.choose2V (fun entityId res stats ->
        match res, stats with
        | ValueSome r, ValueSome s ->
            let cooldowns =
                world.AbilityCooldowns
                |> AMap.force
                |> HashMap.tryFindV entityId
                |> ValueOption.defaultValue HashMap.empty
            let factions =
                world.Factions
                |> AMap.force
                |> HashMap.tryFindV entityId
                |> ValueOption.defaultValue HashSet.empty
            ValueSome {
                Resources = r
                DerivedStats = s
                Cooldowns = cooldowns
                Factions = factions
            }
        | _ -> ValueNone)
```

> **Note:** This implementation forces Cooldowns/Factions inside the projection.
> A cleaner approach would use `AMap.mapA` with `adaptive {}`.

### Systems to Refactor

- [ ] `Combat.fs` - Lines 897-899
- [ ] `AbilityActivation.fs` - Lines 310, 314, 436
- [ ] `AISystem.fs` - Lines 1243, 1251

---

## 3. `ProjectileFlightContext`

**Priority:** Medium

Joins projectile data with position and animation state.

### Projection Definition

```fsharp
[<Struct>]
type ProjectileFlightContext = {
    Projectile: LiveProjectile
    Position: Vector2
    HasAnimation: bool
}

let projectileFlightContexts: amap<Guid<EntityId>, ProjectileFlightContext> =
    world.LiveProjectiles
    |> AMap.mapA (fun entityId proj -> adaptive {
        let! positions = world.Positions
        let! anims = world.ActiveAnimations

        let position =
            positions
            |> HashMap.tryFindV entityId
            |> ValueOption.defaultValue Vector2.Zero

        let hasAnimation = anims |> HashMap.containsKey entityId

        return {
            Projectile = proj
            Position = position
            HasAnimation = hasAnimation
        }
    })
```

### Systems to Refactor

- [ ] `Projectile.fs` - Lines 390-392
- [ ] `Render.fs` - Line 801

---

## 4. `EffectOwnerTransform`

**Priority:** Medium

Joins effect data with owner's position, velocity, and rotation for particle systems.

### Projection Definition

```fsharp
[<Struct>]
type EffectOwnerTransform = {
    Position: Vector2
    Velocity: Vector2
    Rotation: float32
    Effects: IndexList<ActiveEffect>
}

let effectOwnerTransforms: amap<Guid<EntityId>, EffectOwnerTransform> =
    world.ActiveEffects
    |> AMap.mapA (fun entityId effects -> adaptive {
        let! positions = world.Positions
        let! velocities = world.Velocities
        let! rotations = world.Rotations

        return {
            Position = positions |> HashMap.tryFindV entityId |> ValueOption.defaultValue Vector2.Zero
            Velocity = velocities |> HashMap.tryFindV entityId |> ValueOption.defaultValue Vector2.Zero
            Rotation = rotations |> HashMap.tryFindV entityId |> ValueOption.defaultValue 0.0f
            Effects = effects
        }
    })
```

### Systems to Refactor

- [ ] `ParticleSystem.fs` - Lines 596-599
- [ ] `DebugRender.fs` - Lines 1010-1015 (partially)

---

## 5. `MovingEntities`

**Priority:** Low-Medium

Filters to only entities with non-Idle movement state.

### Projection Definition

```fsharp
let movingEntities: amap<Guid<EntityId>, struct (MovementState * Entity.DerivedStats)> =
    (world.MovementStates, derivedStats)
    ||> AMap.choose2V (fun _ state stats ->
        match state, stats with
        | ValueSome s, ValueSome st when s <> Idle -> ValueSome struct (s, st)
        | _ -> ValueNone)
```

### Systems to Refactor

- [ ] `UnitMovement.fs` - Lines 69-70
- [ ] `Collision.fs` - Line 309 (velocities lookup)

---

## Implementation Notes

### Pattern for 2-way joins

```fsharp
(mapA, mapB) ||> AMap.choose2V (fun key a b -> ...)
```

### Pattern for 3+ way joins

```fsharp
mapA |> AMap.mapA (fun key value -> adaptive {
    let! b = mapB |> AMap.tryFind key
    let! c = mapC |> AMap.tryFind key
    return combine value b c
})
```

### Forcing inside projections

Avoid `AMap.force` inside projection definitions when possible. Use `adaptive {}` with `let!` bindings to maintain reactivity.

---

## Progress Tracking

| Projection              | Defined | Systems Updated |
| ----------------------- | ------- | --------------- |
| EntityScenarioContext   | [x]     | 0/11            |
| CombatReadyContext      | [x]     | 0/3             |
| ProjectileFlightContext | [x]     | 0/2             |
| EffectOwnerTransform    | [x]     | 0/2             |
| MovingEntities          | [x]     | 0/2             |

> **Note:** All projections now use `AMap.tryFind` for per-entity reactivity (stable trunk pattern).
> This ensures that changes to one entity's data only invalidate that entity's projection, not all entities.

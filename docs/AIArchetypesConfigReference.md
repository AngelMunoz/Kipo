# AI Archetypes Configuration Guide

This guide explains how to create enemy behavior patterns in `AIArchetypes.json`.

---

## What is an AI Archetype?

An archetype defines how an enemy thinks and acts - are they aggressive attackers? Cautious guards? Each archetype controls:
- **Personality** - Aggressive, defensive, patrolling
- **Senses** - How far they can see, how wide their vision
- **Reactions** - What they do when they spot a player
- **Combat stats** - How strong they are in a fight

---

## Basic Structure

```json
{
  "Id": 1,
  "Name": "Basic Enemy",
  "BehaviorType": "Aggressive",
  "PerceptionConfig": { ... },
  "CuePriorities": [ ... ],
  "DecisionInterval": 10,
  "BaseStats": { ... }
}
```

---

## Behavior Types

The core personality of the enemy:

| Type | Personality | Good For |
|------|-------------|----------|
| `Aggressive` | Hunts and attacks on sight | Standard combat enemies |
| `Patrol` | Follows a route, investigates disturbances | Guards, sentries |
| `Defensive` | Only attacks when attacked first | Neutral creatures |
| `Passive` | Doesn't fight at all | Civilians, animals |
| `Turret` | Stays in place, attacks from range | Stationary defenses |

---

## Senses (Perception)

How the enemy perceives the world:

```json
"PerceptionConfig": {
  "VisualRange": [16, 10],
  "Fov": 180.0,
  "MemoryDuration": 5.0
}
```

### Visual Range
How far they can see. The two numbers multiply: `16 × 10 = 160` pixels of vision.

### Field of View (Fov)
How wide their vision cone is:
- `360` = Sees in all directions (eyes in the back of their head)
- `180` = Sees forward half (realistic for humans)
- `90` = Narrow tunnel vision (focused, easy to sneak around)

### Memory Duration
How many seconds they "remember" a target after losing sight. Longer memory = harder to escape by hiding.

---

## Reactions (Cue Priorities)

What the enemy does when something happens:

```json
"CuePriorities": [
  {
    "CueType": "Visual",
    "MinStrength": "Weak",
    "Priority": 10,
    "Response": "Engage"
  }
]
```

### What Triggers Them (CueType)

| Trigger | When It Happens |
|---------|-----------------|
| `Visual` | They see a target |
| `Damage` | They take damage |
| `Memory` | They remember a target they lost |

### How Close Matters (MinStrength)

| Strength | Distance | Meaning |
|----------|----------|---------|
| `Weak` | Far away | Barely noticed |
| `Moderate` | Medium distance | Clearly visible |
| `Strong` | Close | Right in front of them |
| `Overwhelming` | Very close | Can't miss them |

Use `MinStrength` to control when they react. A guard might only `Investigate` weak sightings but `Engage` moderate ones.

### What They Do (Response)

| Response | Action |
|----------|--------|
| `Engage` | Attack immediately |
| `Investigate` | Move toward the disturbance |
| `Evade` | Return to safe position |
| `Ignore` | Do nothing |

### Priority (Lower = More Important)
When multiple things happen at once, lower priority numbers win. If `Damage` has priority `5` and `Visual` has priority `10`, getting hit takes precedence over just seeing something.

---

## Combat Stats

How strong the enemy is:

```json
"BaseStats": {
  "Power": 5,
  "Magic": 2,
  "Sense": 10,
  "Charm": 100
}
```

| Stat | Affects |
|------|---------|
| **Power** | Physical attack & defense, dodging |
| **Magic** | Spell damage & magic defense, mana pool |
| **Sense** | Awareness, luck, damage avoidance |
| **Charm** | Health pool, resilience |

Higher numbers = stronger enemy. A basic enemy might have stats around 2-5, while a boss could have 20+.

---

## Decision Interval

```json
"DecisionInterval": 10
```

How often the AI "thinks" (in game ticks). Lower = smarter but uses more processing power. `10` is a good default for normal enemies.

---

## Example: Aggressive Attacker

```json
{
  "Id": 1,
  "Name": "Berserker",
  "BehaviorType": "Aggressive",
  "PerceptionConfig": {
    "VisualRange": [16, 12],
    "Fov": 360.0,
    "MemoryDuration": 10.0
  },
  "CuePriorities": [
    { "CueType": "Visual", "MinStrength": "Weak", "Priority": 10, "Response": "Engage" }
  ],
  "DecisionInterval": 10,
  "BaseStats": { "Power": 8, "Magic": 1, "Sense": 3, "Charm": 150 }
}
```

**Personality**: Sees everything (360° vision), never forgets (10 second memory), attacks anything they spot immediately. High Power for damage, high Charm for health. A straightforward brawler.

---

## Example: Cautious Guard

```json
{
  "Id": 2,
  "Name": "Patrolling Guard",
  "BehaviorType": "Patrol",
  "PerceptionConfig": {
    "VisualRange": [16, 8],
    "Fov": 180.0,
    "MemoryDuration": 5.0
  },
  "CuePriorities": [
    { "CueType": "Visual", "MinStrength": "Moderate", "Priority": 10, "Response": "Investigate" },
    { "CueType": "Damage", "MinStrength": "Weak", "Priority": 5, "Response": "Engage" }
  ],
  "DecisionInterval": 10,
  "BaseStats": { "Power": 5, "Magic": 2, "Sense": 8, "Charm": 100 }
}
```

**Personality**: Can only see forward (180°). Weak sightings are ignored - they only investigate things that are moderately visible. But if they take damage, they immediately fight back (Damage has higher priority with lower number).

---

## Tips

1. **Start with a behavior type** - That sets the foundation for everything else

2. **Tune the Fov for stealth gameplay** - Lower Fov = easier to sneak behind

3. **Use priorities for layered reactions** - `Investigate` > `Engage` makes enemies feel smarter

4. **Memory creates persistence** - Long memory = harder to lose them, short memory = can break line of sight and escape

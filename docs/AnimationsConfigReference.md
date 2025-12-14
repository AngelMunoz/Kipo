# Animations Configuration Guide

This guide explains how to create character animations in `Animations.json`.

---

## What is an Animation?

An animation is a series of poses that play over time - like a flipbook. You define **keyframes** (key poses), and the game smoothly blends between them.

---

## Basic Structure

```json
{
  "Arm_Swing": {
    "Duration": 0.8,
    "Loop": true,
    "Tracks": {
      "Arm_L": [ ... poses ... ],
      "Arm_R": [ ... poses ... ]
    }
  }
}
```

- **Duration**: How long the full animation takes (in seconds)
- **Loop**: Does it repeat forever (`true`) or play once (`false`)?
- **Tracks**: Which body parts move, and how

---

## Keyframes (Poses)

Each keyframe defines a pose at a specific moment:

```json
{ "Time": 0.0, "Rotation": { "X": 0, "Y": -30, "Z": -20 } }
```

- **Time**: When this pose happens (0.0 = start, matches Duration = end)
- **Rotation**: How the part is rotated (in degrees)
- **Position**: Where the part is moved to (optional)

The game automatically creates smooth motion between keyframes.

---

## Understanding Rotation

Rotation values are in degrees. Think of each axis like this:

| Axis | Motion | Example |
|------|--------|---------|
| **Y** | Swing forward/back | Arm reaching forward |
| **Z** | Wave up/down | Arm waving hello |
| **X** | Twist/roll | Arm rotating like a drill |

> [!TIP]
> Not sure which axis does what? Start with small values (like 15-30) on one axis at a time and watch what happens.

---

## Example: Arm Swing (Walking/Running)

```json
"Arm_Swing": {
  "Duration": 0.8,
  "Loop": true,
  "Tracks": {
    "Arm_L": [
      { "Time": 0.0, "Rotation": { "Y": -30, "Z": -20 } },
      { "Time": 0.2, "Rotation": { "Y": 30, "Z": -20 } },
      { "Time": 0.4, "Rotation": { "Y": 30, "Z": 20 } },
      { "Time": 0.6, "Rotation": { "Y": -30, "Z": 20 } },
      { "Time": 0.8, "Rotation": { "Y": -30, "Z": -20 } }
    ],
    "Arm_R": [
      { "Time": 0.0, "Rotation": { "Y": 30, "Z": 20 } },
      { "Time": 0.2, "Rotation": { "Y": -30, "Z": 20 } },
      { "Time": 0.4, "Rotation": { "Y": -30, "Z": -20 } },
      { "Time": 0.6, "Rotation": { "Y": 30, "Z": -20 } },
      { "Time": 0.8, "Rotation": { "Y": 30, "Z": 20 } }
    ]
  }
}
```

**What's happening:**
- Both arms swing, but in opposite directions (when left goes forward, right goes back)
- The motion takes 0.8 seconds and loops forever
- The last keyframe matches the first for a smooth loop

---

## Example: Body Bounce (Running)

```json
"Run_Bounce": {
  "Duration": 0.4,
  "Loop": true,
  "Tracks": {
    "Root": [
      { "Time": 0.0, "Position": { "Y": 0.0 } },
      { "Time": 0.2, "Position": { "Y": 1.0 } },
      { "Time": 0.4, "Position": { "Y": 0.0 } }
    ]
  }
}
```

**What's happening:**
- The whole character (Root) bobs up and down
- Moves up 1 unit at the midpoint, then back down
- Creates that springy running feel

---

## Example: Spinning Object

```json
"Projectile_Spin": {
  "Duration": 1.0,
  "Loop": true,
  "Tracks": {
    "Root": [
      { "Time": 0.0, "Rotation": { "Y": 0 } },
      { "Time": 0.25, "Rotation": { "Y": 90 } },
      { "Time": 0.5, "Rotation": { "Y": 180 } },
      { "Time": 0.75, "Rotation": { "Y": 270 } },
      { "Time": 1.0, "Rotation": { "Y": 360 } }
    ]
  }
}
```

**What's happening:**
- Full 360° rotation over 1 second
- Used for tumbling projectiles (thrown knives, boulders)

---

## Combining Animations

Multiple animations can play at the same time! In `Models.json`:

```json
"AnimationBindings": {
  "Run": ["Arm_Swing", "Run_Bounce"]
}
```

This plays **both** animations together - arms swing while the body bounces.

---

## Tips for Creating Animations

1. **Start and end the same** - For looping animations, make the last keyframe match the first

2. **Use 3-5 keyframes** - More keyframes = more control, but also more work. Start simple.

3. **Test small values first** - Try 15-30 degree rotations before going bigger

4. **Mirror for opposite limbs** - Left arm forward (+30) when right arm back (-30)

5. **Match duration to action speed** - Fast actions (attacks) = short duration. Slow actions (walking) = longer duration

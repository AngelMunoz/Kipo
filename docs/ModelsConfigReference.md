# Models Configuration Guide

This guide explains how to set up 3D character rigs in `Models.json`.

---

## What is a Model Config?

A model config defines how a 3D character is assembled from separate body parts, and which animations play when the character moves. Think of it like a digital puppet - you're defining where each limb connects and how it moves.

---

## Basic Structure

```json
{
  "HumanoidBase": {
    "Rig": {
      "Root": { "Model": "Body_Mesh" },
      "Arm_L": { "Model": "LeftArm_Mesh", "Parent": "Root" }
    },
    "AnimationBindings": {
      "Run": ["Arm_Swing"]
    }
  }
}
```

- **Rig**: The skeleton - which body parts exist and how they connect
- **AnimationBindings**: Which animations play for different actions

---

## Building the Rig

Each body part is a **node** with these properties:

### Model (required)
The 3D mesh file to use for this body part.
```json
"Model": "Dummy_Base_Dummy_Body_Dummy_ArmLeft"
```

### Parent (optional)
Which body part this attaches to. Omit for the root/base.
```json
"Parent": "Body"
```

When a parent moves, all its children move with it. So if the body leans, the arms and head lean too.

### Pivot (important for joints!)
The point that the part rotates around.

```json
"Pivot": { "X": 0.0, "Y": 1.5, "Z": 0.0 }
```

> [!IMPORTANT]
> **Why pivots matter**: Our character models have their origin point at the feet (the floor). Without a pivot, rotating an arm would swing it around the floor like a propeller! The pivot tells the system "rotate around the shoulder joint instead."

For humanoid characters, arm pivots are typically around `Y: 1.5` (shoulder height).

### Offset (optional)
Extra positioning adjustment from the parent.
```json
"Offset": { "X": 0.0, "Y": 0.0, "Z": 0.0 }
```

---

## Animation Bindings

These tell the system which animations to play for each action state.

```json
"AnimationBindings": {
  "Run": ["Arm_Swing", "Run_Bounce"]
}
```

- **Run**: When the character is moving, play these animations
- Multiple animations can play at once (arms swinging + body bouncing)

Animation names reference clips defined in `Animations.json`.

---

## Example: Humanoid Character

```json
"HumanoidBase": {
  "Rig": {
    "Root": { "Model": "Dummy_Base" },
    "Body": {
      "Model": "Dummy_Base_Dummy_Body",
      "Parent": "Root"
    },
    "Head": {
      "Model": "Dummy_Base_Dummy_Body_Dummy_Head",
      "Parent": "Body"
    },
    "Arm_L": {
      "Model": "Dummy_Base_Dummy_Body_Dummy_ArmLeft",
      "Parent": "Body",
      "Pivot": { "X": 0.0, "Y": 1.5, "Z": 0.0 }
    },
    "Arm_R": {
      "Model": "Dummy_Base_Dummy_Body_Dummy_ArmRight",
      "Parent": "Body",
      "Pivot": { "X": 0.0, "Y": 1.5, "Z": 0.0 }
    }
  },
  "AnimationBindings": {
    "Run": ["Arm_Swing", "Run_Bounce"]
  }
}
```

**Reading the hierarchy:**
- `Root` is the base (no parent)
- `Body` attaches to `Root`
- `Head`, `Arm_L`, `Arm_R` all attach to `Body`

When the body moves, the head and arms follow. Arms have pivots at shoulder height for natural joint rotation.

---

## Example: Simple Prop (Projectile)

For objects that don't need complex rigs:

```json
"Barrel_B": {
  "Rig": {
    "Root": { "Model": "Barrel_B" }
  },
  "AnimationBindings": {
    "Spin": ["Projectile_Spin"]
  }
}
```

Just one part, one spin animation. Used for thrown projectiles like boulders.

---

## Tips

1. **Keep node names consistent** - Use the same names (`Arm_L`, `Arm_R`, `Head`) across characters so animations can be shared

2. **Test pivots visually** - If a limb rotates weirdly, the pivot is probably wrong. Adjust Y until it looks natural

3. **Start simple** - Build a basic rig first, then add complexity as needed

# Items Configuration Guide

This guide explains how to create equipment and consumables in `Items.json`.

---

## What is an Item?

Items are things characters can carry, wear, or use. There are three types:
- **Equipment** - Armor, weapons, accessories that boost stats when worn
- **Consumables** - Potions and scrolls that have one-time effects
- **Materials** - Crafting ingredients, quest items, junk

---

## Basic Structure

```json
{
  "1": {
    "Id": 1,
    "Name": "Simple Sword",
    "Weight": 5,
    "Kind": { ... }
  }
}
```

- **Id**: Unique number (must match the key)
- **Name**: What players see
- **Weight**: How heavy it is (affects inventory limits)
- **Kind**: What type of item and what it does

---

## Equipment (Wearable Items)

Things characters can equip for stat bonuses.

```json
"Kind": {
  "Type": "Wearable",
  "Slot": "Weapon",
  "Stats": [
    { "Type": "Additive", "Stat": "AP", "Value": 5.0 }
  ]
}
```

### Equipment Slots

| Slot | Description |
|------|-------------|
| `Head` | Helmets, hats, crowns |
| `Chest` | Armor, robes, shirts |
| `Legs` | Pants, greaves, skirts |
| `Feet` | Boots, shoes, sandals |
| `Hands` | Gloves, gauntlets, bracers |
| `Weapon` | Swords, staffs, bows |
| `Shield` | Shields, bucklers |
| `Accessory` | Rings, necklaces, cloaks |

### Stat Bonuses

Each stat bonus needs:
- **Type**: `Additive` (adds flat number) or `Multiplicative` (percentage increase)
- **Stat**: Which stat to boost
- **Value**: How much

**Common Stats:**
| Stat | What It Does |
|------|--------------|
| `AP` | Attack Power - physical damage |
| `MA` | Magic Attack - spell damage |
| `AC` | Armor Class - physical defense |
| `MD` | Magic Defense - spell resistance |
| `MS` | Movement Speed |
| `HP` | Health Points |
| `MP` | Mana Points |
| `HPRegen` | Health regeneration per second |
| `MPRegen` | Mana regeneration per second |

---

## Consumables (Usable Items)

One-time use items like potions and scrolls.

```json
"Kind": {
  "Type": "Usable",
  "Effect": {
    "Name": "Health Potion",
    "Kind": "ResourceOverTime",
    "Duration": { "Type": "Instant" },
    "Modifiers": [
      { "Type": "ResourceChange", "Resource": "HP", "Amount": "50" }
    ]
  }
}
```

The `Effect` works just like skill effects - you can create:
- Instant heals
- Healing over time
- Temporary buffs
- Damage effects (harmful potions?)

See the [Skills Guide](SkillsConfigReference.md) for full effect options.

---

## Materials (Non-Usable Items)

Items that just take up inventory space.

```json
"Kind": { "Type": "NonUsable" }
```

Perfect for:
- Crafting ingredients
- Quest items
- Vendor trash
- Collectibles

---

## Examples

### Warrior Sword
```json
{
  "Id": 1,
  "Name": "Iron Sword",
  "Weight": 5,
  "Kind": {
    "Type": "Wearable",
    "Slot": "Weapon",
    "Stats": [
      { "Type": "Additive", "Stat": "AP", "Value": 10.0 }
    ]
  }
}
```

### Wizard Hat (Multiple Stats)
```json
{
  "Id": 4,
  "Name": "Wizard Hat",
  "Weight": 2,
  "Kind": {
    "Type": "Wearable",
    "Slot": "Head",
    "Stats": [
      { "Type": "Additive", "Stat": "MA", "Value": 15.0 },
      { "Type": "Additive", "Stat": "MPRegen", "Value": 2.0 }
    ]
  }
}
```

### Instant Health Potion
```json
{
  "Id": 10,
  "Name": "Health Potion",
  "Weight": 1,
  "Kind": {
    "Type": "Usable",
    "Effect": {
      "Name": "Heal",
      "Kind": "ResourceOverTime",
      "Stacking": { "Type": "NoStack" },
      "Duration": { "Type": "Instant" },
      "Modifiers": [
        { "Type": "ResourceChange", "Resource": "HP", "Amount": "50" }
      ]
    }
  }
}
```

### Regeneration Potion (Heal Over Time)
```json
{
  "Id": 11,
  "Name": "Troll's Blood",
  "Weight": 1,
  "Kind": {
    "Type": "Usable",
    "Effect": {
      "Name": "Regeneration",
      "Kind": "ResourceOverTime",
      "Stacking": { "Type": "RefreshDuration" },
      "Duration": { "Type": "Loop", "Interval": 1.0, "Duration": 10.0 },
      "Modifiers": [
        { "Type": "ResourceChange", "Resource": "HP", "Amount": "5" }
      ]
    }
  }
}
```
*Heals 5 HP every second for 10 seconds = 50 HP total, but spread out.*

### Simple Rock (Material)
```json
{
  "Id": 99,
  "Name": "Rock",
  "Weight": 1,
  "Kind": { "Type": "NonUsable" }
}
```

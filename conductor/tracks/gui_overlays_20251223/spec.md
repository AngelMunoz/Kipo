# GUI Overlays Specification

## Overview

This track establishes polished, player-focused GUI overlays that feel premium and responsive. The goal is to create a **delightful player experience** — not just data on screen, but an interface that communicates information intuitively through visual hierarchy, animation, and careful attention to detail.

> [!IMPORTANT]
> **UX First:** Every component should prioritize clarity, polish, and player "feel" over raw data display. Players are delicate — they notice when UI feels sluggish, cluttered, or cheap.

## Design Principles

1. **Immediate Feedback** — All player actions should have instant visual acknowledgment
2. **Progressive Disclosure** — Show essential info at a glance, details on hover/focus
3. **Visual Hierarchy** — Critical info (HP, current ability) should dominate; secondary info recedes
4. **Smooth Transitions** — Animate value changes, state transitions, and visibility changes
5. **Consistent Language** — Use color, iconography, and layout consistently across all components

## Functional Requirements

### 1. Player Vitals Panel

A compact, elegant display of the player's survival resources.

- **Health Globe/Bar** (Red/Orange gradient)
  - Smooth animated fill on damage/healing (not instant jumps)
  - Flash/pulse effect on critical damage
  - Low health warning state (pulsing glow, color shift)
- **Mana/Resource Bar** (Blue/Purple gradient)
  - Cost preview: dim the portion that would be consumed when hovering a skill
- **Numerical Overlay** — Current/Max values, but subtle (don't dominate the visual)
- **Regeneration Indicators** — Subtle tick animation when HP/MP is regenerating

### 2. Action Bar

The player's primary interaction point for abilities.

> [!NOTE]
> **Data Source Constraints:** Action sets come from `world.ActionSets` keyed by entity → set index → `HashMap<GameAction, SlotProcessing>`. Each slot can be a `Skill` or `Item`. The active set is in `world.ActiveActionSets`.

- **Slots (6 per set)** — UseSlot1 through UseSlot6 mapped to 1-6 keys
- **Skill Display:**
  - Skill initials (e.g., "FB" for Fireball) — extracted from `SkillStore`
  - Background color tint based on skill intent (Offensive = red-ish, Supportive = green-ish)
- **Item Display:**
  - Item name abbreviation
  - Uses remaining indicator (if consumable)
- **Cooldown Visualization:**
  - Radial sweep overlay (clock-wipe effect) during cooldown
  - Remaining seconds text when > 1s
- **Keybinding Labels** — Small "1", "2", etc. in corner
- **Hover Tooltip** — Full skill/item details (see Tooltips section)
- **Set Switcher** — Indicator showing current action set (1/2/3), switchable via Tab or scroll

### 3. Status Effects Bar (Buffs/Debuffs)

Clear visual communication of active effects on the player.

- **Source:** `world.ActiveEffects` → `IndexList<ActiveEffect>`
- **Visual Representation:**
  - Colored squares/circles with effect kind indicator (buff = green border, debuff = red, DoT = orange pulse)
  - Stack count badge when `StackCount > 1`
- **Duration Indicator:**
  - Radial timer sweep (like cooldowns)
  - Flash when about to expire (< 3s remaining)
- **Grouping:** Buffs on left, debuffs on right, sorted by remaining duration
- **Hover:** Effect name, source, time remaining, and description

### 4. Target Frame

Information about the currently selected entity.

- **Appears:** Only when an entity is selected (via `Selection` state)
- **Health Bar** — Same style as player vitals, scaled to target
- **Name Placeholder** — Until entity names are implemented, show faction/type or "Target"
- **Faction Indicator** — Color coding (Enemy = red outline, Ally = green, Neutral = gray)
- **Active Effects Summary** — Small icons showing target's active buffs/debuffs

### 5. Cast Bar

Feedback during skill charging/casting.

- **Source:** `world.ActiveCharges`
- **Progress Bar** — Smooth fill animation
- **Skill Name Label** — Name of the skill being cast
- **Interruptible Indicator** — Visual distinction if the cast can be interrupted
- **Position:** Center-bottom of screen, prominent during casting

### 6. Floating Combat Text

World-space feedback for damage, healing, and status.

- **Source:** `world.Notifications` (WorldText entries)
- **Styling:**
  - Damage = Red, scales with magnitude (crits are larger/bolder)
  - Healing = Green
  - Status (Miss, Resist) = Gray/White
- **Animation:** Float upward, fade out, slight horizontal drift for variety
- **Projection:** World position → screen coordinates via camera

### 7. Combat Indicator

Ambient awareness of combat state.

- **Source:** `world.InCombatUntil > world.Time.TotalGameTime`
- **Visual:** Subtle screen-edge vignette or frame glow (red tint)
- **Transition:** Fade in/out smoothly, not instant toggle

### 8. Character Sheet Panel

A dedicated overlay for detailed character information (toggle via hotkey).

- **Sections:**
  - **Base Stats:** Power, Magic, Sense, Charm — with styled labels
  - **Derived Stats:** Grouped logically:
    - Combat: AP, AC, DX (Power-derived)
    - Magic: MP (max), MA, MD (Magic-derived)
    - Perception: WT, DA, LK (Sense-derived)
    - Defense: HP (max), DP, HV (Charm-derived)
    - Movement: MS
    - Regen: HPRegen, MPRegen
  - **Elemental Info:** Resistances and attributes (if any)
- **UX:** Presented in a clean, scannable layout — not a wall of text
- **Toggle:** Open/close with a hotkey (e.g., C for Character)

### 9. Equipment Panel

Visual representation of the player's equipped items.

- **Source:** `world.EquippedItems` → `HashMap<Slot, Guid<ItemInstanceId>>`
- **Slots Display:**
  - Head, Chest, Legs, Feet, Hands, Weapon, Shield, Accessory
  - Visual "paper doll" or slot grid layout
  - Empty slots clearly indicated
- **Item Display:**
  - Item name (from `ItemStore` via `world.ItemInstances`)
  - Slot-appropriate placeholder icon
- **Hover:** Item details and stats (via Tooltips)
- **Integration:** Can be part of Character Sheet panel or standalone

### 10. Tooltips System

Consistent, informative tooltips across all interactive elements.

- **Skill Tooltip:**
  - Name, Description
  - Cost (MP/HP), Cooldown, Cast Time
  - Range, Area type
  - Effects summary (with durations)
- **Item Tooltip:**
  - Name, Weight
  - Equipment stats (if Wearable)
  - Uses remaining (if consumable)
- **Effect Tooltip:**
  - Name, Kind (Buff/Debuff/DoT)
  - Time remaining
  - Description of what it does
- **Timing:** Slight delay before showing (~200ms), instant hide on mouseout
- **Position:** Smart positioning to avoid screen edges

### 11. Mini-Map

Spatial awareness overlay.

- **Position:** Corner of screen (configurable)
- **Content:**
  - Simplified map geometry (walls/floors)
  - Player dot (Green, centered or tracked)
  - Enemy dots (Red)
  - Ally dots (Blue)
- **Scale:** Appropriate zoom level for useful awareness

## Infrastructure & Architecture

- **Component-Based:** Each HUD element is a self-contained Myra widget
- **Reactive Binding:** Components subscribe to FDA values — updates only on change
- **HUD Layout Config:** JSON/F# record defining positions and anchor points
- **Theme System:** Colors, fonts, textures abstracted into a Theme record
- **Animation Support:** Built-in easing/tweening for smooth transitions

## Non-Functional Requirements

- **Performance:** Zero GC pressure from UI updates; reactive push, not polling
- **Resolution Scaling:** Anchoring system handles different screen sizes
- **60 FPS:** UI updates never block or stutter the game loop

## Acceptance Criteria

- [ ] Player vitals animate smoothly on HP/MP changes
- [ ] Action bar correctly displays skills/items from the active action set
- [ ] Action bar shows accurate cooldown progress
- [ ] Status effects bar displays active buffs/debuffs with duration
- [ ] Target frame appears on entity selection with health bar
- [ ] Cast bar shows progress during skill charging
- [ ] Combat text floats at correct world positions
- [ ] Character sheet displays all base and derived stats clearly
- [ ] Equipment panel shows all 8 equipped slots
- [ ] Tooltips provide detailed info on hover
- [ ] Mini-map shows player and nearby entities
- [ ] All components use theme system for styling

## Out of Scope

- In-game UI editor (drag-and-drop customization)
- Multiple themes (only default required)
- XP/Leveling UI (not yet in core)
- Entity names display (infrastructure not ready)

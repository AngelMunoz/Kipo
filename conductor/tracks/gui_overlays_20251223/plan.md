# Implementation Plan - GUI Overlays

> [!IMPORTANT]
> **UX-First Approach:** Every component prioritizes player experience. Smooth animations, clear visual hierarchy, and polished feedback — not just data on screen.

> [!NOTE]
> **Deliverable Structure:** Each phase is a **standalone PR/merge unit**. A single PR does not require completing all features — it should be a workable, testable increment. Phase 1 establishes foundations; subsequent phases can be delivered incrementally across multiple sessions and PRs as needed.

## Phase 1: Infrastructure & Theming

- [x] Task: Define `HUDTheme` and `HUDLayout` types
    - Create a module for UI definitions (`Pomo.Core/Domain/UI.fs` or similar)
    - `HUDTheme`: Record with color palette (gradients for HP/MP), fonts, texture region names
    - `HUDLayout`: Record defining position, anchor, and visibility for each HUD component
- [x] Task: Create `HUDService`
    - Service to load/manage current layout and theme at runtime
    - Expose reactive observables for theme/layout changes
    - Provide helpers for common operations (color by faction, easing functions)
- [x] Task: Implement Animation/Transition Utilities
    - Simple easing functions (ease-out for health changes, linear for cooldowns)
    - Value interpolation helpers for smooth bar fills
- [ ] Task: Conductor - User Manual Verification 'Phase 1: Infrastructure & Theming' (Protocol in workflow.md)

---

## Phase 2: Core HUD Components

### 2.1 Player Vitals

- [x] Task: Implement `PlayerVitals` Component
    - Myra widget (VerticalStack or custom panel)
    - Bind to `world.Resources` for local player entity
    - Animated fill bars (not instant jumps) for HP/MP
    - Low health warning state (pulsing glow when HP < 25%)
    - Numerical overlay showing Current/Max

### 2.2 Action Bar

- [x] Task: Implement `ActionBar` Component
    - Horizontal row of 6 slots (UseSlot1-6)
    - **Data binding:**
      - `world.ActionSets[entityId]` → `HashMap<int, HashMap<GameAction, SlotProcessing>>`
      - `world.ActiveActionSets[entityId]` → current set index
      - `world.AbilityCooldowns[entityId]` → cooldown timestamps
    - Per-slot display:
      - **Skill:** Extract initials from SkillStore (e.g., "FB" for Fireball), tint by intent
      - **Item:** Show item abbreviation + uses remaining
    - Cooldown overlay: Radial sweep + seconds remaining text
    - Keybinding labels in corner ("1", "2", etc.)
- [x] Task: Implement Action Set Switcher
    - Visual indicator showing current set (1/2/3)
    - Allow switching via Tab or defined keybind

### 2.3 Status Effects Bar (Buffs/Debuffs)

- [x] Task: Implement `StatusEffectsBar` Component
    - Bind to `world.ActiveEffects[entityId]` → `IndexList<ActiveEffect>`
    - Display as row of colored squares/circles
    - Color coding by EffectKind: Buff=green border, Debuff=red, DoT=orange pulse
    - Stack count badge when `StackCount > 1`
    - Radial duration timer (like cooldowns)
    - Flash animation when < 3s remaining
    - Sort: Buffs left, Debuffs right, by remaining duration

### 2.4 Target Frame

- [x] Task: Implement `TargetFrame` Component
    - Bind to current selection state (from `CursorSystem` or targeting service)
    - Display target health bar (same style as player vitals)
    - Faction indicator (color-coded outline)
    - Name placeholder: Show "Target" or faction type until entity names exist
    - Small summary of target's active effects (icons only)

### 2.5 Cast Bar

- [x] Task: Implement `CastBar` Component
    - Bind to `world.ActiveCharges[entityId]`
    - Smooth progress bar fill animation
    - Skill name label (lookup from SkillStore via SkillId)
    - Position: Center-bottom, prominent during casting

- [ ] Task: Conductor - User Manual Verification 'Phase 2: Core HUD Components' (Protocol in workflow.md)

---

## Phase 3: World-Space & Secondary Overlays

### 3.1 Floating Combat Text

- [ ] Task: Implement `FloatingCombatText` System
    - Subscribe to `world.Notifications` (WorldText entries)
    - Project world positions → screen coordinates via `CameraService`
    - Styling: Damage=Red (scale by magnitude), Healing=Green, Status=Gray
    - Animation: Float up, fade out, slight horizontal drift
    - Render via SpriteBatch integrated into UI layer

### 3.2 Combat Indicator

- [ ] Task: Implement `CombatIndicator`
    - Bind to `world.InCombatUntil > world.Time.TotalGameTime`
    - Visual: Subtle screen-edge vignette or frame glow (red tint)
    - Smooth fade in/out transitions

### 3.3 Mini-Map

- [ ] Task: Implement `MiniMap` Component
    - Custom Myra widget rendering simplified `Scenario.Map`
    - Render entity dots from `world.Positions`:
      - Player = Green (centered)
      - Enemy = Red
      - Ally = Blue
    - Appropriate zoom level for spatial awareness

- [ ] Task: Conductor - User Manual Verification 'Phase 3: World-Space & Secondary Overlays' (Protocol in workflow.md)

---

## Phase 4: Detail Panels

### 4.1 Character Sheet

- [ ] Task: Implement `CharacterSheet` Panel
    - Toggle visibility via hotkey (e.g., 'C')
    - **Base Stats Section:** Power, Magic, Sense, Charm
      - Bind to `world.BaseStats[entityId]`
    - **Derived Stats Section:** Logically grouped
      - Combat (AP, AC, DX), Magic (MP max, MA, MD), Perception (WT, DA, LK)
      - Defense (HP max, DP, HV), Movement (MS), Regen (HPRegen, MPRegen)
      - Bind to `world.DerivedStats[entityId]` (or `Projections.DerivedStats`)
    - **Elemental Info:** Resistances and attributes (if any)
    - Clean, scannable layout with visual grouping — not a wall of text

### 4.2 Equipment Panel

- [ ] Task: Implement `EquipmentPanel` Component
    - Bind to `world.EquippedItems[entityId]` → `HashMap<Slot, Guid<ItemInstanceId>>`
    - 8 slots: Head, Chest, Legs, Feet, Hands, Weapon, Shield, Accessory
    - Display item names (resolve via `world.ItemInstances` + `ItemStore`)
    - Empty slots clearly indicated (dashed border or "Empty" text)
    - Slot grid layout or "paper doll" visual
    - Can be integrated into Character Sheet or standalone

### 4.3 Tooltips System

- [ ] Task: Implement Tooltip Infrastructure
    - Shared tooltip rendering service
    - Smart positioning (avoid screen edges)
    - Slight hover delay (~200ms), instant hide on mouseout
- [ ] Task: Implement Skill Tooltips
    - Name, Description, Cost, Cooldown, Cast Time, Range, Area, Effects
    - Source: `SkillStore.find(skillId)` → `ActiveSkill`
- [ ] Task: Implement Item Tooltips
    - Name, Weight, Equipment stats (if Wearable), Uses remaining
    - Source: `ItemStore.find(itemId)` → `ItemDefinition`
- [ ] Task: Implement Effect Tooltips
    - Name, Kind, Time remaining, Description
    - Source: `ActiveEffect.SourceEffect`

- [ ] Task: Conductor - User Manual Verification 'Phase 4: Detail Panels' (Protocol in workflow.md)

---

## Phase 5: Final Integration

- [ ] Task: Assemble `GameplayHUD`
    - Combine all components into main HUD panel using `HUDLayout`
    - Integrate into existing `UISystem.fs` or create new `HUDSystem.fs`
- [ ] Task: Resolution Scaling Verification
    - Ensure all elements scale correctly with different resolutions
    - Verify anchoring works at 1080p, 1440p, 4K
- [ ] Task: Performance Profiling
    - Verify adaptive updates don't cause stuttering or allocation spikes
    - Confirm UI layer maintains 60 FPS
- [ ] Task: Conductor - User Manual Verification 'Phase 5: Final Integration' (Protocol in workflow.md)

---

## Data Source Reference

| Component         | Primary Data Source                                           |
|-------------------|---------------------------------------------------------------|
| Player Vitals     | `world.Resources[entityId]`                                   |
| Action Bar        | `world.ActionSets`, `world.ActiveActionSets`, `world.AbilityCooldowns` |
| Status Effects    | `world.ActiveEffects[entityId]`                               |
| Target Frame      | Selection state + target's `Resources`, `Factions`, `ActiveEffects` |
| Cast Bar          | `world.ActiveCharges[entityId]`                               |
| Combat Text       | `world.Notifications`                                         |
| Combat Indicator  | `world.InCombatUntil` vs `world.Time`                         |
| Character Sheet   | `world.BaseStats`, `world.DerivedStats` (or `Projections.DerivedStats`) |
| Equipment Panel   | `world.EquippedItems`, `world.ItemInstances`, `ItemStore`     |
| Mini-Map          | `world.Positions`, `Scenario.Map`                             |

## Action Bar Constraints (from EntitySpawner.fs)

```fsharp
// Action sets structure:
world.ActionSets: cmap<Guid<EntityId>, HashMap<int, HashMap<GameAction, SlotProcessing>>>
//                                     ^set index    ^GameAction   ^Skill or Item

// SlotProcessing:
type SlotProcessing =
  | Skill of int<SkillId>
  | Item of Guid<ItemInstanceId>

// Active set:
world.ActiveActionSets: cmap<Guid<EntityId>, int>  // current set index

// Typical setup (from createPlayerLoadout):
// - Set 1: Skills (UseSlot1-6 → Skill IDs)
// - Set 2: Items (UseSlot1-2 → Item instance IDs)
// - Set 3: More skills (UseSlot1-6 → Different skill IDs)
```

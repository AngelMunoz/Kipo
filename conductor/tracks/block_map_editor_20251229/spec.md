# 3D Block Map Editor - Specification

## Overview

Built-in WYSIWYG 3D block map editor using a **True 3D** world architecture. The isometric camera is just one view into a fully 3D world, enabling future expansion to first-person modes, cinematic cutscenes, and more.

---

## Core Architecture Decision

> [!IMPORTANT]
> **The camera is just a view.** The world is fully 3D with `Vector3` positions, 3D collision, and 3D pathfinding. The isometric projection is the default gameplay camera, but the engine supports any camera mode.

```
┌─────────────────────────────────────┐
│         3D WORLD STATE              │
│  (Vector3 positions, 3D collision)  │
└───────────────┬─────────────────────┘
                │
    ┌───────────┼───────────┐
    │           │           │
    ▼           ▼           ▼
┌────────┐ ┌────────┐ ┌────────┐
│Isometric│ │1st Pers│ │Cutscene│
│ Camera  │ │ Camera │ │ Camera │
└────────┘ └────────┘ └────────┘
```

---

## Functional Requirements

### 1. World Position System
- **WorldPosition**: Core coordinate type replacing `Vector2`
  ```fsharp
  [<Struct>]
  type WorldPosition = { X: float32; Y: float32; Z: float32 }
  ```
- **Y-axis**: Vertical height (elevation)
- **X/Z plane**: Ground plane (maps to current X/Y in isometric view)

### 2. Editor Scene
- **Separate Scene**: Standalone editor (like MainMenu)
- **Block Placement**: Click grid to place blocks in 3D space
- **Block Removal**: Right-click removes blocks
- **Layer Navigation**: Change active Y-level (height)
- **Grid Overlay**: Visual grid on current Y-plane
- **Cursor Preview**: Ghost block at hovered cell

### 3. Camera System
- **Isometric Camera**: Default orthographic projection (existing)
- **Free-Fly Camera**: Optional for detailed 3D placement
- **Camera Abstraction**: `CameraService` supports multiple projection modes

### 4. Block Palette
- **Self-Contained**: Palette embedded in map file
- **Categories**: Terrain, Decoration, Structure
- **Block Properties**: Name, Model path, IsSolid, CanStack

### 5. 3D Collision System
- **Dual-strategy**: Fast AABB for regular blocks, mesh collision for slopes
- **CollisionType per BlockType**: `Box | Mesh | NoCollision`
- **3D Spatial Grid**: Extends current 2D grid to 3D (`GridCell3D`)
- **Mesh Collision**: For rotated slope blocks, ray-surface intersection

### 6. 3D Pathfinding
- **3D Navigation Grid**: Walkable cells with height transitions
- **Ramps/Stairs**: Transition blocks between Y-levels
- **A* with Height**: Path considers vertical movement

### 7. Preview Mode
- **Play Button**: Test map with player entity
- **Spawn Point**: Designated block for player spawn
- **Stop Button**: Return to editor, map intact

### 8. Persistence
- **JSON Format**: JDeck encoders/decoders
- **Schema Version**: `Version: int` field for future migrations
- **Self-Contained Palette**: Block types in map file
- **Sparse Storage**: Only placed blocks stored

---

## Data Structures

### WorldPosition
```fsharp
[<Struct>]
type WorldPosition = { X: float32; Y: float32; Z: float32 }
```

### GridCell3D
```fsharp
[<Struct>]
type GridCell3D = { X: int; Y: int; Z: int }  // Y = height
```

### BlockMap
```fsharp
[<Struct>]
type CollisionType =
  | Box              // Fast AABB check
  | Mesh of string   // Collision mesh path for slopes
  | NoCollision      // Decorations, passthrough

[<Struct>]
type PlacedBlock = {
  Cell: GridCell3D
  BlockTypeId: int<BlockTypeId>
  Rotation: Quaternion voption  // None = no rotation, Some = arbitrary 3D
}

type BlockType = {
  Id: int<BlockTypeId>
  Name: string
  Model: string
  Category: string
  CollisionType: CollisionType
}

type BlockMapDefinition = {
  Version: int  // Schema version for migrations
  Key: string
  Width: int; Height: int; Depth: int
  Palette: Dictionary<int<BlockTypeId>, BlockType>
  Blocks: Dictionary<GridCell3D, PlacedBlock>
  SpawnCell: GridCell3D voption
}
```

### Editor State (FDA Reactive)
```fsharp
type EditorState = {
  CurrentBlockTypeId: cval<int<BlockTypeId> voption>
  CurrentLayer: cval<int>
  BrushMode: cval<BrushMode>
  ShowGrid: cval<bool>
  GridCursor: cval<GridCell3D voption>
}
```

---

## Performance & GC Requirements

> [!WARNING]
> All new code follows existing codebase patterns for zero-allocation hot paths.

- `[<Struct>]` on all DUs and small records
- `voption` over `Option`
- `Dictionary<K,V>` over F# `Map` for mutable lookups
- `ArrayPool<T>.Shared` for temp arrays
- No allocations in Update/Draw after warmup

---

## Migration Strategy

### Phase 1: Position Migration
- Replace `Vector2` position storage with `WorldPosition`
- Initially, all entities have `Y = 0`
- Existing systems continue working (2D = XZ plane at Y=0)

### Phase 2: 3D Collision
- Add `BlockGridCollision` for solid block checks
- Entities blocked by blocks at same height

### Phase 3: 3D Rendering
- `BlockEmitter` renders blocks at correct 3D positions
- Camera renders all heights

### Phase 4: Editor
- Full WYSIWYG block placement
- Save/load 3D maps

---

## Out of Scope (MVP)

- Terrain brush tools (paint large areas)
- Multi-select and copy/paste
- Runtime map loading from user files
- Full 3D navmesh (start with grid-based)

---

## Acceptance Criteria

### Core Migration
- [ ] `WorldPosition` replaces `Vector2` for entity positions
- [ ] All existing systems work with Y=0 (no regression)
- [ ] `GridCell3D` used for 3D spatial queries

### Editor
- [ ] Editor scene launches from main menu
- [ ] Blocks placed/removed in 3D grid
- [ ] Layer up/down changes active Y-level
- [ ] Blocks render at correct 3D positions
- [ ] Map saves/loads with JDeck

### Collision
- [ ] Box collision for standard blocks
- [ ] Mesh collision for rotated slope blocks
- [ ] Height differences respected
- [ ] 3D spatial grid for collision queries

### Camera
- [ ] Isometric camera works with 3D world
- [ ] Free-fly camera available in editor

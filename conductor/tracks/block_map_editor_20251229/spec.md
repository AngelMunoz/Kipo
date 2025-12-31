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

### 5. Map Objects & Settings
- **Map Settings**: Global properties (BattleMode, MaxEnemies)
- **Map Objects**: Logical entities distinct from blocks (Spawns, Zones, Teleports)
- **Object Types**:
  - **Spawn**: Player/Enemy spawn points with group/faction data
  - **Zone**: 3D volumes for effects (Speed, Heal, Damage)
  - **Teleport**: localized teleports or map transitions
- **Object Properties**: Strongly typed per object kind (no generic dictionaries)

### 6. 3D Collision System
- **Dual-strategy**: Fast AABB for regular blocks, mesh collision for slopes
- **CollisionType per BlockType**: `Box | Mesh | NoCollision`
- **3D Spatial Grid**: Extends current 2D grid to 3D (`GridCell3D`)
- **Mesh Collision**: For rotated slope blocks, ray-surface intersection

### 7. 3D Pathfinding
- **3D Navigation Grid**: Walkable cells with height transitions
- **Ramps/Stairs**: Transition blocks between Y-levels
- **A* with Height**: Path considers vertical movement

### 8. 3D System Alternatives (Phase 5b)

> Full 3D versions of gameplay systems. All use module functions + factory object expressions, GC-friendly.

#### SpawnData (Extended)
```fsharp
[<Struct>]
type SpawnData = {
  GroupId: string
  SpawnChance: float32
  Faction: Faction voption
  ArchetypeId: string voption  // References AIArchetype
}
```

#### ProjectileTarget3D
```fsharp
[<Struct>]
type ProjectileTarget3D =
  | EntityTarget of Guid<EntityId>
  | PositionTarget of WorldPosition
```

#### MovementState3D
```fsharp
type MovementState =
  | ...
  | MovingAlongPath3D of WorldPosition list
```

#### Camera3D Module
```fsharp
module Camera3D =
  type State = { Position: Vector3; Yaw: float32; Pitch: float32; Zoom: float32 }
  val getViewMatrix: State -> Matrix
  val getProjectionMatrix: State -> Viewport -> pixelsPerUnit:float32 -> Matrix
  val getPickRay: State -> Vector2 -> Viewport -> pixelsPerUnit:float32 -> Ray
  val screenToWorld: State -> Vector2 -> Viewport -> pixelsPerUnit:float32 -> layer:float32 -> WorldPosition
  val panXZ: State -> deltaX:float32 -> deltaZ:float32 -> State
  val moveFreeFly: State -> Vector3 -> State
  val rotate: State -> deltaYaw:float32 -> deltaPitch:float32 -> State
  val zoom: State -> delta:float32 -> State
```

#### Pathfinding3D Module
```fsharp
module Pathfinding3D =
  type NavGrid3D = { BlockMap: BlockMapDefinition; CellSize: float32 }
  val isWalkable: NavGrid3D -> GridCell3D -> bool
  val getNeighbors: NavGrid3D -> GridCell3D -> GridCell3D[]
  val findPath: NavGrid3D -> WorldPosition -> WorldPosition -> WorldPosition list voption
  val hasLineOfSight: NavGrid3D -> WorldPosition -> WorldPosition -> bool
```

#### Navigation3D Module
```fsharp
module Navigation3D =
  val create: EventBus * IStateWriteService * ProjectionService -> CoreEventListener
```

#### BlockMapSpawning Module
```fsharp
module BlockMapSpawning =
  val getSpawnPoints: BlockMapDefinition -> MapObject list
  val getSpawnsByGroup: BlockMapDefinition -> groupId:string -> MapObject list
  val getSpawnsByFaction: BlockMapDefinition -> Faction -> MapObject list
```

#### Projectile3D Module
```fsharp
module Projectile3D =
  type WorldContext = {
    Positions: IReadOnlyDictionary<EntityId, WorldPosition>
    LiveEntities: HashSet<EntityId>
  }
  val processProjectile: IStateWriteService -> WorldContext -> dt:float32 -> EntityId -> LiveProjectile -> IndexList<GameEvent>
  val findChainTargets: WorldContext -> WorldPosition -> maxRange:float32 -> excludeId:EntityId -> struct (EntityId * float32)[]
```

#### Combat3D Module
```fsharp
module Combat3D =
  type EntityContext3D = {
    Positions: IReadOnlyDictionary<EntityId, WorldPosition>
    DerivedStats: HashMap<EntityId, DerivedStats>
    Factions: HashMap<EntityId, Faction>
  }
  module Targeting =
    val resolveSphere: EntityContext3D -> center:WorldPosition -> radius:float32 -> EntityId list
    val resolveCone3D: EntityContext3D -> casterId:EntityId -> target:WorldPosition -> aperture:float32 -> range:float32 -> EntityId list
    val resolveLine3D: EntityContext3D -> start:WorldPosition -> end:WorldPosition -> width:float32 -> EntityId list
    val resolveBox: EntityContext3D -> center:WorldPosition -> halfExtents:Vector3 -> EntityId list
```

### 9. Preview Mode
- **Play Button**: Test map with player entity
- **Spawn Point**: Designated block for player spawn
- **Stop Button**: Return to editor, map intact

### 10. Persistence
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

### Map Objects & Settings
```fsharp
type BattleMode = PVE | PVP

[<RequireQualifiedAccess>]
type ZoneEffect =
    | Speed of multiplier: float32
    | Heal of amount: float32
    | Damage of amount: float32
    | ResourceChange of resource: string * amount: float32

[<RequireQualifiedAccess>]
type MapObjectShape =
    | Point
    | Box of size: Vector3
    | Sphere of radius: float32

[<Struct>]
type SpawnProperties = {
    IsPlayerSpawn: bool
    EntityGroup: string voption
    MaxSpawns: int
    Faction: int voption
}

[<Struct>]
type ZoneProperties = {
    Effect: ZoneEffect
}

[<Struct>]
type TeleportProperties = {
    TargetMap: string voption
    TargetObjectName: string
}

[<RequireQualifiedAccess>]
type MapObjectData =
    | Spawn of SpawnProperties
    | Zone of ZoneProperties
    | Teleport of TeleportProperties
    | Trigger

[<Struct>]
type MapObject = {
    Id: int
    Name: string
    Position: WorldPosition
    Rotation: Quaternion voption
    Shape: MapObjectShape
    Data: MapObjectData
}

[<Struct>]
type MapSettings = {
    BattleMode: BattleMode
    MaxEnemyEntities: int
}
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
  Settings: MapSettings
  Palette: Dictionary<int<BlockTypeId>, BlockType>
  Blocks: Dictionary<GridCell3D, PlacedBlock>
  Objects: Dictionary<int, MapObject>
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
  // New for Objects
  SelectedObjectId: cval<int voption>
  ToolMode: cval<ToolMode> // Block | Object
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
- **Game Logic Integration**: This track only covers the editor and format. Parsing these objects for gameplay happens in a separate track.

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
- [ ] **Map Settings** editable via UI
- [ ] **Map Objects** (Spawn, Zone) placement and property editing

### Collision
- [ ] Box collision for standard blocks
- [ ] Mesh collision for rotated slope blocks
- [ ] Height differences respected
- [ ] 3D spatial grid for collision queries

### Camera
- [ ] Isometric camera works with 3D world
- [ ] Free-fly camera available in editor

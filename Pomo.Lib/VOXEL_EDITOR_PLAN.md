# Voxel Editor Implementation Plan - Pomo.Lib/Mibo Architecture

> **Context**: This plan reimplements the Pomo.Core voxel editor from scratch using Mibo's Elmish (MVU) architecture. The old Pomo.Core editor relied on FSharp.Data.Adaptive reactive state; Mibo uses pure functional state transformations.

> **Architecture Pattern**: Module-based Elmish decomposition - single file per module, each with its own Model/Msg/Update/View. Main program orchestrates subsystems.

> **Last Updated**: 2026-01-29 - Check disk before implementing - file locations and patterns have diverged from original plan

---

## Current State

**Implemented:**
- Domain types: BlockMapDefinition, BlockType, PlacedBlock, etc. in Pomo.Lib/Domain.fs
- Editor types: BrushMode, CameraMode, EditorAction in Pomo.Lib/Editor/Domain.fs
- Services: FileSystem, BlockMapPersistence in Pomo.Lib/Services/
- AppEnv: Central composition in Pomo.Lib/AppEnv.fs
- BlockMap subsystem: Model, Msg, init, update in Pomo.Lib/Editor/Subsystems/BlockMap.fs
- Entry point: Consolidated in Pomo.Lib/Editor/Entry.fs

**Stubs:**
- BlockMapPersistence Save/Load (TODO comments present)
- Editor view function (empty implementation)

**Not Started:**
- Camera, Brush, Navigation, Input, History, UI subsystems
- Subscriptions
- JSON serialization (actual encode/decode)

---

## 1. Target Features

### 1.1 Core Editor Features

**Block Manipulation:**

- [x] Place blocks at grid cursor position
- [x] Remove blocks at cursor position
- [ ] Block rotation (X, Y, Z axes via Q/E keys)
- [ ] Multiple brush modes (Place, Erase, Select) - types exist, subsystem needed
- [ ] Drag-to-place (continuous placement while holding click)
- [ ] Collision-enabled variant toggle

**Navigation:**

- [ ] Isometric camera (pan with WASD, zoom with scroll, fixed angle)
- [ ] Free-fly camera (6DOF, rotate with right-click drag)
- [ ] Layer navigation (Page Up/Down to change Y-level)
- [ ] Grid cursor tracking (ray-plane intersection at current layer)
- [ ] Visual grid overlay at current editing layer
- [ ] Ghost block preview at cursor position

**State Management:**

- [ ] Undo/Redo with full action history
- [x] Dirty tracking
- [ ] Block map serialization/deserialization (JSON) - stubbed
- [ ] New map creation with configurable dimensions
- [ ] Load/save map files with validation - stubbed

### 1.2 Map Domain Features

**Block Types:**

- [ ] Block palette (archetypes loaded from JSON)
- [ ] Block categories for UI organization
- [ ] Variant system (collision, effect variants)
- [ ] Block models (3D model references)
- [ ] Collision types (Box, Mesh, NoCollision)
- [ ] Block effects (lava damage, ice slow, etc. via Skill.Effect)

**Map Structure:**

- [ ] 3D grid coordinates (X/Y/Z)
- [ ] Sparse block storage (only occupied cells)
- [ ] Map dimensions (Width, Height, Depth)
- [ ] Spawn point definition
- [ ] Map settings (engagement rules, max enemies)
- [ ] Map objects (spawns, teleports, triggers)

### 1.3 Rendering Features

**3D Rendering:**

- [ ] Block mesh rendering with Mibo's draw builder
- [ ] Proper world transforms (scale × rotation × translation)
- [ ] Frustum culling for visible blocks
- [ ] Ghost block with transparency
- [ ] Cursor wireframe overlay
- [ ] Grid line rendering
- [ ] Lighting support (PBR materials optional, unlit for editor)

**Performance:**

- [ ] Lazy model loading (cached by model path)
- [ ] View culling (only visible blocks rendered)
- [ ] Array pooling for render commands (no GC in hot path)

### 1.4 UI Features

**Editor UI:**

- [ ] Block palette with category filtering
- [ ] Block type selection (click to select brush)
- [ ] Layer indicator and navigation buttons
- [ ] Brush mode toggle buttons
- [ ] Camera mode switch (Isometric/FreeFly)
- [ ] Help overlay with keyboard shortcuts
- [ ] Undo/Redo buttons
- [ ] Save/Load menu with file dialogs
- [ ] Map settings panel (dimensions, spawn point)

**Input Handling:**

- [ ] Semantic input mapping (actions bound to keys)
- [ ] Mouse tracking for cursor position
- [ ] Mouse click for block placement/removal
- [ ] Keyboard shortcuts (undo, redo, rotation, layer change)
- [ ] Input rebinding support (via Mibo's input mapping)

### 1.5 Serialization Features

**JSON Persistence:**

- [ ] BlockMapDefinition encoder/decoder
- [ ] PlacedBlock encoder/decoder
- [ ] BlockType encoder/decoder
- [ ] MapObject encoder/decoder
- [ ] Quaternion encoder/decoder
- [ ] GridCell3D encoder/decoder
- [ ] Roundtrip verification (encode → decode → equal)

---

## 2. Architecture & Structure

### 2.1 File Organization

**Current Layout:**

```
Pomo.Lib/
  Editor/
    Domain.fs           // BrushMode, CameraMode, EditorAction
    Entry.fs            // Main Elmish (Model, Msg, init, update, view)
    Subsystems/
      BlockMap.fs       // Model, Msg, init, update (view stub)

  Services/
    FileSystem.fs       // FileSystem capability + implementation
    BlockMapPersistence.fs // Persistence capability + stub

  Domain.fs             // BlockMapDefinition, BlockType, etc.
  AppEnv.fs             // Central composition root
```

**Differences from plan:**
- Services at library level (shared with gameplay)
- Domain types at library level (shared)
- Entry.fs consolidated instead of Model.fs + Msg.fs + Init.fs + Update.fs + View.fs

### 2.2 Core Domain Types

**Pomo.Lib/Domain.fs:**

```fsharp
[<Measure>] type BlockTypeId

[<Struct>] type GridDimensions = { Width: int; Height: int; Depth: int }
[<Struct>] type CollisionType = Box | Mesh | NoCollision

type BlockType = {
  Id: int<BlockTypeId>
  ArchetypeId: int<BlockTypeId>
  VariantKey: string voption
  Name: string
  Model: string
  Category: string
  CollisionType: CollisionType
}

[<Struct>] type PlacedBlock = {
  Cell: Vector3
  BlockTypeId: int<BlockTypeId>
  Rotation: Quaternion voption
}

type BlockMapDefinition = {
  Version: int
  Key: string
  MapKey: string voption
  Dimensions: GridDimensions
  Palette: Dictionary<int<BlockTypeId>, BlockType>
  Blocks: Dictionary<Vector3, PlacedBlock>
  SpawnCell: Vector3 voption
  Settings: MapSettings
  Objects: MapObject[]
}
```

**Pomo.Lib/Editor/Domain.fs:**

```fsharp
[<Struct>] type BrushMode = Place | Erase | Select
[<Struct>] type CameraMode = Isometric | FreeFly

[<Struct>] type EditorAction =
  | PlaceBlock of cell: Vector3 * blockTypeId: int<BlockTypeId>
  | RemoveBlock of cell: Vector3
  | ChangeLayer of layer: int
  | SetBrushMode of brushMode: BrushMode
  | SetCameraMode of cameraMode: CameraMode

[<Struct>] type InputState = {
  MousePosition: Point
  IsLeftDown: bool
  IsRightDown: bool
  KeysDown: Set<Keys>
}
```

### 2.3 Subsystem Model Pattern

**Pattern from BlockMap.fs:**

```fsharp
namespace Pomo.Lib.Editor.Subsystems

open Microsoft.Xna.Framework
open Mibo.Elmish
open Pomo.Lib
open Pomo.Lib.Services

module BlockMap =
  [<Struct>]
  type BlockMapModel = {
    Definition: BlockMapDefinition
    Cursor: Vector3 voption
    Dirty: bool
  }

  type BlockMapMsg =
    | PlaceBlock of cell: Vector3 * blockId: int<BlockTypeId>
    | RemoveBlock of cell: Vector3
    | SetCursor of Vector3 voption
    | SetMap of BlockMapDefinition

  let init
    (_env: #FileSystemCap & #AssetsCap)
    (mapDef: BlockMapDefinition)
    : BlockMapModel =
    { Definition = mapDef; Cursor = ValueNone; Dirty = false }

  let update
    (_env: #FileSystemCap & #AssetsCap)
    (msg: BlockMapMsg)
    (model: BlockMapModel)
    : struct (BlockMapModel * Cmd<BlockMapMsg>) =
    match msg with
    | PlaceBlock(cell, blockId) ->
      model.Definition.Blocks.Add(cell, { Cell = cell; BlockTypeId = blockId; Rotation = ValueNone })
      { model with Dirty = true }, Cmd.none
    | RemoveBlock cell ->
      model.Definition.Blocks.Remove cell |> ignore
      { model with Dirty = true }, Cmd.none
    | SetCursor cursor -> { model with Cursor = cursor }, Cmd.none
    | SetMap map -> { model with Definition = map; Dirty = false }, Cmd.none

  let view ctx model buffer = ()  // stub
```

### 2.4 Main Model Composition

**From Entry.fs:**

```fsharp
namespace Pomo.Lib.Editor

open Microsoft.Xna.Framework
open Mibo.Elmish
open Pomo.Lib
open Pomo.Lib.Services
open Pomo.Lib.Editor.Subsystems
open Pomo.Lib.Editor.Subsystems.BlockMap

[<Struct>] type EditorModel = { BlockMap: BlockMapModel }

[<Struct>] type EditorMsg =
  | BlockMapMsg of blockMap: BlockMapMsg
  | Tick of gt: GameTime

module Entry =
  let init
    (env: #FileSystemCap & #AssetsCap & #BlockMapPersistenceCap)
    (ctx: obj)
    : struct (EditorModel * Cmd<EditorMsg>) =
    { BlockMap = BlockMap.init env BlockMapDefinition.empty }, Cmd.none

  let update
    (env: #FileSystemCap & #AssetsCap & #BlockMapPersistenceCap)
    (msg: EditorMsg)
    (model: EditorModel)
    : struct (EditorModel * Cmd<EditorMsg>) =
    match msg with
    | BlockMapMsg subMsg ->
      let struct (subModel, cmd) = BlockMap.update env subMsg model.BlockMap
      { model with BlockMap = subModel }, cmd |> Cmd.map BlockMapMsg
    | Tick _ -> model, Cmd.none

  let view env ctx model buffer = ()  // stub
```

Add new subsystems by: extending EditorModel, adding wrapped case to EditorMsg, handling in update with Cmd.map, calling in view.

### 2.5 Update, View, Subscriptions, Services

All consolidated in Entry.fs. See section 2.4 for the actual pattern.

Planned additions:
- Subscriptions module for input handling
- View context with rendering pipeline
- Additional services (ModelCache, AssetLoader)

---

## 3. Follow-Ups & Extensions

### 3.1 Phase 2: Advanced Editor Features

**Object Editing:**

- [ ] Map object placement (spawns, teleports, triggers)
- [ ] Object selection and property editing
- [ ] Object shape visualization (wireframe box, sphere)
- [ ] Spawn group configuration
- [ ] Zone effects (link to Skill.Effect)

**Copy/Paste:**

- [ ] Block selection (multi-cell)
- [ ] Copy selected region to clipboard
- [ ] Paste blocks at cursor
- [ ] Rotate pasted blocks as group

**Map Settings UI:**

- [ ] Side panel with all map properties
- [ ] Real-time dimension editing (with validation)
- [ ] Spawn point visualizer and placement
- [ ] Engagement rules selector
- [ ] Max enemy count slider

### 3.2 Phase 3: Performance Optimizations

**Adaptive State Integration (Optional):**

- [ ] Evaluate FSharp.Data.Adaptive for hot-path state
- [ ] Replace reactive cval with adaptive collections for:
  - BlockMap.Blocks (adaptive cmap for change propagation)
  - Camera position (adaptive cval for smooth interpolation)
- [ ] Keep non-adaptive for:
  - Brush, History, UI (low-frequency updates)

**Spatial Indexing:**

- [ ] Octree or chunk-based spatial index for large maps
- [ ] Fast block lookup by position
- [ ] Efficient culling for view frustum

**Rendering Optimizations:**

- [ ] GPU instancing for repeated blocks
- [ ] Mesh combining for static geometry
- [ ] Level-of-detail (LOD) for distant blocks
- [ ] Async mesh loading

### 3.3 Phase 4: Game Integration

**Playtest Mode:**

- [ ] Seamless transition from editor to gameplay
- [ ] Load editor map in game scene
- [ ] Spawn entities from map objects
- [ ] Quick return to editor from gameplay

**Export Pipeline:**

- [ ] Export to optimized runtime format
- [ ] Bake lighting/occlusion
- [ ] Generate collision meshes
- [ ] Compress block palette (deduplicate unused blocks)

**Assistive Tools:**

- [ ] Auto-fill (fill contiguous region)
- [ ] Replace (swap all blocks of type X with Y)
- [ ] Undo/redo visualization (timeline view)

---

## 4. Implementation Phases

### Phase 1: Foundation

- [x] Core domain types
- [x] Service infrastructure
- [x] AppEnv composition
- [x] BlockMap subsystem
- [x] Basic Elmish setup
- [ ] Camera subsystem
- [ ] Brush subsystem
- [ ] Input handling
- [ ] View/Rendering
- [ ] Mibo pipeline

### Phase 2: Core Editor

- [ ] Block placement/removal
- [ ] Cursor tracking
- [ ] Grid overlay
- [ ] Ghost block preview
- [ ] Basic UI (palette, layer indicator)

### Phase 3: Serialization

- [ ] JSON encoders/decoders
- [ ] Save/Load functionality
- [ ] New map creation
- [ ] Validation and error handling

### Phase 4: Advanced Editor

- [ ] Undo/Redo system
- [ ] Camera modes (isometric + free-fly)
- [ ] Brush rotation
- [ ] Collision variants
- [ ] Map objects

### Phase 5: Polish

- [ ] Complete UI (all panels, dialogs)
- [ ] Input mapping and rebinding
- [ ] Help system
- [ ] Performance optimization
- [ ] Testing and bug fixes

---

## 5. Design Decisions

### 5.1 Single File Per Module

- **Reasoning**: Keeps related code together, reduces file navigation overhead
- **Trade-off**: Can grow large, but F# code folding mitigates this

### 5.2 Message Wrapping Pattern

- **Reasoning**: Type-safe message delegation, compiler enforces correct lifting
- **Trade-off**: Boilerplate for wrapping/unwrapping messages

### 5.3 Optional System Pipeline

- **Reasoning**: Start simple, add pipeline when complexity grows
- **Trade-off**: Need to refactor when adding pipeline (but low cost)

### 5.4 No Adaptive State Initially

- **Reasoning**: Pure MVU is simpler, easier to reason about
- **Trade-off**: May need to integrate adaptive collections later for performance

### 5.5 Service Abstractions

- **Reasoning**: Testability, can swap implementations (file system, asset loader)
- **Trade-off**: Slightly more indirection, but enables clean testing

---

## 6. Testing Strategy

### 6.1 Unit Tests

- [ ] Serialization roundtrip (encode → decode → equal)
- [ ] Block map transformations (place, remove, rotate)
- [ ] Coordinate conversions (cell ↔ world position)
- [ ] Undo/Redo reversibility
- [ ] Camera matrix calculations

### 6.2 Integration Tests

- [ ] Full editor workflow (new map → place blocks → save → load → verify)
- [ ] Camera mode switching
- [ ] UI interaction (click palette, select block, place)

---

**Next Steps**: Begin Phase 1 implementation by creating the core domain types and subsystem structure in Pomo.Lib/Editor/.

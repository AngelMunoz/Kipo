# Voxel Editor Implementation Plan - Pomo.Lib/Mibo Architecture

> **Context**: This plan reimplements the Pomo.Core voxel editor from scratch using Mibo's Elmish (MVU) architecture. The old Pomo.Core editor relied on FSharp.Data.Adaptive reactive state; Mibo uses pure functional state transformations.

> **Architecture Pattern**: Module-based Elmish decomposition - single file per module, each with its own Model/Msg/Update/View. Main program orchestrates subsystems.

> **Last Updated**: 2026-01-30 - Mouse cursor tracking service implemented, grid/cursor rendering optimized with buffer.Lines

---

## Current State

**Implemented:**
- Domain types: BlockMapDefinition, BlockType, PlacedBlock, etc. in Pomo.Lib/Domain.fs
- Editor types: BrushMode, CameraMode, EditorAction in Pomo.Lib/Editor/Domain.fs
- Services: FileSystem, BlockMapPersistence, EditorCursor in Pomo.Lib/Services/
- AppEnv: Central composition with capability interfaces in Pomo.Lib/AppEnv.fs
- BlockMap subsystem: Model, Msg, init, update in Pomo.Lib/Editor/Subsystems/BlockMap.fs
- Camera subsystem: Pomo.Lib/Editor/Subsystems/Camera.fs (Isometric/FreeFly modes, pan, zoom, layer tracking)
- EditorCursor service: Pull-based cursor tracking for ray-plane intersection at editing layer
- Input handling: Subscriptions wired via InputMapper with semantic action mapping in Pomo.Lib/Editor/Entry.fs
- Mouse interaction: Left-click place block, right-click remove block via EditorCursor service
- View/Rendering: Grid overlay using buffer.Lines with VertexPositionColor arrays
- Mouse cursor tracking: Ray-plane intersection at current layer with wireframe box highlight
- Entry point: Consolidated in Pomo.Lib/Editor/Entry.fs with InputHandler module

**Stubs:**
- BlockMapPersistence Save/Load (TODO comments present)
- BlockMap subsystem view function (empty implementation)
- Camera view function (empty - camera is renderless)

**Not Started:**
- Brush subsystem (types exist in Domain.fs, no subsystem yet)
- History (Undo/Redo) subsystem
- UI subsystem
- JSON serialization (actual encode/decode)

---

## 1. Target Features

### 1.1 Core Editor Features

**Block Manipulation:**

- [x] Place blocks at grid cursor position (via BlockMap subsystem - API ready)
- [x] Remove blocks at cursor position (via BlockMap subsystem - API ready)
- [ ] Block rotation (X, Y, Z axes via Q/E keys)
- [ ] Multiple brush modes (Place, Erase, Select) - types exist in Domain.fs, subsystem needed
- [ ] Drag-to-place (continuous placement while holding click)
- [ ] Collision-enabled variant toggle

**Navigation:**

- [x] Isometric camera (pan with WASD, fixed angle)
- [x] Free-fly camera (6DOF with mode toggle via Tab)
- [x] Layer navigation (Page Up/Down to change Y-level)
- [x] Visual grid overlay at current editing layer
- [x] Grid cursor tracking (ray-plane intersection at current layer)
- [ ] Ghost block preview at cursor position

**State Management:**

- [ ] Undo/Redo with full action history
- [x] Dirty tracking (in BlockMapModel)
- [ ] Block map serialization/deserialization (JSON) - stubbed
- [ ] New map creation with configurable dimensions
- [ ] Load/save map files with validation - stubbed

### 1.2 Map Domain Features

**Block Types:**

- [ ] Block palette (archetypes loaded from JSON)
- [ ] Block categories for UI organization
- [ ] Variant system (collision, effect variants)
- [ ] Block models (3D model references)
- [ ] Collision types (Box, Mesh, NoCollision) - type exists
- [ ] Block effects (lava damage, ice slow, etc. via Skill.Effect)

**Map Structure:**

- [x] 3D grid coordinates (X/Y/Z) - using Vector3
- [x] Sparse block storage (only occupied cells) - Dictionary<Vector3, PlacedBlock>
- [x] Map dimensions (Width, Height, Depth) - GridDimensions type
- [ ] Spawn point definition
- [ ] Map settings (engagement rules, max enemies) - type exists
- [ ] Map objects (spawns, teleports, triggers) - type exists

### 1.3 Rendering Features

**3D Rendering:**

- [ ] Block mesh rendering with Mibo's draw builder
- [ ] Proper world transforms (scale × rotation × translation)
- [ ] Frustum culling for visible blocks
- [ ] Ghost block with transparency
- [x] Cursor wireframe overlay - Yellow wireframe box at cursor cell using buffer.Lines
- [x] Grid line rendering - Using buffer.Lines with VertexPositionColor arrays
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
- [ ] Camera mode switch (Isometric/FreeFly) - implemented via Tab key
- [ ] Help overlay with keyboard shortcuts
- [ ] Undo/Redo buttons
- [ ] Save/Load menu with file dialogs
- [ ] Map settings panel (dimensions, spawn point)

**Input Handling:**

- [x] Semantic input mapping (actions bound to keys) - EditorInputAction type with InputMapper
- [x] Mouse tracking for cursor position - EditorCursor service with ray-plane intersection
- [x] Mouse click for block placement/removal - LeftClick/RightClick input actions
- [x] Keyboard shortcuts for camera navigation (WASD, PageUp/Down, Home, Tab)
- [ ] Input rebinding support (via Mibo's input mapping)

### 1.5 Serialization Features

**JSON Persistence:**

- [ ] BlockMapDefinition encoder/decoder
- [ ] PlacedBlock encoder/decoder
- [ ] BlockType encoder/decoder
- [ ] MapObject encoder/decoder
- [ ] Quaternion encoder/decoder
- [ ] Grid coordinates encoder/decoder
- [ ] Roundtrip verification (encode → decode → equal)

---

## 2. Architecture & Structure

### 2.1 File Organization

**Current Layout:**

```
Pomo.Lib/
  Editor/
    Domain.fs           // BrushMode, CameraMode, EditorAction
    Entry.fs            // Main Elmish (Model, Msg, init, update, view, subscribe)
    Subsystems/
      BlockMap.fs       // Model, Msg, init, update, view
      Camera.fs         // Model, Msg, init, update, view

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
- Camera subsystem added

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

**Pomo.Lib/Editor/Entry.fs (Input Actions):**

```fsharp
[<Struct>] type EditorInputAction =
  | PanLeft
  | PanRight
  | PanForward
  | PanBackward
  | LayerUp
  | LayerDown
  | ResetCameraView
  | ToggleCameraMode
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

  let view ctx model buffer = ()
```

**Pattern from Camera.fs:**

```fsharp
namespace Pomo.Lib.Editor.Subsystems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Mibo.Elmish
open Mibo.Rendering.Graphics3D
open Pomo.Lib.Services

module Camera =
  [<Struct>] type CameraMode = Isometric | FreeFly

  [<Struct>] type CameraModel = {
    Camera: Camera
    Mode: CameraMode
    Pan: Vector2
    Zoom: float32
    CurrentLayer: int
    IsDragging: bool
    LastMousePos: Point
  }

  type Msg =
    | SetMode of CameraMode
    | Pan of delta: Vector2
    | Zoom of delta: float32
    | SetLayer of layer: int
    | SetIsDragging of isDragging: bool
    | SetLastMousePos of pos: Point
    | Orbit of yaw: float32 * pitch: float32
    | ResetCamera

  let isometricDefaults: Camera = ...
  let freeFlyDefaults: Camera = ...

  let init (_env: #FileSystemCap & #AssetsCap) : CameraModel = ...

  let update
    (_env: #FileSystemCap & #AssetsCap)
    (msg: Msg)
    (model: CameraModel)
    : struct (CameraModel * Cmd<Msg>) = ...

  let view _ctx _model _buffer = ()
```

### 2.4 Main Model Composition

**From Entry.fs:**

```fsharp
namespace Pomo.Lib.Editor

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Mibo.Elmish
open Mibo.Input
open Mibo.Rendering
open Mibo.Rendering.Graphics3D
open Pomo.Lib
open Pomo.Lib.Services
open Pomo.Lib.Editor.Subsystems
open Pomo.Lib.Editor.Subsystems.BlockMap
open Pomo.Lib.Editor.Subsystems.Camera

[<Struct>] type EditorInputAction = ...

[<Struct>] type EditorModel = {
  BlockMap: BlockMapModel
  Camera: CameraModel
  Actions: ActionState<EditorInputAction>
}

[<Struct>] type EditorMsg =
  | BlockMapMsg of blockMap: BlockMap.Msg
  | CameraMsg of camera: Camera.Msg
  | InputMapped of ActionState<EditorInputAction>
  | Tick of gt: GameTime

module Entry =
  let private inputMap = ...

  let init env ctx : struct (EditorModel * Cmd<EditorMsg>) = ...

  let update env msg model : struct (EditorModel * Cmd<EditorMsg>) = ...

  let subscribe ctx model : Sub<EditorMsg> =
    InputMapper.subscribeStatic inputMap InputMapped ctx

  let view env ctx model buffer : unit = ...
```

### 2.5 Update, View, Subscriptions, Services

All consolidated in Entry.fs. See section 2.4 for the actual pattern.

Implemented additions:
- Subscriptions via InputMapper for input handling
- View context with rendering pipeline
- Grid rendering via line drawing

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
- [x] Camera subsystem
- [ ] Brush subsystem
- [x] Input handling (keyboard navigation)
- [x] View/Rendering (grid)
- [x] Mibo pipeline
- [x] Mouse/cursor tracking
- [x] Block placement via mouse

### Phase 2: Core Editor

- [x] Block placement/removal via mouse
- [x] Cursor tracking (ray-plane intersection) - EditorCursor service
- [x] Grid overlay - Using buffer.Lines
- [ ] Ghost block preview
- [ ] Basic UI (palette, layer indicator)

### Phase 3: Serialization

- [ ] JSON encoders/decoders
- [ ] Save/Load functionality
- [ ] New map creation
- [ ] Validation and error handling

### Phase 4: Advanced Editor

- [ ] Undo/Redo system
- [x] Camera modes (isometric + free-fly)
- [ ] Brush rotation
- [ ] Collision variants
- [ ] Map objects

### Phase 5: Polish

- [ ] Complete UI (all panels, dialogs)
- [x] Input mapping and rebinding (framework in place)
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

**Next Steps**: Begin Phase 2 completion by implementing Brush subsystem (Place/Erase/Select modes) and Ghost block preview.

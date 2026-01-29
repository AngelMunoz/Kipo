# Voxel Editor Implementation Plan - Pomo.Lib/Mibo Architecture

> **Context**: This plan reimplements the Pomo.Core voxel editor from scratch using Mibo's Elmish (MVU) architecture. The old Pomo.Core editor relied on FSharp.Data.Adaptive reactive state; Mibo uses pure functional state transformations.

> **Architecture Pattern**: Module-based Elmish decomposition - single file per module, each with its own Model/Msg/Update/View. Main program orchestrates subsystems.

---

## 1. Target Features

### 1.1 Core Editor Features

**Block Manipulation:**

- [ ] Place blocks at grid cursor position
- [ ] Remove blocks at cursor position
- [ ] Block rotation (X, Y, Z axes via Q/E keys)
- [ ] Multiple brush modes (Place, Erase, Select)
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
- [ ] Dirty tracking (map version increments on modification)
- [ ] Block map serialization/deserialization (JSON)
- [ ] New map creation with configurable dimensions
- [ ] Load/save map files with validation

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

**Top-Level Modules:**

```
Pomo.Lib/
  Editor/
    Domain.fs           // Core domain types (BrushMode, CameraMode, EditorAction)
    Model.fs            // EditorModel = composition of subsystems
    Msg.fs              // EditorMsg = wrapping pattern for subsystems
    Init.fs             // Initialize editor state and services
    Update.fs           // Main update, delegates to subsystems
    View.fs             // Main view, delegates to subsystems
    Subscriptions.fs    // Input subscriptions
    Services.fs         // Service abstractions and implementations
    Persistence.fs     // JSON serialization

    Subsystems/
      BlockMap.fs       // BlockMap model, messages, update, view
      Brush.fs          // Brush model, messages, update, view
      Camera.fs         // Camera model, messages, update, view
      Navigation.fs     // Navigation model, messages, update, view
      UI.fs            // UI model, messages, update, view
      History.fs        // History model, messages, update, view
      Input.fs          // Input model, messages, update, view
```

### 2.2 Core Domain Types

**Spatial Types (Domain.fs):**

```fsharp
// 3D grid coordinates (integer)
[<Struct>]
type GridCell3D = { X: int; Y: int; Z: int }

// World position (float32)
[<Struct>]
type WorldPosition = { X: float32; Y: float32; Z: float32 }

// Map dimensions
[<Struct>]
type GridDimensions = { Width: int; Height: int; Depth: int }

// Transform
[<Struct>]
type Transform = {
  Position: WorldPosition
  Rotation: Quaternion
  Scale: Vector3
}
```

**Block Types (Subsystems/BlockMap.fs):**

```fsharp
type BlockTypeId = int

[<Struct>]
type CollisionType = Box | Mesh | NoCollision

[<Struct>]
type BlockType = {
  Id: BlockTypeId
  ArchetypeId: BlockTypeId
  VariantKey: string voption
  Name: string
  Model: string
  Category: string
  CollisionType: CollisionType
}

[<Struct>]
type PlacedBlock = {
  Cell: GridCell3D
  BlockTypeId: BlockTypeId
  Rotation: Quaternion voption
}

type BlockMapDefinition = {
  Version: int
  Key: string
  Dimensions: GridDimensions
  Palette: Map<BlockTypeId, BlockType>
  Blocks: Map<GridCell3D, PlacedBlock>
  SpawnCell: GridCell3D voption
  Settings: MapSettings
  Objects: MapObject list
}
```

**Editor Types (Domain.fs):**

```fsharp
[<Struct>]
type BrushMode = Place | Erase | Select

[<Struct>]
type CameraMode = Isometric | FreeFly

[<Struct>]
type EditorAction =
  | PlaceBlock of PlacedBlock * PlacedBlock voption
  | RemoveBlock of GridCell3D * PlacedBlock voption
  | SetRotation of Quaternion * Quaternion
  | ChangeLayer of int
  | SetBrushMode of BrushMode * BrushMode
```

### 2.3 Subsystem Model Pattern

Each subsystem file follows this pattern:

```fsharp
namespace Pomo.Lib.Editor.Subsystems

open Mibo.Elmish

// 1. MODEL
[<Struct>]
type BlockMapModel = {
  Definition: BlockMapDefinition
  Cursor: GridCell3D voption
  Dirty: bool
}

// 2. MESSAGES
and BlockMapMsg =
  | PlaceBlock of cell: GridCell3D
  | RemoveBlock of cell: GridCell3D
  | SetCursor of GridCell3D voption
  | SetMap of BlockMapDefinition
  | SetSpawn of GridCell3D voption

// 3. INIT
module BlockMap =
  let init (map: BlockMapDefinition) : BlockMapModel = {
    Definition = map
    Cursor = ValueNone
    Dirty = false
  }

// 4. UPDATE
  let update (msg: BlockMapMsg) (model: BlockMapModel)
    : struct (BlockMapModel * Cmd<BlockMapMsg>) =
    match msg with
    | PlaceBlock cell ->
      // Pure transformation logic
      let newDef = { model.Definition with ... }
      struct ({ model with Definition = newDef; Dirty = true }, Cmd.none)

    | SetCursor opt ->
      struct ({ model with Cursor = opt }, Cmd.none)

    | _ -> struct (model, Cmd.none)

// 5. VIEW (render commands)
  let view (ctx: EditorViewContext) (model: BlockMapModel)
    (buffer: RenderBuffer<unit, RenderCommand>) : unit =
    // Emit render commands based on model state
    match model.Cursor with
    | ValueSome cell ->
      buffer |> DrawCursor cell
    | ValueNone -> ()
```

### 2.4 Main Model Composition

**Main Editor Model (Model.fs):**

```fsharp
namespace Pomo.Lib.Editor

open Mibo.Elmish
open Pomo.Lib.Editor.Subsystems

[<Struct>]
type EditorModel = {
  // Subsystem models
  BlockMap: BlockMap.BlockMapModel
  Brush: Brush.BrushModel
  Camera: Camera.CameraModel
  Navigation: Navigation.NavigationModel
  UI: UI.UIModel
  History: History.HistoryModel
  Input: Input.InputModel

  // Shared state
  Services: EditorServices
}
```

**Main Editor Message (Msg.fs):**

```fsharp
namespace Pomo.Lib.Editor

[<Struct>]
type EditorMsg =
  // Subsystem messages (wrapping pattern)
  | BlockMapMsg of BlockMap.BlockMapMsg
  | BrushMsg of Brush.BrushMsg
  | CameraMsg of Camera.CameraMsg
  | NavigationMsg of Navigation.NavigationMsg
  | UIMsg of UI.UIMsg
  | HistoryMsg of History.HistoryMsg
  | InputMsg of Input.InputMsg

  // Cross-subsystem messages
  | Tick of GameTime
  | FileSave of path: string
  | FileLoad of path: string
  | NewMap of GridDimensions
```

### 2.5 Update Orchestration

**Main Update (Update.fs):**

```fsharp
namespace Pomo.Lib.Editor

module Update =
  let update (msg: EditorMsg) (model: EditorModel)
    : struct (EditorModel * Cmd<EditorMsg>) =
    match msg with
    // Delegate to subsystems
    | BlockMapMsg msg ->
      let newBlockMap, cmd = BlockMap.update msg model.BlockMap
      let newModel = { model with BlockMap = newBlockMap }
      struct (newModel, cmd |> Cmd.map BlockMapMsg)

    | CameraMsg msg ->
      let newCamera, cmd = Camera.update msg model.Camera
      let newModel = { model with Camera = newCamera }
      struct (newModel, cmd |> Cmd.map CameraMsg)

    // Handle Tick with System Pipeline (optional for complex logic)
    | Tick gt ->
      let dt = float32 gt.ElapsedGameTime.TotalSeconds

      // Use Mibo System pipeline if needed
      // For now, simple delegation
      struct (model, Cmd.none)

    // File operations (async, return commands)
    | FileSave path ->
      let cmd = Persistence.save model.BlockMap.Definition path
      struct (model, cmd |> Cmd.map FileSaved)

    | _ -> struct (model, Cmd.none)
```

### 2.6 View Orchestration

**Main View (View.fs):**

```fsharp
namespace Pomo.Lib.Editor

open Mibo.Rendering.Graphics3D

type EditorViewContext = {
  Camera: Camera
  Lighting: LightingState
  Assets: AssetCache
  Time: GameTime
  Device: GraphicsDevice
}

module View =
  let view (ctx: EditorViewContext) (model: EditorModel)
    (buffer: RenderBuffer<unit, RenderCommand>) : unit =

    // 1. Set camera
    let camera = Camera.getCamera model.Camera
    buffer
      |> SetCamera camera
      |> Clear Color.Black

    // 2. Render subsystems (order matters)
    BlockMap.view ctx model.BlockMap buffer
    Navigation.view ctx model.Navigation buffer
    UI.view ctx model.UI buffer

    |> Submit()
```

### 2.7 Subscriptions

**Input Subscriptions (Subscriptions.fs):**

```fsharp
namespace Pomo.Lib.Editor

module Subscriptions =
  let subscribe (ctx: GameContext) (model: EditorModel) : Sub<EditorMsg> =
    Sub.batch [
      // Keyboard
      Keyboard.onPressed (fun key ->
        InputMsg (Input.KeyPressed key)) ctx

      // Mouse
      Mouse.onMove (fun pos ->
        InputMsg (Input.MouseMoved pos)) ctx

      Mouse.onLeftClick (fun pos ->
        InputMsg (Input.LeftClicked pos)) ctx

      // Semantic input mapping (recommended)
      InputMapper.subscribe {
        Map = Editor.inputMap
        OnAction = fun action -> InputMsg (Input.Action action)
      } ctx
    ]
```

### 2.8 Service Abstractions

**Editor Services (Services.fs):**

```fsharp
namespace Pomo.Lib.Editor

[<Struct>]
type EditorServices = {
  FileSystem: IFileSystem
  AssetLoader: IAssetLoader
  ModelCache: IModelCache
  Persistence: IBlockMapPersistence
}

[<Interface>]
type IFileSystem =
  abstract ReadTextFile: string -> Async<Result<string, Error>>
  abstract WriteTextFile: string * string -> Async<Result<unit, Error>>

[<Interface>]
type IAssetLoader =
  abstract LoadModel: string -> Async<Result<Model, Error>>

[<Interface>]
type IModelCache =
  abstract GetOrLoad: string -> Async<Result<Model, Error>>

[<Interface>]
type IBlockMapPersistence =
  abstract Save: BlockMapDefinition * string -> Async<Result<unit, Error>>
  abstract Load: string -> Async<Result<BlockMapDefinition, Error>>
```

### 2.9 Program Setup

**Initialization (Init.fs):**

```fsharp
namespace Pomo.Lib.Editor

module Init =
  let init (ctx: GameContext) : struct (EditorModel * Cmd<EditorMsg>) =

    // Initialize services
    let services = {
      FileSystem = StandardFileSystem()
      AssetLoader = MonoGameAssetLoader(ctx.Content)
      ModelCache = ModelCache()
      Persistence = JsonBlockMapPersistence()
    }

    // Load initial map or create new
    let initialMap = BlockMap.createDefault GridDimensions.Default

    // Initialize subsystems
    let blockMapModel = BlockMap.init initialMap
    let brushModel = Brush.init()
    let cameraModel = Camera.init CameraMode.Isometric
    let navigationModel = Navigation.init()
    let uiModel = UI.init()
    let historyModel = History.init()
    let inputModel = Input.init()

    let model = {
      BlockMap = blockMapModel
      Brush = brushModel
      Camera = cameraModel
      Navigation = navigationModel
      UI = uiModel
      History = historyModel
      Input = inputModel
      Services = services
    }

    struct (model, Cmd.none)
```

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

### Phase 1: Foundation (Current Goal)

- [ ] Core domain types
- [ ] Subsystem structure (single file per module)
- [ ] Basic Elmish program setup
- [ ] Mibo pipeline configuration
- [ ] Initial camera (isometric only)

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

# Mibo Redesign Analysis for Kipo

## Executive Summary

This document analyzes the gap between Kipo's current architecture and Mibo's intended design patterns. The current `Pomo.Core/App` namespace represents a partial integration attempt that doesn't fully align with Mibo's philosophy. This analysis identifies Kipo's features, maps them to Mibo's APIs, and proposes a ground-up redesign strategy.

---

## Part 1: Kipo Features Inventory

### Core Game Systems

#### 1. **Combat & Skills**
- **Skill System**: Data-driven skills with cooldowns, mana costs, damage calculations
- **Effect System**: Active effects with stacking, duration, refresh mechanics
- **Damage Calculator**: Formula-based damage with resistances and modifiers
- **Ability Activation**: Skill targeting (entity, position, directional)
- **Projectiles**: Homing, linear, and arc-based projectile physics
- **Orbital System**: Rotating visual effects around entities

#### 2. **Entity Management**
- **Entity Spawner**: Dynamic entity creation from templates
- **Resource Management**: HP, Mana, stamina with regeneration
- **Inventory & Equipment**: Item management with slots
- **Attributes**: Stats with base values and modifiers

#### 3. **AI System**
- **Decision Trees**: Hierarchical AI behavior
- **AI Archetypes & Families**: Configurable enemy behaviors
- **AI Perception**: Target memory and perception ranges
- **Navigation**: A* pathfinding in 3D block maps

#### 4. **Movement & Collision**
- **Player Movement**: WASD control with click-to-move
- **Unit Movement**: Velocity-based movement with pathfinding
- **Collision Detection**: Entity-entity and entity-terrain collision
- **3D Block Map**: Voxel-based world with height layers

#### 5. **Rendering**
- **3D Isometric Renderer**: Custom command-based renderer
- **Mesh Particles**: 3D particle effects
- **Animation System**: Keyframe-based skeletal animation
- **Camera System**: Isometric camera with zoom/rotation
- **Culling**: View frustum culling for performance

#### 6. **Input**
- **Raw Input**: Keyboard, mouse, gamepad polling
- **Input Mapping**: Semantic action mapping (GameAction DU)
- **Cursor System**: World-space cursor projection

#### 7. **UI**
- **Myra UI Integration**: Reactive UI with FSharp.Data.Adaptive
- **HUD System**: Health bars, notifications, action slots
- **Menu System**: Main menu, character sheets, equipment

#### 8. **Data Management**
- **Stores**: Skills, Items, AI configs, Models, Animations, Particles
- **Asset Preloader**: Eager loading with manifests
- **Serialization**: JSON-based configuration
- **Localization**: Multi-language support

#### 9. **Editor**
- **3D Map Editor**: WYSIWYG block map editing
- **Block Palette**: Placing/removing blocks with rotation
- **Undo/Redo**: History management
- **Editor Camera**: Free-look camera for editing

#### 10. **State Management**
- **FSharp.Data.Adaptive**: Reactive state with FDA
- **Command Queue Pattern**: Pooled command buffers for state writes
- **Event Bus**: Reactive event system for cross-system communication
- **Transaction-based Updates**: Batched state mutations

---

## Part 2: How Kipo Features Map to Mibo's APIs

### Mibo's Core Architecture

Mibo follows **Elmish/MVU (Model-View-Update)** pattern:
1. **init**: Define initial state → returns `(Model, Cmd<Msg>)`
2. **update**: `Msg → Model → (Model, Cmd<Msg>)` - pure state transformation
3. **view**: `GameContext → Model → RenderBuffer → unit` - populate render commands
4. **subscribe**: External events → messages

### Mapping Table

| Kipo Feature | Mibo Equivalent | Notes |
|--------------|-----------------|-------|
| **State Management** | `Model` (immutable record) | Currently using FDA heavily; Mibo is FDA-agnostic |
| **StateWrite (Command Queue)** | `Cmd<Msg>` | Instead of command buffers, return `Cmd` from `update` |
| **Event Bus** | `Cmd<Msg>` + Subscriptions | Use `Cmd.ofMsg`, `Cmd.batch`, `subscribe` for events |
| **Systems (GameComponent)** | Module-level functions in `update` | No GameComponent; logic in `update` and `view` |
| **RenderOrchestrator** | `IRenderer<Model>.Draw()` | Use `withRenderer` + RenderBuffer pattern |
| **Raw Input** | `Program.withInput` + `Sub` | `Keyboard.onPressed`, `Mouse.onMove` subscriptions |
| **Input Mapping** | `InputMap` + `ActionState<Action>` | Built-in semantic input mapping |
| **Assets (Lazy Loading)** | `Program.withAssets` + `IAssets` | Cached asset service via `GameContext` |
| **Camera** | Part of `Model` | Camera state lives in model, used in `view` |
| **UI (Myra)** | Separate `IRenderer<Model>` | Add UI renderer with `withRenderer` |
| **Scene Management** | Union type in `Model` | `type Scene = MainMenu | Gameplay | Editor` |
| **Stores (Static Data)** | Custom service on top of `IAssets` | Build domain-specific store wrapper |
| **AI Systems** | `update` logic (Level 2-3) | Use simulation transaction pattern |
| **Collision/Physics** | `update` logic (Level 3) | Use pipeline phases (mutable → snapshot → readonly) |
| **Particles** | Part of `Model` + emitters in `view` | Particles live in model, rendered in `view` |
| **Animation** | Part of `Model` (timers) | Animation state updated in `update` |

---

## Part 3: Main Gaps Between Current App and Mibo

### Architectural Mismatches

#### 1. **Hybrid Elmish/GameComponent Approach**
**Current:** Mixed approach with `IRenderer<AppModel>` doing input handling AND rendering
```fsharp
// EditorRenderer.fs - ANTI-PATTERN
member _.Draw(ctx, model, gameTime) =
  // Processing input inside renderer!
  AppEditorInput.processInput gd session.State camera ...
  // Also rendering
  renderEditor ctx.GraphicsDevice state session pixelsPerUnit
```

**Problem:**
- Violates separation of concerns (render should only render)
- Input handling is a side effect in `Draw`, not in `update`
- State mutations happen outside the Elmish loop

**Mibo Way:**
- Input → Subscriptions → Messages → `update` → new Model
- `view` ONLY reads model and populates render buffer

---

#### 2. **Event Bus Instead of Cmd**
**Current:** Using `Subject<AppMsg>` for global message dispatch
```fsharp
// Types.fs
module AppEvents =
  let dispatchSubject = Subject<AppMsg>.broadcast
  let dispatch msg = dispatchSubject.OnNext msg

// Usage
AppEvents.dispatch(GuiTriggered GuiAction.StartNewGame)
```

**Problem:**
- Bypasses Elmish command model
- Hard to track message flow
- Breaks determinism and testability

**Mibo Way:**
```fsharp
// Return commands from update
match msg with
| ButtonClicked ->
  model, Cmd.ofMsg StartNewGame

// Or batch multiple messages
model, Cmd.batch [
  Cmd.ofMsg SaveMap
  Cmd.ofMsg TransitionToGameplay
]
```

---

#### 3. **Heavy FDA Usage Without Clear Benefit**
**Current:** Using `cval`, `AVal.force` in App layer without clear reactive patterns
```fsharp
// EditorRenderer.fs
let blockMap = editorState.BlockMap |> AVal.force
let layer = editorState.CurrentLayer |> AVal.force
let cursor = editorState.GridCursor |> AVal.force
```

**Problem:**
- FDA overhead without leveraging incremental computation
- Forces all values every frame anyway
- Adds complexity without benefit

**Mibo Way:**
- Model is just plain immutable records
- FDA is optional (can still use it judiciously)
- Most state is transformed directly, not adaptively

---

#### 4. **Non-MonoGame Services Separated, But Not Passed Through**
**Current:** `AppServices` created separately but not passed to `update`
```fsharp
// State.fs
let update (appEnv: AppEnv) msg model : struct (AppModel * Cmd<AppMsg>) =
  // appEnv has services but not used in init
```

**Problem:**
- Services available in `update` but NOT in `init` or `view`
- Inconsistent access pattern

**Mibo Way:**
```fsharp
// Services available via GameContext in init, view, AND can be captured in update
let init (ctx: GameContext) =
  let assets = MyAssetStore.create ctx
  { Model with Assets = assets }, Cmd.none

let update services msg model =
  // Use services here

// Or capture in program creation
let wrappedUpdate = update services
Program.mkProgram init wrappedUpdate
```

---

#### 5. **Scene Management via Side Effects**
**Current:** Scene transitions via global subject, model holds session separately
```fsharp
type AppModel = {
  CurrentScene: SceneState
  EditorSession: EditorSession voption  // Separate from scene!
}
```

**Problem:**
- Mutable session state duplicates scene state
- Scene transition bypasses model update
- Hard to reason about state ownership

**Mibo Way:**
```fsharp
type Model = {
  CurrentScene: Scene
}

and Scene =
  | MainMenu
  | Editor of EditorState
  | Gameplay of GameplayState

// Scene transition is just returning a new model
match msg with
| StartEditor ->
  { model with CurrentScene = Editor (EditorState.init()) }, Cmd.none
```

---

### Missing Mibo Features in Current App

1. **Scaling Levels Pattern**: No clear progression from Level 0 (pure MVU) to Level 3 (phases)
2. **Simulation Transaction**: No `Tick`-only mutation pattern (mutations scattered)
3. **Phase Pipelines**: No `System.pipeMutable`, `System.snapshot`, `System.pipe` usage
4. **Fixed Timestep**: Not using Mibo's built-in fixed-step helpers
5. **Semantic Input**: Not using `InputMap` or `InputMapper.subscribe`
6. **Asset Service**: Not using `Program.withAssets` or `Assets.texture` helpers

---

## Part 4: Proposed Initial Design

### Design Strategy: Incremental Migration

**Goal:** Redesign Kipo to be a full Mibo application while preserving existing features and learning from failed App attempt.

**Anti-Goals:**
- Don't rewrite everything at once
- Don't lose existing working features
- Don't abandon FDA entirely (use cautiously)

---

### Phase 0: Core Redesign (Pure MVU)

**Objective:** Establish a clean Elmish foundation with simple scene management

#### 0.1: Define Core Model

```fsharp
// Pomo.Core/MiboApp/Model.fs
module Pomo.Core.MiboApp.Model

type Scene =
  | MainMenu
  | Gameplay of GameplayModel
  | Editor of EditorModel

and GameplayModel = {
  // To be designed
  Entities: Map<EntityId, Entity>
  // ...
}

and EditorModel = {
  BlockMap: BlockMapDefinition
  Camera: EditorCamera
  CurrentLayer: int
  GridCursor: GridCell3D voption
  SelectedBlockType: string voption
  CurrentRotation: int
}

type AppModel = {
  CurrentScene: Scene
}

and EditorCamera = {
  Position: Vector3
  Rotation: float32
  Zoom: float32
}
```

#### 0.2: Define Messages

```fsharp
// Pomo.Core/MiboApp/Messages.fs
type AppMsg =
  | Tick of GameTime
  | FixedStep of float32
  | SceneTransition of Scene
  // Input
  | InputMapped of ActionState<AppAction>
  // Editor
  | EditorMsg of EditorMsg
  // Gameplay
  | GameplayMsg of GameplayMsg

and AppAction =
  | MenuUp | MenuDown | MenuSelect | MenuBack
  // Editor actions
  | CameraForward | CameraBack | CameraLeft | CameraRight
  | CameraRotateLeft | CameraRotateRight
  | ZoomIn | ZoomOut
  | PlaceBlock | RemoveBlock
  | Undo | Redo
  | Playtest

and EditorMsg =
  | CameraUpdated of EditorCamera
  | CursorMoved of GridCell3D voption
  | BlockPlaced of GridCell3D * string * int
  | BlockRemoved of GridCell3D
  | LayerChanged of int
  | RotationChanged of int
  | UndoRequested
  | RedoRequested

and GameplayMsg =
  // To be designed
  | PlayerMoved of WorldPosition
  | SkillCast of SkillId * SkillTarget
```

#### 0.3: Init Function

```fsharp
// Pomo.Core/MiboApp/Init.fs
module Pomo.Core.MiboApp.Init

let init (ctx: GameContext) : struct (AppModel * Cmd<AppMsg>) =
  {
    CurrentScene = MainMenu
  },
  Cmd.none
```

#### 0.4: Update Function (Level 2 Pattern)

```fsharp
// Pomo.Core/MiboApp/Update.fs
module Pomo.Core.MiboApp.Update

let update (services: AppServices) msg model =
  match msg with
  | SceneTransition scene ->
    { model with CurrentScene = scene }, Cmd.none

  | InputMapped actions ->
    // Store actions for next Tick
    // (Depends on scene)
    model, Cmd.none

  | Tick gt ->
    // ONLY place where simulation runs
    match model.CurrentScene with
    | MainMenu -> model, Cmd.none
    | Editor editorModel ->
      let newEditor = EditorUpdate.tick services gt editorModel
      { model with CurrentScene = Editor newEditor }, Cmd.none
    | Gameplay gameModel ->
      let newGame, cmds = GameplayUpdate.tick services gt gameModel
      { model with CurrentScene = Gameplay newGame }, cmds

  | EditorMsg edMsg ->
    match model.CurrentScene with
    | Editor editorModel ->
      let newEditor = EditorUpdate.handleMsg edMsg editorModel
      { model with CurrentScene = Editor newEditor }, Cmd.none
    | _ -> model, Cmd.none

  | GameplayMsg gpMsg ->
    match model.CurrentScene with
    | Gameplay gameModel ->
      let newGame, cmds = GameplayUpdate.handleMsg services gpMsg gameModel
      { model with CurrentScene = Gameplay newGame }, cmds
    | _ -> model, Cmd.none
```

#### 0.5: View Function (RenderBuffer Pattern)

```fsharp
// Pomo.Core/MiboApp/View.fs
module Pomo.Core.MiboApp.View

// Custom render buffer types
type EditorRenderBuffer = {
  Blocks: ResizeArray<MeshCommand>
  Grid: ResizeArray<VertexPositionColor>
  Cursor: ResizeArray<VertexPositionColor>
  Ghost: MeshCommand voption
}

let view (ctx: GameContext) (model: AppModel) (buffer: EditorRenderBuffer) =
  match model.CurrentScene with
  | MainMenu -> ()  // No 3D rendering
  | Editor editorModel ->
    EditorView.populateBuffer ctx editorModel buffer
  | Gameplay gameModel ->
    GameplayView.populateBuffer ctx gameModel buffer
```

#### 0.6: Subscriptions

```fsharp
// Pomo.Core/MiboApp/Subscriptions.fs
module Pomo.Core.MiboApp.Subscriptions

let subscribe (ctx: GameContext) (model: AppModel) =
  let inputMap = AppInputMap.create model.CurrentScene

  Sub.batch [
    InputMapper.subscribe (fun () -> inputMap) InputMapped ctx
  ]
```

#### 0.7: Program Setup

```fsharp
// Pomo.Core/MiboApp/Program.fs
module Pomo.Core.MiboApp.Program

let create () =
  let services = AppServices.create()

  let wrappedUpdate msg model =
    Update.update services msg model

  Program.mkProgram Init.init wrappedUpdate
  |> Program.withInput
  |> Program.withAssets
  |> Program.withRenderer (fun game -> EditorRenderer.create game)
  |> Program.withRenderer (fun game -> UIRenderer.create game)
  |> Program.withFixedStep {
    StepSeconds = 1.0f / 60.0f
    MaxStepsPerFrame = 5
    MaxFrameSeconds = ValueSome 0.25f
    Map = FixedStep
  }
  |> Program.withTick Tick
  |> Program.withSubscription Subscriptions.subscribe
  |> Program.withConfig (fun (game, gdm) ->
    game.IsMouseVisible <- true
    game.Content.RootDirectory <- "Content"
    // ...
  )
```

---

### Phase 1: Extract Editor to Mibo Pattern

**Objective:** Migrate the 3D Block Map Editor to pure Mibo, keeping it simple

#### What Works:
- Editor is relatively isolated
- Clear state (camera, cursor, block palette)
- No complex entity interactions

#### Migration Steps:

1. **Remove FDA from EditorModel** (unless actively needed)
   - Replace `cval<Vector3>` → `Vector3`
   - Replace `cval<GridCell3D voption>` → `GridCell3D voption`

2. **Input via Subscriptions**
   - Remove `AppEditorInput.processInput` from renderer
   - Add `EditorAction` DU (CameraMove, Rotate, PlaceBlock, etc.)
   - Use `InputMapper.subscribe` with `InputMap<EditorAction>`
   - Handle in `update EditorMsg`

3. **Camera Updates in `update EditorMsg`, Not Renderer**
   - `EditorMsg.CameraMoved of Vector3`
   - Pure function: `EditorCamera.applyInput : ActionState<EditorAction> -> float32 -> EditorCamera -> EditorCamera`

4. **Undo/Redo as Commands**
   - Store history in `EditorModel.History : BlockMapHistory`
   - `EditorMsg.Undo` → update model with previous state
   - `EditorMsg.Redo` → update model with next state

5. **Renderer Only Renders**
   - `EditorRenderer.Draw(ctx, model, gt)` → read `model.CurrentScene.Editor` → populate buffer → render

---

### Phase 2: Extract Gameplay Core (Level 1-2)

**Objective:** Build a minimal gameplay loop (player movement, simple combat)

#### Minimal Gameplay Model:

```fsharp
type GameplayModel = {
  // World state
  BlockMap: BlockMapDefinition
  Entities: Map<EntityId, Entity>

  // Input state (for Tick processing)
  PlayerActions: ActionState<GameplayAction>

  // Camera
  Camera: IsometricCamera

  // Time
  Time: GameTime
}

type Entity = {
  Id: EntityId
  Position: WorldPosition
  Velocity: Vector3
  Resources: Resources
  // ...
}
```

#### Update Pattern (Level 2):

```fsharp
let tick (services: AppServices) (gt: GameTime) (model: GameplayModel) =
  // 1. Read input buffer
  let actions = model.PlayerActions

  // 2. Run simulation (physics, AI, combat)
  let newEntities =
    model.Entities
    |> Map.map (fun id entity ->
      Physics.integrate entity actions gt)

  let newModel = {
    model with
      Entities = newEntities
      Time = gt
  }

  newModel, Cmd.none
```

---

### Phase 3: Add Systems as Modules (Level 3)

**Objective:** Scale gameplay to support AI, skills, particles using Mibo's phase pipeline

#### Use `System.pipeMutable` and `System.snapshot`

```fsharp
// Pomo.Core/MiboApp/GameplaySystems.fs

module Physics =
  let update (dt: float32) (model: GameplayModel) =
    // Mutate entity positions
    for kv in model.Entities do
      let entity = kv.Value
      entity.Position <- entity.Position + entity.Velocity * dt
    model

module AI =
  let update (services: AppServices) (dt: float32) (snapshot: GameplaySnapshot) =
    // Readonly query - generate commands
    // ...
    []  // Return list of commands

// In Update.fs
let tick (services: AppServices) (gt: GameTime) (model: GameplayModel) =
  let dt = float32 gt.ElapsedGameTime.TotalSeconds

  System.start model
  // Mutable phase
  |> System.pipeMutable (Physics.update dt)
  |> System.pipeMutable (Particles.update dt)
  // Snapshot
  |> System.snapshot GameplayModel.toSnapshot
  // Readonly phase
  |> System.pipe (AI.update services dt)
  // Finish
  |> System.finish GameplayModel.fromSnapshot
```

---

### Phase 4: Asset Management via Mibo

**Objective:** Replace custom lazy loading with `Program.withAssets`

#### Steps:

1. **Enable asset service**
   ```fsharp
   Program.mkProgram init update
   |> Program.withAssets
   ```

2. **Custom store on top of IAssets**
   ```fsharp
   // Pomo.Core/MiboApp/Stores.fs
   module SkillStore =
     let load (ctx: GameContext) =
       Assets.fromJsonCache "Content/Skills.json" SkillDecoder.decode ctx
   ```

3. **Use in init**
   ```fsharp
   let init (ctx: GameContext) =
     let skills = SkillStore.load ctx
     let models = ModelStore.load ctx

     {
       CurrentScene = MainMenu
       Assets = { Skills = skills; Models = models }
     }, Cmd.none
   ```

---

### Key Principles for Redesign

1. **Model is Immutable** - No `cval` unless you have a clear incremental computation need
2. **Update is Pure** - Returns `(Model, Cmd<Msg>)`, no side effects
3. **View is Pure** - Reads model, populates buffer, doesn't mutate anything
4. **Subscriptions for Input** - Use `InputMapper.subscribe`, not direct state polling
5. **Cmd for Events** - Use `Cmd.ofMsg`, `Cmd.batch` instead of event bus
6. **Services Passed Explicitly** - Capture in `wrappedUpdate`, not in global scope
7. **Scenes as Union Types** - `Scene = MainMenu | Gameplay | Editor`
8. **Tick is Simulation Transaction** - All gameplay logic in `Tick`, other messages just buffer

---

## Part 5: Migration Roadmap

### Step 1: Create New Namespace (Parallel to Old)
- Create `Pomo.Core/MiboApp/` directory
- Implement Phase 0 (Core Redesign) alongside existing `App/`
- Run both in parallel (switch via flag)

### Step 2: Port Editor (Phase 1)
- Migrate editor logic to Mibo pattern
- Test thoroughly
- Remove old `App/EditorRenderer.fs` and `App/EditorInput.fs`

### Step 3: Port Gameplay (Phase 2-3)
- Start with minimal gameplay (player movement, camera)
- Gradually add systems (AI, combat, particles) using Level 3 pattern
- Remove old `CompositionRoot.fs` and scene management

### Step 4: Full Migration
- Delete `Pomo.Core/App/`
- Rename `Pomo.Core/MiboApp/` → `Pomo.Core/App/`
- Update all entry points (`Pomo.WindowsDX`, `Pomo.DesktopGL`)

---

## Conclusion

### What We Learned from Failed App Attempt

1. **Don't mix paradigms** - Either Mibo or GameComponent, not both
2. **Respect Elmish boundaries** - Input in subscriptions, logic in update, rendering in view
3. **FDA is optional** - Don't force it without clear benefit
4. **Services must be threaded through** - Pass to init, update, view consistently
5. **Event bus is an anti-pattern in Elmish** - Use `Cmd`

### Success Criteria for Redesign

✅ Clear separation: Input → Update → View
✅ All state mutations in `update` (or `Tick`)
✅ Renderers only render
✅ Scene management via `Model.CurrentScene`
✅ Services passed explicitly
✅ FDA used judiciously (where it adds value)
✅ Incremental migration (don't lose features)

# Editor UI Integration Plan - Pomo.Lib

> **Context**: Integrate Myra UI into the Pomo.Lib voxel editor, mirroring the Pomo.Core editor UI implementation while adapting to pure Elmish architecture (no FSharp.Data.Adaptive bindings).

> **Architecture Pattern**: Pure Elmish state + manual Myra widget updates via messages. UI Desktop rendered after Mibo 3D scene.

---

## Overview

The goal is to add UI overlay to the voxel editor using Myra, enabling:

- Block palette selection for switching between block types
- HUD elements (layer indicator, brush mode, block info)
- Help overlay with keyboard shortcuts
- Future: save/load dialogs, settings panels

**Key Differences from Pomo.Core:**

| Pomo.Core | Pomo.Lib |
|-----------|-----------|
| FSharp.Data.Adaptive reactive state (aval, cval, transact) | Pure Elmish state transformation |
| Automatic widget binding via `bindText`, `bindBackground` etc. | Manual widget updates in message handlers |
| Mutable camera shared between UI and game loop | Camera in EditorModel, accessed directly |

---

## 1. Myra DSL Migration

**File:** `Pomo.Lib/UI/MyraExtensions.fs`

**Source:** `Pomo.Core/UI/MyraExtensions.fs` (simplified, remove adaptive bindings)

### Structure

```fsharp
namespace Pomo.Lib.UI

open System
open System.Runtime.CompilerServices
open Microsoft.Xna.Framework
open Myra.Graphics2D
open Myra.Graphics2D.UI

// Widget disposal tracking (keeps Myra clean)
module WidgetSubs =
  let private store = ConditionalWeakTable<Widget, CompositeDisposable>()

  let get(w: Widget) =
    match store.TryGetValue(w) with
    | true, cd -> cd
    | false, _ ->
      let cd = new CompositeDisposable()
      store.Add(w, cd)
      cd

  let dispose(w: Widget) =
    match store.TryGetValue(w) with
    | true, cd ->
      cd.Dispose()
      store.Remove(w) |> ignore
    | false, _ -> ()
```

### Core Widget Helpers (W module)

**Keep these from Pomo.Core:**
- `background` - Set panel/widget background color
- `width`, `height`, `size` - Widget dimensions
- `hAlign`, `vAlign` - Alignment
- `margin`, `padding` - Spacing
- `left`, `top` - Position
- `enabled`, `opacity` - Widget state
- `text`, `textColor` - Label styling
- `spacing` - Stack panel spacing

**Keep type-specific children helpers:**
- `childrenP` - Add widgets to Panel
- `childrenH` - Add widgets to HorizontalStackPanel
- `childrenV` - Add widgets to VerticalStackPanel

### Type-Specific Modules

**Label module:**
```fsharp
module Label =
  let inline create(text: string) = Label(Text = text)
  let inline colored (text: string) (color: Color) =
    Label(Text = text, TextColor = color)
```

**Panel module:**
```fsharp
module Panel =
  let inline create() = Panel()
  let inline sized (width: int) (height: int) =
    Panel(Width = Nullable width, Height = Nullable height)
```

**HStack module:**
```fsharp
module HStack =
  let inline create() = HorizontalStackPanel()
  let inline spaced(spacing: int) = HorizontalStackPanel(Spacing = spacing)
```

**VStack module:**
```fsharp
module VStack =
  let inline create() = VerticalStackPanel()
  let inline spaced(spacing: int) = VerticalStackPanel(Spacing = spacing)
```

**Grid module:**
```fsharp
module Grid =
  let inline create() = Grid()
  let inline spaced (columnSpacing: int) (rowSpacing: int) =
    Grid(ColumnSpacing = columnSpacing, RowSpacing = rowSpacing)
  let inline columns (proportions: Proportion list) (grid: Grid) =
    for p in proportions do
      grid.ColumnsProportions.Add(p)
    grid
  let inline rows (proportions: Proportion list) (grid: Grid) =
    for p in proportions do
      grid.RowsProportions.Add(p)
    grid
```

**Btn module:**
```fsharp
module Btn =
  let inline create(text: string) = Button(Content = Label(Text = text))
  let inline empty() = Button()
  let inline content (widget: Widget) (btn: Button) =
    btn.Content <- widget
    btn
  let inline onClick (handler: unit -> unit) (btn: Button) =
    let sub = btn.Click.Subscribe(fun _ -> handler())
    WidgetSubs.get(btn).Add(sub)
    btn
```

### What to REMOVE (adaptive-specific)

Delete from Pomo.Core version:
- `bindText`, `bindTextColor`, `bindColor`, `bindOpacity`
- `bindCurrentValue`, `bindMaxValue`
- `bindColorFill`, `bindColorBackground`, `bindBackgroundBrush`
- `bindCooldownEndTime`, `bindCooldownColor`, `bindCooldownDuration`
- `bindBgColor`, `bindKind`, `bindTotalDurationSeconds`
- `bindWorldTime`, `bindColorBuff`, `bindColorDebuff`, `bindColorDot`
- `bindIsInCombat`, `playerId`, `bindMapFactions`, `bindViewBounds`
- `bindChild`, `bindChildren`, `bindBackgroundBrush` (adaptive versions)
- Domain-specific helpers: `cooldownEndTime`, `effectKind`, `isInCombat`, etc.

**Reason:** Pomo.Lib uses pure Elmish state, not `aval`/`cval`. Widget state is updated manually in message handlers.

---

## 2. Editor UI Types

**File:** `Pomo.Lib/Editor/EditorTypes.fs`

### Extend EditorModel

```fsharp
[<Struct>]
type EditorModel = {
  BlockMap: BlockMap.BlockMapModel
  Camera: Camera.CameraModel
  Brush: Brush.BrushModel
  Actions: ActionState<EditorInputAction>
  PrevMouseState: MouseState
  Desktop: Desktop voption  // NEW: Myra Desktop root widget
}
```

### Add UI Messages

```fsharp
type EditorMsg =
  | BlockMapMsg of blockMap: BlockMap.Msg
  | CameraMsg of camera: Camera.Msg
  | BrushMsg of brush: Brush.Msg
  | InputMapped of ActionState<EditorInputAction>
  | Tick of gt: GameTime
  | UIMsg of UIMsg  // NEW: UI-specific messages

and UIMsg =
  | InitializeUI  // Create and attach Myra Desktop
  | ShowHelp    // Show F1 help overlay
  | HideHelp    // Hide F1 help overlay
```

---

## 3. Editor UI Module

**File:** `Pomo.Lib/UI/EditorUI.fs`

### HUD Elements Build

```fsharp
namespace Pomo.Lib.UI

open Myra.Graphics2D.UI
open Myra.Graphics2D.Brushes
open Pomo.Lib.UI
open Pomo.Lib.Editor

module EditorUI =
  let buildRoot (model: Editor.EditorModel) : Panel =
    // Main UI panel (full screen transparent container)
    let panel = Panel.create()

    // Bottom-left HUD container
    let container =
      VStack.spaced 8
      |> W.hAlign HorizontalAlignment.Left
      |> W.vAlign VerticalAlignment.Bottom
      |> W.padding 20

    // Header
    let title = Label.create "MAP EDITOR" |> W.textColor Color.Yellow
    let helpHint = Label.create "F1 for help" |> W.size 120 12

    // Layer indicator
    let layerLabel =
      Label.create $"Layer: {model.Camera.CurrentLayer}"
      |> W.size 100 30

    // Brush mode
    let brushLabel =
      Label.create $"Brush: {model.Brush.Mode}"
      |> W.size 120 30

    // Block type (placeholder - will update from palette later)
    let blockLabel =
      Label.create $"Block: {model.Brush.SelectedBlockId |> UMX.untag}"
      |> W.size 150 30

    // Collision toggle
    let collisionLabel =
      Label.create(if model.Brush.CollisionEnabled then "Collision: On" else "Collision: Off")
      |> W.size 120 30

    // Collision button
    let collisionBtn =
      Btn.empty()
      |> Btn.content collisionLabel
      |> W.size 120 30
      |> Btn.onClick(fun () ->
        // TODO: Send collision toggle message
        ())

    // Assemble HUD
    container
    |> W.childrenV [
      title
      helpHint
      layerLabel
      brushLabel
      blockLabel
      collisionBtn
    ]

    panel.Widgets.Add(container)
    panel
```

### Help Overlay Build

```fsharp
  let buildHelp() : Panel =
    let controls = [
      "Tab", "Toggle Camera Mode"
      "WASD", "Move Camera"
      "Scroll", "Zoom"
      "Page Up/Down", "Change Layer"
      "Left Click", "Place Block"
      "Right Click", "Remove Block"
      "Q / E", "Rotate Brush"
      "C", "Toggle Collision"
      "1 / 2", "Brush Mode"
    ]

    let helpRows =
      controls
      |> List.map(fun (key, action) ->
        HStack.spaced 10
        |> W.childrenH [
          Label.create key
          |> W.textColor Color.Yellow
          |> W.width 100
          |> W.hAlign HorizontalAlignment.Right

          Label.create action
          |> W.textColor Color.White
          |> W.hAlign HorizontalAlignment.Left
        ]
        :> Widget)

    let helpList =
      VStack.spaced 5
      |> W.childrenV(
        (Label.create "--- EDITOR CONTROLS (F1) ---"
           |> W.hAlign HorizontalAlignment.Center
           |> W.textColor Color.Cyan
          :> Widget)
        :: helpRows
      )

    let helpContainer =
      Panel.create()
      |> W.hAlign HorizontalAlignment.Center
      |> W.vAlign VerticalAlignment.Center
      |> W.padding 20

    helpContainer.Background <- SolidBrush(Color(0, 0, 0, 220))
    helpContainer.Widgets.Add(helpList)
    helpContainer
```

---

## 4. Entry Integration

**File:** `Pomo.Lib/Editor/Entry.fs`

### Update View Function

**Current view:** Renders 3D scene with Mibo's `RenderBuffer`

**New view:** Renders 3D scene, then renders Myra Desktop

```fsharp
let view
  (env: AppEnv)
  (ctx: GameContext)
  (model: EditorModel)
  (buffer: RenderBuffer<unit, RenderCommand>)
  : unit =
  // Mibo 3D rendering (grid, ghost block, cursor)
  buffer.Camera(model.Camera.Camera).Clear Color.CornflowerBlue |> ignore

  let layerY = float32 model.Camera.CurrentLayer
  drawGrid buffer layerY

  let cursorCellOpt = EditorCursor.getCursorCell env model.Camera.Camera layerY
  Brush.view ctx model.Brush cursorCellOpt buffer

  cursorCellOpt |> Option.iter (drawCursorHighlight buffer)

  // Submit 3D commands to GPU
  buffer.Submit()

  // Render Myra UI (2D overlay on top of 3D scene)
  model.Desktop
  |> ValueOption.iter (fun desktop -> desktop.Render())
```

### Update Message Handling

```fsharp
let update
  (env: AppEnv)
  (msg: EditorMsg)
  (model: EditorModel)
  : struct (EditorModel * Cmd<EditorMsg>) =
  match msg with
  | BlockMapMsg subMsg ->
    let struct (subModel, cmd) = BlockMap.update env subMsg model.BlockMap
    { model with BlockMap = subModel }, cmd |> Cmd.map BlockMapMsg

  | CameraMsg subMsg ->
    let struct (subModel, cmd) = Camera.update env subMsg model.Camera
    { model with Camera = subModel }, cmd |> Cmd.map CameraMsg

  | BrushMsg subMsg ->
    let struct (subModel, cmd) = Brush.update subMsg model.Brush
    { model with Brush = subModel }, cmd |> Cmd.map BrushMsg

  | InputMapped actions ->
    let result = InputHandler.processInput env actions model
    // ... existing logic ...

  | Tick time ->
    // ... existing tick logic ...

  | UIMsg InitializeUI ->
    let desktop = new Desktop(Root = EditorUI.buildRoot model)
    { model with Desktop = ValueSome desktop }, Cmd.none

  | UIMsg ShowHelp ->
    // TODO: Add help overlay as child of Desktop
    model, Cmd.none

  | UIMsg HideHelp ->
    // TODO: Remove help overlay from Desktop
    model, Cmd.none
```

### Update Init Function

**Queue UI initialization as first message:**

```fsharp
let init
  (env: AppEnv)
  (ctx: GameContext)
  : struct (EditorModel * Cmd<EditorMsg>) =
  let struct (es, cmd) = Editor.Entry.init env ctx

  {
    es with Desktop = ValueNone  // Clear existing desktop
  },
  Cmd.batch [cmd; Cmd.ofMsg (EditorMsg.UIMsg InitializeUI)]
```

---

## 5. Project File Updates

**File:** `Pomo.Lib/Pomo.Lib.fsproj`

**Add to compile order (before Library.fs):**

```xml
<ItemGroup>
  <!-- Existing files -->
  <Compile Include="Domain.fs" />
  <Compile Include="Services/FileSystem.fs" />
  <Compile Include="Services/EditorCursor.fs" />
  <Compile Include="Services/BlockMapPersistence.fs" />
  <Compile Include="AppEnv.fs" />

  <!-- NEW: UI files -->
  <Compile Include="UI/MyraExtensions.fs" />
  <Compile Include="UI/EditorUI.fs" />

  <!-- Editor files -->
  <Compile Include="Editor/Domain.fs" />
  <Compile Include="Editor/EditorTypes.fs" />
  <Compile Include="Editor/Subsystems/Brush.fs" />
  <Compile Include="Editor/Subsystems/Camera.fs" />
  <Compile Include="Editor/Subsystems/BlockMap.fs" />
  <Compile Include="Editor/Subsystems/Cursor.fs" />
  <Compile Include="Editor/InputHandler.fs" />
  <Compile Include="Editor/Entry.fs" />

  <!-- Gameplay files -->
  <Compile Include="Gameplay/Domain.fs" />
  <Compile Include="Gameplay/Entry.fs" />

  <!-- Factory and Library -->
  <Compile Include="EnvFactory.fs" />
  <Compile Include="Library.fs" />
</ItemGroup>
```

**Ordering:**
1. `Domain.fs` - Core types
2. Services - Services that domain may reference
3. `AppEnv.fs` - Composition root
4. `UI/MyraExtensions.fs` - UI helpers (no dependencies on editor types)
5. `Editor/Domain.fs` - Editor-specific types
6. `Editor/EditorTypes.fs` - Editor model/msg (extends with Desktop)
7. Subsystems - Component subsystems
8. `UI/EditorUI.fs` - UI builder (uses EditorModel)
9. `Editor/InputHandler.fs` - Input processing
10. `Editor/Entry.fs` - Main editor orchestration
11. Gameplay files
12. `EnvFactory.fs` - Creates AppEnv
13. `Library.fs` - Program entry

---

## 6. Key Design Decisions

### No Reactive Bindings

**Decision:** Remove all `bind*` functions from Myra DSL

**Reasoning:**
- Pomo.Lib uses pure Elmish state (immutable Model + Msg)
- No `aval` (adaptive values) or `cval` (cells) in Pomo.Lib
- Reactive bindings add complexity without benefit
- Manual updates in message handlers are simpler and predictable

**Alternative if needed later:**
- Add simple `setLabelText` / `setLabelColor` helpers if repetitive
- Keep non-reactive to avoid FSharp.Data.Adaptive dependency

### Desktop Lifecycle

**Initialization:**
- Created via `UIMsg InitializeUI` message
- Stored in `EditorModel.Desktop` (voption)
- Created once after Elmish init completes

**Rendering:**
- Called after Mibo's `buffer.Submit()` in `view` function
- Myra's `desktop.Render()` draws 2D sprites on top of 3D scene
- Uses same `Game.GraphicsDevice` (Myra auto-detects)

**Disposal:**
- Automatic when model is replaced (old Desktop goes out of scope)
- `WidgetSubs` tracks subscriptions for clean disposal

### UI Update Strategy

**Manual vs Reactive:**

| Aspect | Pomo.Core | Pomo.Lib |
|---------|-----------|-----------|
| State updates | `transact(fun () -> state.Value <- newValue)` | Pure function returning new state |
| Widget updates | Automatic via `bindText` etc. | Manual in update handler |
| Complexity | Low (one line) | Medium (explicit update) |
| Predictability | Medium (reactive magic) | High (explicit data flow) |

**Example manual update:**
```fsharp
// In update function:
| BrushMsg (Brush.Msg.SetMode newMode) ->
  let struct (newBrush, cmd) = Brush.update (Brush.Msg.SetMode newMode) model.Brush
  let newModel = { model with Brush = newBrush }

  // Manual widget update:
  match newModel.Desktop with
  | ValueSome desktop ->
    // Find brush label widget and update text
    for widget in desktop.Root.Widgets do
      match widget with
      | :? Label as label when label.Text.StartsWith "Brush:" ->
        label.Text <- $"Brush: {newMode}"
      | _ -> ()
  | ValueNone -> ()

  newModel, cmd |> Cmd.map BrushMsg
```

### Help Overlay Implementation

**Simple toggle (no state needed initially):**

1. Press F1 → Send `UIMsg ShowHelp`
2. Create help overlay panel: `EditorUI.buildHelp()`
3. Add as child of Desktop
4. Press F1 or ESC → Remove help overlay (or toggle visibility)

**Future enhancement:**
- Track help visibility in EditorModel
- Prevent block placement when help is shown

---

## 7. Testing Checklist

**After implementation, verify:**

- [ ] Build compiles successfully
- [ ] Myra Desktop renders overlay (check `desktop.Visible` and `desktop.Root`)
- [ ] HUD elements display (layer, brush mode, block label)
- [ ] Help overlay shows/hides on F1
- [ ] UI doesn't block 3D rendering (check depth sorting or transparent panel)
- [ ] Button clicks trigger messages (collision toggle test)
- [ ] Mouse interaction with 3D scene still works (cursor, block placement)
- [ ] No memory leaks (check `WidgetSubs` disposal on Desktop rebuild)

**Test steps:**
```bash
# Build
dotnet build Pomo.slnx

# Run
dotnet run --project Pomo.DesktopGL/Pomo.DesktopGL.fsproj

# Verify in-game:
# 1. See HUD elements bottom-left
# 2. Press F1, see help overlay
# 3. Press WASD, camera moves (UI doesn't interfere)
# 4. Click to place block (UI doesn't block 3D input)
```

---

## 8. Next Phase: Block Palette (Future)

**Once UI framework is working:**

### Files Needed
1. `Pomo.Lib/Content/BlockPalette.json` - Block type manifest
2. `Pomo.mgcb` additions - FBX entries for starter blocks
3. `Pomo.Lib/Services/BlockPalette.fs` - Palette loading service

### UI Structure Preview
```fsharp
let buildPalette (model: EditorModel) : Panel =
  let panel = Panel.sized 400 300
  |> W.hAlign HorizontalAlignment.Right
  |> W.vAlign VerticalAlignment.Bottom
  |> W.padding 12

  let grid = Grid.spaced 4 4 |> Grid.autoColumns 4

  // For each block type in palette...
  let btn = Btn.create blockType.Name
  |> W.size 100 30
  |> Btn.onClick(fun () ->
    // Send message to change selected block type
  )

  let scroll = ScrollViewer(Content = grid)
  panel.Widgets.Add(scroll)
  panel
```

### Integration Steps
1. Add `UIMsg` for palette visibility toggle
2. Store selected block type in EditorModel (or use existing Brush.SelectedBlockId)
3. Load palette from JSON at init
4. Build palette UI dynamically from palette entries
5. Update `EditorUI.buildRoot` to include palette panel
6. Handle button clicks → update `Brush.SelectedBlockId`

---

## Summary

**Phase 1: Foundation** (This plan)
- Migrate Myra DSL (simplified)
- Add EditorModel.Desktop and UIMsg
- Build basic HUD + help overlay
- Integrate Myra rendering into Entry.view

**Phase 2: Block Palette** (Future)
- Create BlockPalette.json with FBX file list
- Add FBX entries to Pomo.mgcb
- Build palette service
- Add palette selection UI

**Phase 3: Advanced UI** (Future)
- Save/Load dialogs
- Map settings panel
- Undo/Redo visualization
- Tooltips and help system

---

## Dependencies

**Required NuGet packages (already in Pomo.Lib.fsproj):**
- `Myra` (v1.5.10) - UI framework
- `Mibo` (v1.*) - Game framework
- `MonoGame.Framework.Native` (v3.8.*) - Graphics

**No additional dependencies needed.**

---

## Risks and Mitigations

| Risk | Mitigation |
|-------|------------|
| Myra depth conflict with Mibo 3D rendering | Test that Myra draws on top (render after `buffer.Submit()`) |
| Widget disposal memory leaks | Use `WidgetSubs` with `ConditionalWeakTable` (same as Pomo.Core) |
| UI blocks 3D mouse input | **Skipping mouse-over detection for now** (per user request) |
| UI rebuild on every frame (performance) | Only rebuild on specific messages (InitializeUI), not every frame |
| Myra Font loading issues | Use existing `Fonts/Hud.spritefont` from Pomo.mgcb |

---

## References

- [Pomo.Lib/VOXEL_EDITOR_PLAN.md](./VOXEL_EDITOR_PLAN.md) - Main editor plan
- [Pomo.Lib/SERIALIZATION_PLAN.md](./SERIALIZATION_PLAN.md) - Serialization design
- [Pomo.Core/UI/MyraExtensions.fs](../../Pomo.Core/UI/MyraExtensions.fs) - Source DSL
- [Pomo.Core/Editor/UI.fs](../../Pomo.Core/Editor/UI.fs) - Source UI implementation
- [Myra Documentation](https://github.com/rds1983/Myra) - Myra API reference

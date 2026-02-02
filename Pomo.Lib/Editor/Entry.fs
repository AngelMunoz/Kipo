namespace Pomo.Lib.Editor

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open Mibo.Elmish
open Mibo.Input
open Mibo.Rendering
open Mibo.Rendering.Graphics3D
open FSharp.UMX
open Pomo.Lib
open Pomo.Lib.Services
open Pomo.Lib.Editor.Subsystems
open Pomo.Lib.Editor.Subsystems.BlockMap
open Pomo.Lib.Editor.Subsystems.Camera
open Pomo.Lib.Editor.Subsystems.Brush
open Pomo.Lib.UI

module Entry =
  open Myra.Graphics2D.UI

  let private inputMap =
    InputMap.empty
    |> InputMap.key PanLeft Keys.Left
    |> InputMap.key PanRight Keys.Right
    |> InputMap.key PanForward Keys.Up
    |> InputMap.key PanBackward Keys.Down
    |> InputMap.key LayerUp Keys.PageUp
    |> InputMap.key LayerDown Keys.PageDown
    |> InputMap.key ResetCameraView Keys.Home
    |> InputMap.key ToggleCameraMode Keys.Tab
    |> InputMap.key RotateLeft Keys.Q
    |> InputMap.key RotateRight Keys.E
    |> InputMap.key EditorInputAction.ToggleCollision Keys.C
    |> InputMap.key SetBrushPlace Keys.D1
    |> InputMap.key SetBrushErase Keys.D2
    |> InputMap.key EditorInputAction.ShowHelp Keys.F1
    |> InputMap.key EditorInputAction.NextBlock Keys.O
    |> InputMap.key EditorInputAction.PrevBlock Keys.P

  let init
    (env: AppEnv)
    (ctx: GameContext)
    : struct (EditorModel * Cmd<EditorMsg>) =
    let baseModel = {
      BlockMap = BlockMap.init env BlockMapDefinition.empty
      Camera = Camera.init env
      Brush = Brush.init()
      Actions = ActionState.empty
      PrevMouseState = Mouse.GetState()
      Desktop = ValueNone
    }

    baseModel,
    Cmd.batch [
      Cmd.ofMsg(EditorMsg.Load "Content/BlockPalette.json")
      Cmd.ofMsg(EditorMsg.UIMsg InitializeUI)
    ]

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

      // Apply brush updates from input handler
      let mutable brushModel = model.Brush

      for brushMsg in result.BrushCommands do
        let struct (newBrush, _) = Brush.update brushMsg brushModel
        brushModel <- newBrush

      // Build camera commands from input result
      let cameraCmds =
        result.CameraCommands
        |> Seq.map(fun msg -> Cmd.ofMsg(CameraMsg msg))
        |> Cmd.batch

      // Check for ShowHelp action
      let uiCmd =
        if actions.Started.Contains EditorInputAction.ShowHelp then
          Cmd.ofMsg(
            EditorMsg.UIMsg(
              if model.Desktop.IsSome then
                UIMsg.HideHelp
              else
                UIMsg.ShowHelp
            )
          )
        else
          Cmd.none

      {
        model with
            Actions = result.Actions
            BlockMap = result.BlockMapModel
            Brush = brushModel
            PrevMouseState = result.PrevMouseState
      },
      Cmd.batch [ cameraCmds; uiCmd ]

    | Tick time ->
      // Continuous camera movement based on currently held actions
      let panSpeed = 0.1f
      let mutable panDelta = Vector2.Zero

      if model.Actions.Held.Contains PanLeft then
        panDelta <- panDelta + Vector2(-panSpeed, 0f)

      if model.Actions.Held.Contains PanRight then
        panDelta <- panDelta + Vector2(panSpeed, 0f)

      if model.Actions.Held.Contains PanForward then
        panDelta <- panDelta + Vector2(0f, panSpeed)

      if model.Actions.Held.Contains PanBackward then
        panDelta <- panDelta + Vector2(0f, -panSpeed)

      let cameraModel =
        if panDelta <> Vector2.Zero then
          let struct (newCamera, _) =
            Camera.update env (Pan panDelta) model.Camera

          newCamera
        else
          model.Camera

      // Update mouse state tracking
      let mouseState = Mouse.GetState()

      {
        model with
            Camera = cameraModel
            PrevMouseState = mouseState
      },
      Cmd.none

    | UIMsg InitializeUI ->
      let desktop = new Desktop(Root = EditorUI.buildRoot model)

      {
        model with
            Desktop = ValueSome desktop
      },
      Cmd.none

    | UIMsg ShowHelp ->
      let helpPanel = EditorUI.buildHelp()

      match model.Desktop with
      | ValueSome desktop ->
        desktop.Widgets.Add(helpPanel)
      | ValueNone -> ()

      model, Cmd.none

    | UIMsg HideHelp ->
      match model.Desktop with
      | ValueSome desktop ->
        desktop.Widgets.Clear()
        desktop.Widgets.Add(EditorUI.buildRoot model)
      | ValueNone -> ()

      model, Cmd.none

    | Load path ->
      let loadBlockMap env path = async {
        let! result = BlockMapPersistence.load env path

        match result with
        | Ok def -> return def
        | Error err ->
          let msg =
            match err with
            | FileNotFound p -> $"File not found: {p}"
            | AccessDenied p -> $"Access denied: {p}"
            | IOException m -> m
            | DeserializationError m -> m

          return failwith msg
      }

      model,
      Cmd.ofAsync
        (loadBlockMap env path)
        (fun def ->
          let blockMapModel = BlockMap.init env def
          SetBlockMapModel blockMapModel)
        (fun ex -> LoadFailed(IOException ex.Message))

    | Save path ->
      let saveBlockMap env definition path = async {
        let! result = BlockMapPersistence.save env definition path

        match result with
        | Ok() -> ()
        | Error err ->
          let msg =
            match err with
            | FileNotFound p -> $"File not found: {p}"
            | AccessDenied p -> $"Access denied: {p}"
            | IOException m -> m
            | DeserializationError m -> m

          failwith msg
      }

      model,
      Cmd.ofAsync
        (saveBlockMap env model.BlockMap.Definition path)
        (fun _ -> SaveComplete)
        (fun ex -> LoadFailed(IOException ex.Message))

    | SetBlockMapModel blockMapModel ->
      { model with BlockMap = blockMapModel }, Cmd.none

    | LoadFailed err -> model, Cmd.none

    | SaveComplete -> model, Cmd.none

  let subscribe (ctx: GameContext) (_model: EditorModel) : Sub<EditorMsg> =
    InputMapper.subscribeStatic inputMap InputMapped ctx

  // Grid size in cells - 20 wide, 32 deep
  let [<Literal>] GridWidth = 20
  let [<Literal>] GridDepth = 32

  let private drawGrid
    (buffer: RenderBuffer<unit, RenderCommand>)
    (layerY: float32)
    =
    let gridSpacing = GridDimensions.CellSize
    let gridColor = Color(0.3f, 0.3f, 0.3f)
    let maxLines = (GridWidth + 1) + (GridDepth + 1)

    // Build vertex array for all grid lines
    let verts = Array.zeroCreate(maxLines * 2)
    let mutable idx = 0

    // Vertical lines (along Z axis, at x = 0, 1, 2, ... GridWidth)
    for x in 0 .. GridWidth do
      let xPos = float32 x * gridSpacing

      verts.[idx] <-
        VertexPositionColor(
          Vector3(xPos, layerY, 0.0f),
          gridColor
        )

      verts.[idx + 1] <-
        VertexPositionColor(
          Vector3(xPos, layerY, float32 GridDepth * gridSpacing),
          gridColor
        )

      idx <- idx + 2

    // Horizontal lines (along X axis, at z = 0, 1, 2, ... GridDepth)
    for z in 0 .. GridDepth do
      let zPos = float32 z * gridSpacing

      verts.[idx] <-
        VertexPositionColor(
          Vector3(0.0f, layerY, zPos),
          gridColor
        )

      verts.[idx + 1] <-
        VertexPositionColor(
          Vector3(float32 GridWidth * gridSpacing, layerY, zPos),
          gridColor
        )

      idx <- idx + 2

    buffer.Lines(verts, maxLines) |> ignore

  let view
    (env: AppEnv)
    (ctx: GameContext)
    (model: EditorModel)
    (buffer: RenderBuffer<unit, RenderCommand>)
    : unit =
    buffer.Camera(model.Camera.Camera).Clear Color.CornflowerBlue |> ignore
    // Draw grid at current editing layer
    let layerY = float32 model.Camera.CurrentLayer * GridDimensions.CellSize
    drawGrid buffer layerY

    // Render all placed blocks
    BlockMap.view env ctx model.BlockMap buffer

    // Get cursor cell using the service and draw highlight
    let cursorCellOpt =
      EditorCursor.getCursorCell env model.Camera.Camera layerY

    // Draw ghost block preview
    Brush.view ctx model.Brush cursorCellOpt buffer

    match cursorCellOpt with
    | ValueSome cell ->
      let cursorColor = Color.Yellow
      let cellSize = GridDimensions.CellSize
      let min = Vector3(cell.X, cell.Y, cell.Z)
      let max = Vector3(cell.X + cellSize, cell.Y + cellSize, cell.Z + cellSize)
      // Draw wireframe box around cursor cell (12 edges)
      let verts = [|
        // Bottom face
        VertexPositionColor(Vector3(min.X, min.Y, min.Z), cursorColor)
        VertexPositionColor(Vector3(max.X, min.Y, min.Z), cursorColor)
        VertexPositionColor(Vector3(max.X, min.Y, min.Z), cursorColor)
        VertexPositionColor(Vector3(max.X, min.Y, max.Z), cursorColor)
        VertexPositionColor(Vector3(max.X, min.Y, max.Z), cursorColor)
        VertexPositionColor(Vector3(min.X, min.Y, max.Z), cursorColor)
        VertexPositionColor(Vector3(min.X, min.Y, max.Z), cursorColor)
        VertexPositionColor(Vector3(min.X, min.Y, min.Z), cursorColor)
        // Top face
        VertexPositionColor(Vector3(min.X, max.Y, min.Z), cursorColor)
        VertexPositionColor(Vector3(max.X, max.Y, min.Z), cursorColor)
        VertexPositionColor(Vector3(max.X, max.Y, min.Z), cursorColor)
        VertexPositionColor(Vector3(max.X, max.Y, max.Z), cursorColor)
        VertexPositionColor(Vector3(max.X, max.Y, max.Z), cursorColor)
        VertexPositionColor(Vector3(min.X, max.Y, max.Z), cursorColor)
        VertexPositionColor(Vector3(min.X, max.Y, max.Z), cursorColor)
        VertexPositionColor(Vector3(min.X, max.Y, min.Z), cursorColor)
        // Vertical edges
        VertexPositionColor(Vector3(min.X, min.Y, min.Z), cursorColor)
        VertexPositionColor(Vector3(min.X, max.Y, min.Z), cursorColor)
        VertexPositionColor(Vector3(max.X, min.Y, min.Z), cursorColor)
        VertexPositionColor(Vector3(max.X, max.Y, min.Z), cursorColor)
        VertexPositionColor(Vector3(max.X, min.Y, max.Z), cursorColor)
        VertexPositionColor(Vector3(max.X, max.Y, max.Z), cursorColor)
        VertexPositionColor(Vector3(min.X, min.Y, max.Z), cursorColor)
        VertexPositionColor(Vector3(min.X, max.Y, max.Z), cursorColor)
      |]

      buffer.Lines(verts, 12) |> ignore
    | ValueNone -> ()

    buffer
      .Custom(fun _ _ ->
        model.Desktop |> ValueOption.iter(_.Render()))
      .Submit()


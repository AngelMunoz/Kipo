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

module Entry =

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

  let init
    (env: AppEnv)
    (ctx: GameContext)
    : struct (EditorModel * Cmd<EditorMsg>) =
    {
      BlockMap = BlockMap.init env BlockMapDefinition.empty
      Camera = Camera.init env
      Brush = Brush.init()
      Actions = ActionState.empty
      PrevMouseState = Mouse.GetState()
    },
    Cmd.none

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

      {
        model with
            Actions = result.Actions
            BlockMap = result.BlockMapModel
            Brush = brushModel
            PrevMouseState = result.PrevMouseState
      },
      cameraCmds

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

  let subscribe (ctx: GameContext) (_model: EditorModel) : Sub<EditorMsg> =
    InputMapper.subscribeStatic inputMap InputMapped ctx

  let private drawGrid
    (buffer: RenderBuffer<unit, RenderCommand>)
    (layerY: float32)
    =
    let gridSize = 50
    let gridSpacing = 1.0f
    let gridColor = Color(0.3f, 0.3f, 0.3f)
    let maxLines = (gridSize * 2 + 1) * 2

    // Build vertex array for all grid lines
    let verts = Array.zeroCreate(maxLines * 2)
    let mutable idx = 0

    // Horizontal lines (along X axis)
    for z in -gridSize .. gridSize do
      let zPos = float32 z * gridSpacing

      verts.[idx] <-
        VertexPositionColor(
          Vector3(float32 -gridSize * gridSpacing, layerY, zPos),
          gridColor
        )

      verts.[idx + 1] <-
        VertexPositionColor(
          Vector3(float32 gridSize * gridSpacing, layerY, zPos),
          gridColor
        )

      idx <- idx + 2

    // Vertical lines (along Z axis)
    for x in -gridSize .. gridSize do
      let xPos = float32 x * gridSpacing

      verts.[idx] <-
        VertexPositionColor(
          Vector3(xPos, layerY, float32 -gridSize * gridSpacing),
          gridColor
        )

      verts.[idx + 1] <-
        VertexPositionColor(
          Vector3(xPos, layerY, float32 gridSize * gridSpacing),
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
    let layerY = float32 model.Camera.CurrentLayer
    drawGrid buffer layerY


    // Get cursor cell using the service and draw highlight
    let cursorCellOpt =
      EditorCursor.getCursorCell env model.Camera.Camera layerY

    // Draw ghost block preview
    Brush.view ctx model.Brush cursorCellOpt buffer

    match cursorCellOpt with
    | ValueSome cell ->
      let cursorColor = Color.Yellow
      let min = Vector3(cell.X, cell.Y, cell.Z)
      let max = Vector3(cell.X + 1f, cell.Y + 1f, cell.Z + 1f)
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

    buffer.Submit()

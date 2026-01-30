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

[<Struct>]
type EditorInputAction =
  | PanLeft
  | PanRight
  | PanForward
  | PanBackward
  | LayerUp
  | LayerDown
  | ResetCameraView
  | ToggleCameraMode
  | LeftClick
  | RightClick

[<Struct>]
type EditorModel = {
  BlockMap: BlockMapModel
  Camera: CameraModel
  Actions: ActionState<EditorInputAction>
  PrevMouseState: MouseState
}

[<Struct>]
type EditorMsg =
  | BlockMapMsg of blockMap: BlockMap.Msg
  | CameraMsg of camera: Camera.Msg
  | InputMapped of ActionState<EditorInputAction>
  | Tick of gt: GameTime

module InputHandler =
  type InputResult = {
    CameraCommands: ResizeArray<Camera.Msg>
    BlockMapModel: BlockMapModel
    Actions: ActionState<EditorInputAction>
    PrevMouseState: MouseState
  }

  let handleInput
    (env:
      #FileSystemCap & #AssetsCap & #BlockMapPersistenceCap & #EditorCursorCap)
    (actions: ActionState<EditorInputAction>)
    (model: EditorModel)
    : struct (InputResult * Cmd<EditorMsg>) =
    let panSpeed = 0.5f
    let cameraCmds = ResizeArray<Camera.Msg>()
    let mutable blockMapModel = model.BlockMap

    // Check mouse state for button clicks
    let mouseState = Mouse.GetState()

    let leftClick =
      mouseState.LeftButton = ButtonState.Pressed
      && model.PrevMouseState.LeftButton = ButtonState.Released

    let rightClick =
      mouseState.RightButton = ButtonState.Pressed
      && model.PrevMouseState.RightButton = ButtonState.Released

    // Get current cursor cell using the service (only if clicking)
    let cursorCell =
      if leftClick || rightClick then
        EditorCursor.getCursorCell
          env
          model.Camera.Camera
          (float32 model.Camera.CurrentLayer)
      else
        ValueNone

    // Handle one-shot actions (Started)
    if actions.Started.Contains LayerUp then
      cameraCmds.Add(SetLayer(model.Camera.CurrentLayer + 1))

    if actions.Started.Contains LayerDown then
      cameraCmds.Add(SetLayer(model.Camera.CurrentLayer - 1))

    if actions.Started.Contains ResetCameraView then
      cameraCmds.Add(ResetCamera)

    if actions.Started.Contains ToggleCameraMode then
      let newMode =
        match model.Camera.Mode with
        | Isometric -> FreeFly
        | FreeFly -> Isometric

      cameraCmds.Add(SetMode newMode)

    // Handle mouse clicks for block placement/removal
    if leftClick then
      match cursorCell with
      | ValueSome cell ->
        let struct (newBlockMap, _) =
          BlockMap.update
            env
            (BlockMap.Msg.PlaceBlock(cell, UMX.tag 1))
            blockMapModel

        blockMapModel <- newBlockMap
      | ValueNone -> ()

    if rightClick then
      match cursorCell with
      | ValueSome cell ->
        let struct (newBlockMap, _) =
          BlockMap.update env (BlockMap.Msg.RemoveBlock cell) blockMapModel

        blockMapModel <- newBlockMap
      | ValueNone -> ()

    // Handle continuous actions (Held) - these apply every frame while held
    let mutable panDelta = Vector2.Zero

    if actions.Held.Contains PanLeft then
      panDelta <- panDelta + Vector2(-panSpeed, 0f)

    if actions.Held.Contains PanRight then
      panDelta <- panDelta + Vector2(panSpeed, 0f)

    if actions.Held.Contains PanForward then
      panDelta <- panDelta + Vector2(0f, panSpeed)

    if actions.Held.Contains PanBackward then
      panDelta <- panDelta + Vector2(0f, -panSpeed)

    if panDelta <> Vector2.Zero then
      cameraCmds.Add(Pan panDelta)

    let result = {
      CameraCommands = cameraCmds
      BlockMapModel = blockMapModel
      Actions = actions
      PrevMouseState = mouseState
    }

    let cmd =
      cameraCmds
      |> Seq.map(fun msg -> Cmd.map CameraMsg (Cmd.ofMsg msg))
      |> Cmd.batch

    struct (result, cmd)

module Entry =

  let private inputMap =
    InputMap.empty
    |> InputMap.key PanLeft Keys.A
    |> InputMap.key PanRight Keys.D
    |> InputMap.key PanForward Keys.W
    |> InputMap.key PanBackward Keys.S
    |> InputMap.key LayerUp Keys.PageUp
    |> InputMap.key LayerDown Keys.PageDown
    |> InputMap.key ResetCameraView Keys.Home
    |> InputMap.key ToggleCameraMode Keys.Tab

  let init
    (env: #FileSystemCap & #AssetsCap & #BlockMapPersistenceCap)
    (ctx: GameContext)
    : struct (EditorModel * Cmd<EditorMsg>) =
    {
      BlockMap = BlockMap.init env BlockMapDefinition.empty
      Camera = Camera.init env
      Actions = ActionState.empty
      PrevMouseState = Mouse.GetState()
    },
    Cmd.none

  let update
    (env:
      #FileSystemCap & #AssetsCap & #BlockMapPersistenceCap & #EditorCursorCap)
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

    | InputMapped actions ->
      let struct (result, cmd) = InputHandler.handleInput env actions model

      struct ({
                model with
                    Actions = result.Actions
                    BlockMap = result.BlockMapModel
                    PrevMouseState = result.PrevMouseState
              },
              cmd)

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

      struct ({
                model with
                    Camera = cameraModel
                    PrevMouseState = mouseState
              },
              Cmd.none)

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

    BlockMap.view ctx model.BlockMap buffer

    // Get cursor cell using the service and draw highlight
    let cursorCellOpt =
      EditorCursor.getCursorCell env model.Camera.Camera layerY

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

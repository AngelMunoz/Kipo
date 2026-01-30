namespace Pomo.Lib.Editor

open System
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

[<Struct>]
type EditorModel = {
  BlockMap: BlockMapModel
  Camera: CameraModel
  Actions: ActionState<EditorInputAction>
}

[<Struct>]
type EditorMsg =
  | BlockMapMsg of blockMap: BlockMap.Msg
  | CameraMsg of camera: Camera.Msg
  | InputMapped of ActionState<EditorInputAction>
  | Tick of gt: GameTime

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
    },
    Cmd.none

  let update
    (env: #FileSystemCap & #AssetsCap & #BlockMapPersistenceCap)
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
      let panSpeed = 0.5f
      let cameraCmds = ResizeArray<Camera.Msg>()

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

      let cmd =
        cameraCmds
        |> Seq.map (fun msg -> Cmd.map CameraMsg (Cmd.ofMsg msg))
        |> Cmd.batch

      struct ({ model with Actions = actions }, cmd)

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

      if panDelta <> Vector2.Zero then
        let struct (newCamera, _) = Camera.update env (Pan panDelta) model.Camera
        struct ({ model with Camera = newCamera }, Cmd.none)
      else
        struct (model, Cmd.none)

  let subscribe (ctx: GameContext) (_model: EditorModel) : Sub<EditorMsg> =
    InputMapper.subscribeStatic inputMap InputMapped ctx

  let private drawGrid (buffer: RenderBuffer<unit, RenderCommand>) (layerY: float32) =
    let gridSize = 50
    let gridSpacing = 1.0f
    let gridColor = Color(0.3f, 0.3f, 0.3f)

    // Draw horizontal lines (along X axis)
    for z in -gridSize .. gridSize do
      let zPos = float32 z * gridSpacing
      buffer.Line(
        Vector3(float32 -gridSize * gridSpacing, layerY, zPos),
        Vector3(float32 gridSize * gridSpacing, layerY, zPos),
        gridColor
      ) |> ignore

    // Draw vertical lines (along Z axis)
    for x in -gridSize .. gridSize do
      let xPos = float32 x * gridSpacing
      buffer.Line(
        Vector3(xPos, layerY, float32 -gridSize * gridSpacing),
        Vector3(xPos, layerY, float32 gridSize * gridSpacing),
        gridColor
      ) |> ignore

  let view
    (env: #FileSystemCap & #AssetsCap & #BlockMapPersistenceCap)
    (ctx: GameContext)
    (model: EditorModel)
    (buffer: RenderBuffer<unit, RenderCommand>)
    : unit =
    buffer.Camera(model.Camera.Camera).Clear(Color.CornflowerBlue) |> ignore

    // Draw grid at current editing layer
    let layerY = float32 model.Camera.CurrentLayer
    drawGrid buffer layerY

    BlockMap.view ctx model.BlockMap buffer
    buffer.Submit()

namespace Pomo.Lib.Editor

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Mibo.Input
open Pomo.Lib
open Pomo.Lib.Services
open Pomo.Lib.Editor
open Pomo.Lib.Editor.Subsystems

/// Context for input processing - groups related state to reduce parameter count
[<Struct>]
type InputContext = {
  Env: AppEnv
  Model: EditorModel
  Actions: ActionState<EditorInputAction>
  MouseState: MouseState
  CursorCell: Vector3 voption
}

/// Accumulator for commands and state changes during input processing
type InputAccumulator = {
  CameraCommands: ResizeArray<Camera.Msg>
  BrushCommands: ResizeArray<Brush.Msg>
  mutable BlockMapModel: BlockMap.BlockMapModel
  mutable BrushModel: Brush.BrushModel
}

module InputAccumulator =
  let create(model: EditorModel) = {
    CameraCommands = ResizeArray()
    BrushCommands = ResizeArray()
    BlockMapModel = model.BlockMap
    BrushModel = model.Brush
  }

/// Mouse button state tracking
[<Struct>]
type MouseButtonState = {
  LeftPressed: bool
  LeftHeld: bool
  RightPressed: bool
}

module MouseButtonState =
  let fromStates (current: MouseState) (previous: MouseState) = {
    LeftPressed =
      current.LeftButton = ButtonState.Pressed
      && previous.LeftButton = ButtonState.Released
    LeftHeld = current.LeftButton = ButtonState.Pressed
    RightPressed =
      current.RightButton = ButtonState.Pressed
      && previous.RightButton = ButtonState.Released
  }

module OneShotActions =
  /// Handle layer navigation
  let handleLayerNav (ctx: InputContext) (acc: InputAccumulator) =
    if ctx.Actions.Started.Contains LayerUp then
      acc.CameraCommands.Add(
        Camera.Msg.SetLayer(ctx.Model.Camera.CurrentLayer + 1)
      )

    if ctx.Actions.Started.Contains LayerDown then
      acc.CameraCommands.Add(
        Camera.Msg.SetLayer(ctx.Model.Camera.CurrentLayer - 1)
      )

  /// Handle camera reset and mode toggle
  let handleCameraActions (ctx: InputContext) (acc: InputAccumulator) =
    if ctx.Actions.Started.Contains ResetCameraView then
      acc.CameraCommands.Add(Camera.Msg.ResetCamera)

    if ctx.Actions.Started.Contains ToggleCameraMode then
      let newMode =
        match ctx.Model.Camera.Mode with
        | CameraMode.Isometric -> FreeFly
        | CameraMode.FreeFly -> Isometric

      acc.CameraCommands.Add(Camera.Msg.SetMode newMode)

  /// Handle brush rotation (Q/E)
  let handleBrushRotation (ctx: InputContext) (acc: InputAccumulator) =
    if ctx.Actions.Started.Contains RotateLeft then
      acc.BrushCommands.Add(Brush.Msg.RotateY -90.0f)

    if ctx.Actions.Started.Contains RotateRight then
      acc.BrushCommands.Add(Brush.Msg.RotateY 90.0f)

  /// Handle brush mode switching and collision toggle
  let handleBrushMode (ctx: InputContext) (acc: InputAccumulator) =
    if ctx.Actions.Started.Contains SetBrushPlace then
      acc.BrushCommands.Add(Brush.Msg.SetMode BrushMode.Place)

    if ctx.Actions.Started.Contains SetBrushErase then
      acc.BrushCommands.Add(Brush.Msg.SetMode BrushMode.Erase)

    if ctx.Actions.Started.Contains ToggleCollision then
      acc.BrushCommands.Add(Brush.Msg.ToggleCollision)

module ContinuousActions =
  /// Handle camera panning (arrow keys held)
  let handlePanning
    (ctx: InputContext)
    (acc: InputAccumulator)
    (panSpeed: float32)
    =
    let mutable panDelta = Vector2.Zero

    if ctx.Actions.Held.Contains PanLeft then
      panDelta <- panDelta + Vector2(-panSpeed, 0f)

    if ctx.Actions.Held.Contains PanRight then
      panDelta <- panDelta + Vector2(panSpeed, 0f)

    if ctx.Actions.Held.Contains PanForward then
      panDelta <- panDelta + Vector2(0f, panSpeed)

    if ctx.Actions.Held.Contains PanBackward then
      panDelta <- panDelta + Vector2(0f, -panSpeed)

    if panDelta <> Vector2.Zero then
      acc.CameraCommands.Add(Camera.Msg.Pan panDelta)

module MouseActions =
  /// Place a block at the given cell
  let private placeBlock
    (env: AppEnv)
    (cell: Vector3)
    (blockId: int<BlockTypeId>)
    (acc: InputAccumulator)
    =
    let struct (newBlockMap, _) =
      BlockMap.update
        env
        (BlockMap.Msg.PlaceBlock(cell, blockId))
        acc.BlockMapModel

    acc.BlockMapModel <- newBlockMap

  /// Remove a block at the given cell
  let private removeBlock
    (env: AppEnv)
    (cell: Vector3)
    (acc: InputAccumulator)
    =
    let struct (newBlockMap, _) =
      BlockMap.update env (BlockMap.Msg.RemoveBlock cell) acc.BlockMapModel

    acc.BlockMapModel <- newBlockMap

  /// Handle Place mode mouse interactions
  let handlePlaceMode
    (ctx: InputContext)
    (acc: InputAccumulator)
    (mouseState: MouseButtonState)
    =
    let brushModel = acc.BrushModel

    // Start drag
    if mouseState.LeftPressed then
      acc.BrushCommands.Add(Brush.Msg.SetDragging(true, ctx.CursorCell))

      match ctx.CursorCell with
      | ValueSome cell ->
        placeBlock ctx.Env cell brushModel.SelectedBlockId acc
        acc.BrushCommands.Add(Brush.Msg.SetLastPlacedCell(ValueSome cell))
      | ValueNone -> ()

    // Continue dragging
    if mouseState.LeftHeld && brushModel.IsDragging then
      match ctx.CursorCell with
      | ValueSome cell ->
        match brushModel.LastPlacedCell with
        | ValueSome lastCell when lastCell <> cell ->
          placeBlock ctx.Env cell brushModel.SelectedBlockId acc
          acc.BrushCommands.Add(Brush.Msg.SetLastPlacedCell(ValueSome cell))
        | ValueNone ->
          placeBlock ctx.Env cell brushModel.SelectedBlockId acc
          acc.BrushCommands.Add(Brush.Msg.SetLastPlacedCell(ValueSome cell))
        | _ -> ()
      | ValueNone -> ()

    // End drag
    if
      not mouseState.LeftHeld
      && ctx.Model.PrevMouseState.LeftButton = ButtonState.Pressed
    then
      acc.BrushCommands.Add(Brush.Msg.SetDragging(false, ValueNone))
      acc.BrushCommands.Add(Brush.Msg.SetLastPlacedCell ValueNone)

  /// Handle Erase mode mouse interactions
  let handleEraseMode
    (ctx: InputContext)
    (acc: InputAccumulator)
    (mouseState: MouseButtonState)
    =
    let brushModel = acc.BrushModel

    // Click to erase
    if mouseState.LeftPressed then
      match ctx.CursorCell with
      | ValueSome cell -> removeBlock ctx.Env cell acc
      | ValueNone -> ()

    // Drag-to-erase
    if mouseState.LeftHeld then
      match ctx.CursorCell with
      | ValueSome cell ->
        match brushModel.LastPlacedCell with
        | ValueSome lastCell when lastCell <> cell ->
          removeBlock ctx.Env cell acc
          acc.BrushCommands.Add(Brush.Msg.SetLastPlacedCell(ValueSome cell))
        | ValueNone ->
          acc.BrushCommands.Add(Brush.Msg.SetLastPlacedCell(ValueSome cell))
        | _ -> ()
      | ValueNone -> ()

    // Reset tracking on release
    if
      not mouseState.LeftHeld
      && ctx.Model.PrevMouseState.LeftButton = ButtonState.Pressed
    then
      acc.BrushCommands.Add(Brush.Msg.SetLastPlacedCell ValueNone)

  /// Handle brush-specific mouse actions based on current mode
  let handleBrushMouse
    (ctx: InputContext)
    (acc: InputAccumulator)
    (mouseState: MouseButtonState)
    =
    match acc.BrushModel.Mode with
    | BrushMode.Place -> handlePlaceMode ctx acc mouseState
    | BrushMode.Erase -> handleEraseMode ctx acc mouseState
    | BrushMode.Select -> ()

  /// Handle right-click (always erases)
  let handleRightClick
    (ctx: InputContext)
    (acc: InputAccumulator)
    (mouseState: MouseButtonState)
    =
    if mouseState.RightPressed then
      match ctx.CursorCell with
      | ValueSome cell -> removeBlock ctx.Env cell acc
      | ValueNone -> ()

/// Input processing module containing the main entry point
[<RequireQualifiedAccess>]
module InputHandler =
  /// Process input and return accumulated state changes
  let processInput
    (env: AppEnv)
    (actions: ActionState<EditorInputAction>)
    (model: EditorModel)
    : InputResult =

    let mouseState = Mouse.GetState()

    let buttonState =
      MouseButtonState.fromStates mouseState model.PrevMouseState

    let cursorCell =
      EditorCursor.getCursorCell
        env
        model.Camera.Camera
        (float32 model.Camera.CurrentLayer)

    let ctx = {
      Env = env
      Model = model
      Actions = actions
      MouseState = mouseState
      CursorCell = cursorCell
    }

    let acc = InputAccumulator.create model

    // Process one-shot actions
    OneShotActions.handleLayerNav ctx acc
    OneShotActions.handleCameraActions ctx acc
    OneShotActions.handleBrushRotation ctx acc
    OneShotActions.handleBrushMode ctx acc

    // Process brush mouse actions
    MouseActions.handleBrushMouse ctx acc buttonState
    MouseActions.handleRightClick ctx acc buttonState

    // Process continuous actions (panning)
    ContinuousActions.handlePanning ctx acc 0.5f

    {
      CameraCommands = acc.CameraCommands
      BrushCommands = acc.BrushCommands
      BlockMapModel = acc.BlockMapModel
      BrushModel = acc.BrushModel
      Actions = actions
      PrevMouseState = mouseState
    }

namespace Pomo.Core.Editor

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.BlockMap

module EditorInput =
  open Pomo.Core.Domain.Spatial

  /// Input context bundling keyboard/mouse state and mutable timers
  type EditorInputContext = {
    mutable Keyboard: KeyboardState
    mutable PrevKeyboard: KeyboardState
    mutable Mouse: MouseState
    mutable PrevMouse: MouseState
    mutable Viewport: Viewport
    PixelsPerUnit: Vector2
    mutable DeltaTime: float32
    mutable RotationTimer: float32
    mutable UndoRedoTimer: float32
    mutable LastPaintedCell: GridCell3D
  }

  let private isKeyJustPressed
    (prev: KeyboardState)
    (curr: KeyboardState)
    (key: Keys)
    =
    curr.IsKeyDown key && prev.IsKeyUp key

  let private cameraSpeed = 5.0f
  let private rotationSpeed = 0.01f

  /// Handle camera movement based on mode
  let handleCameraInput
    (state: EditorState) // Added state
    (cam: EditorCameraState)
    (keyboard: KeyboardState)
    (mouse: MouseState)
    (prevMouse: MouseState)
    (deltaTime: float32)
    =
    let moveAmount = cameraSpeed * deltaTime * 60f

    match cam.Mode with
    | Isometric ->
      // Arrow keys pan XZ
      if keyboard.IsKeyDown Keys.Left then
        EditorCamera.panXZ cam -moveAmount 0f

      if keyboard.IsKeyDown Keys.Right then
        EditorCamera.panXZ cam moveAmount 0f

      if keyboard.IsKeyDown Keys.Up then
        EditorCamera.panXZ cam 0f -moveAmount

      if keyboard.IsKeyDown Keys.Down then
        EditorCamera.panXZ cam 0f moveAmount

      // Scroll zooms
      let scrollDelta =
        float32(mouse.ScrollWheelValue - prevMouse.ScrollWheelValue) / 120f

      if scrollDelta <> 0f then
        EditorCamera.zoom cam (scrollDelta * 0.1f)

    | FreeFly ->
      // Arrow keys move XY
      if keyboard.IsKeyDown Keys.Left then
        EditorCamera.moveFreeFly cam -moveAmount 0f 0f

      if keyboard.IsKeyDown Keys.Right then
        EditorCamera.moveFreeFly cam moveAmount 0f 0f

      if keyboard.IsKeyDown Keys.Up then
        EditorCamera.moveFreeFly cam 0f moveAmount 0f

      if keyboard.IsKeyDown Keys.Down then
        EditorCamera.moveFreeFly cam 0f -moveAmount 0f

      // Scroll moves Z
      let scrollDelta =
        float32(mouse.ScrollWheelValue - prevMouse.ScrollWheelValue) / 120f

      if scrollDelta <> 0f then
        EditorCamera.moveFreeFly cam 0f 0f (scrollDelta * moveAmount)

      // Right-click drag rotates
      if mouse.RightButton = ButtonState.Pressed then
        let deltaX = float32(mouse.X - prevMouse.X) * rotationSpeed
        let deltaY = float32(mouse.Y - prevMouse.Y) * rotationSpeed
        EditorCamera.rotate cam -deltaX -deltaY

    // Middle-click resets to isometric
    if
      mouse.MiddleButton = ButtonState.Pressed
      && prevMouse.MiddleButton = ButtonState.Released
    then
      transact(fun () ->
        cam.ResetToIsometric()
        state.CameraMode.Value <- Isometric)

  /// Handle editor actions (block placement, layer changes, etc.)
  let handleEditorInput
    (state: EditorState)
    (cam: EditorCameraState)
    (uiService: Pomo.Core.Environment.IUIService)
    (ctx: EditorInputContext)
    =
    // Destructure context for easier access
    let keyboard = ctx.Keyboard
    let prevKeyboard = ctx.PrevKeyboard
    let mouse = ctx.Mouse
    let prevMouse = ctx.PrevMouse
    let viewport = ctx.Viewport
    let pixelsPerUnit = ctx.PixelsPerUnit
    let deltaTime = ctx.DeltaTime

    let isMouseOverUI = uiService.IsMouseOverUI |> AVal.force

    // Toggle Help
    if isKeyJustPressed prevKeyboard keyboard Keys.F1 then
      transact(fun () -> state.ShowHelp.Value <- not state.ShowHelp.Value)

    // Toggle camera mode with Tab (Works even if over UI)
    if isKeyJustPressed prevKeyboard keyboard Keys.Tab then
      transact(fun () ->
        let newMode = if cam.Mode = Isometric then FreeFly else Isometric
        cam.Mode <- newMode
        state.CameraMode.Value <- newMode)

    // Undo/Redo (Continuous)
    ctx.UndoRedoTimer <- ctx.UndoRedoTimer - deltaTime
    let undoDelay = 0.1f

    if ctx.UndoRedoTimer <= 0.0f then
      let ctrl =
        keyboard.IsKeyDown(Keys.LeftControl)
        || keyboard.IsKeyDown(Keys.RightControl)

      if ctrl then
        if keyboard.IsKeyDown Keys.Z then
          ctx.UndoRedoTimer <- undoDelay
          EditorState.undo state
        elif keyboard.IsKeyDown Keys.Y then
          ctx.UndoRedoTimer <- undoDelay
          EditorState.redo state

    // Reset Rotation
    if isKeyJustPressed prevKeyboard keyboard Keys.R then
      EditorState.applyAction
        state
        (SetRotation(Quaternion.Identity, state.CurrentRotation.Value))

    // Layer navigation with Page Up/Down
    if isKeyJustPressed prevKeyboard keyboard Keys.PageUp then
      EditorState.applyAction state (ChangeLayer 1)

    if isKeyJustPressed prevKeyboard keyboard Keys.PageDown then
      EditorState.applyAction state (ChangeLayer -1)

    // Brush mode
    if isKeyJustPressed prevKeyboard keyboard Keys.D1 then
      let current = state.BrushMode |> AVal.force
      EditorState.applyAction state (SetBrushMode(Place, current))

    if isKeyJustPressed prevKeyboard keyboard Keys.D2 then
      let current = state.BrushMode |> AVal.force
      EditorState.applyAction state (SetBrushMode(Erase, current))

    // Rotate block with Q/E (Continuous)
    ctx.RotationTimer <- ctx.RotationTimer - deltaTime

    let isShift =
      keyboard.IsKeyDown Keys.LeftShift || keyboard.IsKeyDown Keys.RightShift

    let isAlt =
      keyboard.IsKeyDown Keys.LeftAlt || keyboard.IsKeyDown Keys.RightAlt

    let getRotationAxis() =
      if isShift then Vector3.UnitX // Pitch
      elif isAlt then Vector3.UnitZ // Roll
      else Vector3.UnitY // Yaw

    if ctx.RotationTimer <= 0.0f then
      let rotationDelay = 0.05f
      let step = MathHelper.ToRadians(10.0f)

      if keyboard.IsKeyDown Keys.Q then
        ctx.RotationTimer <- rotationDelay
        let current = state.CurrentRotation |> AVal.force
        let axis = getRotationAxis()
        let delta = Quaternion.CreateFromAxisAngle(axis, step)
        let newRot = current * delta
        EditorState.applyAction state (SetRotation(newRot, current))
      elif keyboard.IsKeyDown Keys.E then
        ctx.RotationTimer <- rotationDelay
        let current = state.CurrentRotation |> AVal.force
        let axis = getRotationAxis()
        let delta = Quaternion.CreateFromAxisAngle(axis, -step)
        let newRot = current * delta
        EditorState.applyAction state (SetRotation(newRot, current))

    // Update cursor position
    let map = state.BlockMap |> AVal.force

    // Inverse of render offset to map world coord back to logical map coord
    let centerOffset =
      Vector3(
        -float32 map.Width * CellSize * 0.5f,
        0f,
        -float32 map.Depth * CellSize * 0.5f
      )

    let screenPos = Vector2(float32 mouse.X, float32 mouse.Y)

    let worldPos =
      EditorCamera.screenToWorld
        cam
        screenPos
        viewport
        pixelsPerUnit
        state.CurrentLayer.Value

    // Adjust world position to logical position
    // RenderPos = LogicalPos + Offset
    // LogicalPos = RenderPos - Offset
    // Manually subtract since WorldPosition is struct without operator - with Vector3
    let logicalPos: WorldPosition = {
      X = worldPos.X - centerOffset.X
      Y = worldPos.Y - centerOffset.Y
      Z = worldPos.Z - centerOffset.Z
    }

    let cell = BlockMap.worldPositionToCell logicalPos

    // Validate cell is within bounds
    let isValid =
      cell.X >= 0
      && cell.X < map.Width
      && cell.Z >= 0
      && cell.Z < map.Depth
      && cell.Y >= 0
      && cell.Y < map.Height

    transact(fun () ->
      if isValid then
        state.GridCursor.Value <- ValueSome cell
      else
        state.GridCursor.Value <- ValueNone)

    // Left-click: Place or Erase based on brush mode (single click or drag)
    let shouldPaint =
      not isMouseOverUI
      && mouse.LeftButton = ButtonState.Pressed
      && isValid
      && (prevMouse.LeftButton = ButtonState.Released
          || cell <> ctx.LastPaintedCell)

    if shouldPaint then
      ctx.LastPaintedCell <- cell

      match state.SelectedBlockType |> AVal.force with
      | ValueSome blockTypeId ->
        match state.BrushMode |> AVal.force with
        | Place ->
          let rotation =
            if state.CurrentRotation.Value = Quaternion.Identity then
              ValueNone
            else
              ValueSome state.CurrentRotation.Value

          let block: PlacedBlock = {
            Cell = cell
            BlockTypeId = blockTypeId
            Rotation = rotation
          }

          EditorState.applyAction state (PlaceBlock(block, ValueNone))
        | Erase -> EditorState.applyAction state (RemoveBlock(cell, ValueNone))
        | Select -> ()
      | ValueNone ->
        if state.BrushMode |> AVal.force = Erase then
          EditorState.applyAction state (RemoveBlock(cell, ValueNone))

    // Reset last painted cell when button released
    if mouse.LeftButton = ButtonState.Released then
      ctx.LastPaintedCell <- { X = -1; Y = -1; Z = -1 }

    // Right-click: Always erase (quick erase)
    if
      not isMouseOverUI
      && mouse.RightButton = ButtonState.Pressed
      && prevMouse.RightButton = ButtonState.Released
    then
      if cam.Mode = Isometric then // Only in isometric, free-fly uses right-click for rotation
        EditorState.applyAction state (RemoveBlock(cell, ValueNone))

  let createSystem
    (game: Game)
    (state: EditorState)
    (cam: EditorCameraState)
    (uiService: Pomo.Core.Environment.IUIService)
    (pixelsPerUnit: Vector2)
    : GameComponent =

    // Input context with mutable state
    let inputContext = {
      Keyboard = Keyboard.GetState()
      PrevKeyboard = Keyboard.GetState()
      Mouse = Mouse.GetState()
      PrevMouse = Mouse.GetState()
      Viewport = game.GraphicsDevice.Viewport
      PixelsPerUnit = pixelsPerUnit
      DeltaTime = 0.0f
      RotationTimer = 0.0f
      UndoRedoTimer = 0.0f
      LastPaintedCell = { X = -1; Y = -1; Z = -1 }
    }

    { new GameComponent(game) with
        override _.Update gameTime =
          // Update input context in-place
          inputContext.PrevKeyboard <- inputContext.Keyboard
          inputContext.PrevMouse <- inputContext.Mouse
          inputContext.Keyboard <- Keyboard.GetState()
          inputContext.Mouse <- Mouse.GetState()
          inputContext.Viewport <- game.GraphicsDevice.Viewport

          inputContext.DeltaTime <-
            float32 gameTime.ElapsedGameTime.TotalSeconds

          handleCameraInput
            state
            cam
            inputContext.Keyboard
            inputContext.Mouse
            inputContext.PrevMouse
            inputContext.DeltaTime

          handleEditorInput state cam uiService inputContext
    }

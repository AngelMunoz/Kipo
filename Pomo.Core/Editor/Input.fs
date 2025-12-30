namespace Pomo.Core.Editor

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.BlockMap

module EditorInput =

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
      cam.ResetToIsometric()

  /// Handle editor actions (block placement, layer changes, etc.)
  let handleEditorInput
    (state: EditorState)
    (cam: EditorCameraState)
    (keyboard: KeyboardState)
    (prevKeyboard: KeyboardState)
    (mouse: MouseState)
    (prevMouse: MouseState)
    (viewport: Viewport)
    (pixelsPerUnit: Vector2)
    =
    // Toggle camera mode with Tab
    if isKeyJustPressed prevKeyboard keyboard Keys.Tab then
      transact(fun () ->
        let newMode = if cam.Mode = Isometric then FreeFly else Isometric
        cam.Mode <- newMode
        state.CameraMode.Value <- newMode)

    // Layer navigation with Page Up/Down
    if isKeyJustPressed prevKeyboard keyboard Keys.PageUp then
      EditorState.applyAction state (ChangeLayer 1)

    if isKeyJustPressed prevKeyboard keyboard Keys.PageDown then
      EditorState.applyAction state (ChangeLayer -1)

    // Brush mode
    if isKeyJustPressed prevKeyboard keyboard Keys.D1 then
      EditorState.applyAction state (SetBrushMode Place)

    if isKeyJustPressed prevKeyboard keyboard Keys.D2 then
      EditorState.applyAction state (SetBrushMode Erase)

    // Rotate block with Q/E
    if isKeyJustPressed prevKeyboard keyboard Keys.Q then
      let current = state.CurrentRotation |> AVal.force

      let newRot =
        current * Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver2, 0f, 0f)

      EditorState.applyAction state (SetRotation newRot)

    if isKeyJustPressed prevKeyboard keyboard Keys.E then
      let current = state.CurrentRotation |> AVal.force

      let newRot =
        current * Quaternion.CreateFromYawPitchRoll(-MathHelper.PiOver2, 0f, 0f)

      EditorState.applyAction state (SetRotation newRot)

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
    let logicalPos = {
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

    // Left-click: Place or Erase based on brush mode
    if
      mouse.LeftButton = ButtonState.Pressed
      && prevMouse.LeftButton = ButtonState.Released
    then
      match state.SelectedBlockType |> AVal.force with
      | ValueSome blockTypeId ->
        match state.BrushMode |> AVal.force with
        | Place -> EditorState.applyAction state (PlaceBlock(cell, blockTypeId))
        | Erase -> EditorState.applyAction state (RemoveBlock cell)
        | Select -> ()
      | ValueNone ->
        if state.BrushMode |> AVal.force = Erase then
          EditorState.applyAction state (RemoveBlock cell)

    // Right-click: Always erase (quick erase)
    if
      mouse.RightButton = ButtonState.Pressed
      && prevMouse.RightButton = ButtonState.Released
    then
      if cam.Mode = Isometric then // Only in isometric, free-fly uses right-click for rotation
        EditorState.applyAction state (RemoveBlock cell)

  let createSystem
    (game: Game)
    (state: EditorState)
    (cam: EditorCameraState)
    (pixelsPerUnit: Vector2)
    : GameComponent =
    let mutable prevKeyboard = Keyboard.GetState()
    let mutable prevMouse = Mouse.GetState()

    { new GameComponent(game) with
        override _.Update gameTime =
          let keyboard = Keyboard.GetState()
          let mouse = Mouse.GetState()
          let deltaTime = float32 gameTime.ElapsedGameTime.TotalSeconds
          let viewport = game.GraphicsDevice.Viewport

          handleCameraInput cam keyboard mouse prevMouse deltaTime

          handleEditorInput
            state
            cam
            keyboard
            prevKeyboard
            mouse
            prevMouse
            viewport
            pixelsPerUnit

          prevKeyboard <- keyboard
          prevMouse <- mouse
    }

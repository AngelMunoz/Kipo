namespace Pomo.Core.App

open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Core.Constants
open Pomo.Core.Editor
open Pomo.Core.Environment

/// Editor input handling integrated with Mibo.
/// Uses direct MonoGame input state for continuous (held key) detection.
module AppEditorInput =

  /// Input context with mutable timers (per-session state)
  type InputContext = {
    mutable PrevMouse: MouseState
    mutable PrevKeyboard: KeyboardState
    mutable RotationTimer: float32
    mutable UndoRedoTimer: float32
    mutable LastPaintedCell: Pomo.Core.Domain.Spatial.GridCell3D
  }

  let create() = {
    PrevMouse = Mouse.GetState()
    PrevKeyboard = Keyboard.GetState()
    RotationTimer = 0.0f
    UndoRedoTimer = 0.0f
    LastPaintedCell = { X = -1; Y = -1; Z = -1 }
  }

  /// Process editor input for the current frame.
  /// Uses MonoGame's direct state for continuous input (held keys).
  let processInput
    (gd: GraphicsDevice)
    (editorState: EditorState)
    (camera: MutableCamera)
    (uiService: IUIService)
    (onPlaytest: unit -> unit)
    (deltaTime: float32)
    (ctx: InputContext)
    =
    // Get current state directly from MonoGame
    let keyboard = Keyboard.GetState()
    let mouse = Mouse.GetState()
    let prevKeyboard = ctx.PrevKeyboard
    let prevMouse = ctx.PrevMouse

    let pixelsPerUnit = Constants.BlockMap3DPixelsPerUnit
    let viewport = gd.Viewport

    // Create EditorInput context for existing handlers
    let editorCtx: EditorInput.EditorInputContext = {
      Keyboard = keyboard
      PrevKeyboard = prevKeyboard
      Mouse = mouse
      PrevMouse = prevMouse
      Viewport = viewport
      PixelsPerUnit = pixelsPerUnit
      DeltaTime = deltaTime
      RotationTimer = ctx.RotationTimer
      UndoRedoTimer = ctx.UndoRedoTimer
      LastPaintedCell = ctx.LastPaintedCell
    }

    // Handle camera input
    EditorInput.handleCameraInput
      editorState
      camera
      keyboard
      mouse
      prevMouse
      deltaTime

    // Handle editor input using provided UI service and onPlaytest callback
    EditorInput.handleEditorInput
      editorState
      camera
      uiService
      onPlaytest
      editorCtx

    // Copy timers and state back
    ctx.RotationTimer <- editorCtx.RotationTimer
    ctx.UndoRedoTimer <- editorCtx.UndoRedoTimer
    ctx.LastPaintedCell <- editorCtx.LastPaintedCell
    ctx.PrevMouse <- mouse
    ctx.PrevKeyboard <- keyboard

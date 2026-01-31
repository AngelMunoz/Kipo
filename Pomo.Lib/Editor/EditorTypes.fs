namespace Pomo.Lib.Editor

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Mibo.Elmish
open Mibo.Input
open Pomo.Lib.Editor.Subsystems

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
  | RotateLeft
  | RotateRight
  | ToggleCollision
  | SetBrushPlace
  | SetBrushErase

[<Struct>]
type EditorModel = {
  BlockMap: BlockMap.BlockMapModel
  Camera: Camera.CameraModel
  Brush: Brush.BrushModel
  Actions: ActionState<EditorInputAction>
  PrevMouseState: MouseState
}

[<Struct>]
type EditorMsg =
  | BlockMapMsg of blockMap: BlockMap.Msg
  | CameraMsg of camera: Camera.Msg
  | BrushMsg of brush: Brush.Msg
  | InputMapped of ActionState<EditorInputAction>
  | Tick of gt: GameTime

type InputResult = {
  CameraCommands: ResizeArray<Camera.Msg>
  BrushCommands: ResizeArray<Brush.Msg>
  BlockMapModel: BlockMap.BlockMapModel
  BrushModel: Brush.BrushModel
  Actions: ActionState<EditorInputAction>
  PrevMouseState: MouseState
}

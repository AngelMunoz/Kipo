namespace Pomo.Lib.Editor

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Myra.Graphics2D.UI
open Mibo.Elmish
open Mibo.Input
open Pomo.Lib.Editor
open Pomo.Lib.Editor.Subsystems
open Pomo.Lib.Services

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
  | ShowHelp
  | NextBlock
  | PrevBlock

[<Struct>]
type EditorModel = {
  BlockMap: BlockMap.BlockMapModel
  Camera: Camera.CameraModel
  Brush: Brush.BrushModel
  Actions: ActionState<EditorInputAction>
  PrevMouseState: MouseState
  Desktop: Desktop voption
}

[<Struct>]
type UIMsg =
  | InitializeUI
  | ShowHelp
  | HideHelp

[<Struct>]
type EditorMsg =
  | BlockMapMsg of blockMap: BlockMap.Msg
  | CameraMsg of camera: Camera.Msg
  | BrushMsg of brush: Brush.Msg
  | InputMapped of actions: ActionState<EditorInputAction>
  | Tick of gt: GameTime
  | UIMsg of uimsg: UIMsg
  | Load of path: string
  | Save of path: string
  | SetBlockMapModel of BlockMap.BlockMapModel
  | LoadFailed of error: FsError
  | SaveComplete


type InputResult = {
  CameraCommands: ResizeArray<Camera.Msg>
  BrushCommands: ResizeArray<Brush.Msg>
  BlockMapModel: BlockMap.BlockMapModel
  BrushModel: Brush.BrushModel
  Actions: ActionState<EditorInputAction>
  PrevMouseState: MouseState
}

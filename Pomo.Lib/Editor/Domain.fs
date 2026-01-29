namespace Pomo.Lib.Editor

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open FSharp.UMX
open Mibo.Input
open Pomo.Lib

// --- Editor Interaction Types ---

[<Struct>]
type BrushMode =
  | Place
  | Erase
  | Select

[<Struct>]
type CameraMode =
  | Isometric
  | FreeFly

[<Struct>]
type EditorAction =
  | PlaceBlock of cell: Vector3 * blockTypeId: int<BlockTypeId>
  | RemoveBlock of cell: Vector3
  | ChangeLayer of layer: int
  | SetBrushMode of brushMode: BrushMode
  | SetCameraMode of cameraMode: CameraMode

[<Struct>]
type InputState = {
  MousePosition: Point
  IsLeftDown: bool
  IsRightDown: bool
  KeysDown: Set<Keys>
}

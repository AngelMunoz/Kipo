namespace Pomo.Core.Editor

open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Spatial

/// Brush modes for block placement
[<Struct>]
type BrushMode =
  | Place
  | Erase
  | Select

/// Camera modes for the editor
[<Struct>]
type CameraMode =
  | Isometric
  | FreeFly

/// Editor actions for undo/redo
[<Struct>]
type EditorAction =
  | PlaceBlock of cell: GridCell3D * blockTypeId: int<BlockTypeId>
  | RemoveBlock of cell: GridCell3D
  | SetRotation of rotation: Quaternion
  | ChangeLayer of delta: int
  | SetBrushMode of mode: BrushMode

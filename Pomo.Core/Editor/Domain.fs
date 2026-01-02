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

open Pomo.Core.Domain.BlockMap

/// Editor actions for undo/redo
[<Struct>]
type EditorAction =
  | PlaceBlock of placedBlock: PlacedBlock * replacedBlock: PlacedBlock voption
  | RemoveBlock of cell: GridCell3D * removedBlock: PlacedBlock voption
  | SetRotation of rotation: Quaternion * prevRotation: Quaternion
  | ChangeLayer of delta: int
  | SetBrushMode of mode: BrushMode * prevMode: BrushMode

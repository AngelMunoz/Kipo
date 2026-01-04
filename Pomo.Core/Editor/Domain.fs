namespace Pomo.Core.Editor

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Skill

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


module EditorEffectPresets =

  let lava: Effect = {
    Name = "Lava"
    Kind = DamageOverTime
    DamageSource = Magical
    Stacking = RefreshDuration
    Duration = Loop(TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(5.0))
    Visuals = VisualManifest.empty
    Modifiers = [|
      ResourceChange(Entity.ResourceType.HP, Formula.Const -10.0)
    |]
  }

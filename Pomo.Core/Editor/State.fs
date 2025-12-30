namespace Pomo.Core.Editor

open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.BlockMap

type EditorState = {
  BlockMap: cval<BlockMapDefinition>
  CurrentLayer: cval<int>
  GridCursor: cval<GridCell3D voption>
  SelectedBlockType: cval<int<BlockTypeId> voption>
  BrushMode: cval<BrushMode>
  CurrentRotation: cval<Quaternion>
  CameraMode: cval<CameraMode>
  UndoStack: clist<EditorAction>
  RedoStack: clist<EditorAction>
}

module EditorState =

  let create(initialMap: BlockMapDefinition) : EditorState = {
    BlockMap = cval initialMap
    CurrentLayer = cval 0
    GridCursor = cval ValueNone
    SelectedBlockType = cval ValueNone
    BrushMode = cval Place
    CurrentRotation = cval Quaternion.Identity
    CameraMode = cval Isometric
    UndoStack = clist []
    RedoStack = clist []
  }

  let private pushUndo (state: EditorState) (action: EditorAction) =
    transact(fun () ->
      state.UndoStack.Add action |> ignore
      state.RedoStack.Clear())

  let applyAction (state: EditorState) (action: EditorAction) : unit =
    transact(fun () ->
      match action with
      | PlaceBlock(cell, blockTypeId) ->
        let map = state.BlockMap.Value

        let block: PlacedBlock = {
          Cell = cell
          BlockTypeId = blockTypeId
          Rotation =
            if state.CurrentRotation.Value = Quaternion.Identity then
              ValueNone
            else
              ValueSome state.CurrentRotation.Value
        }

        map.Blocks[cell] <- block

        state.BlockMap.Value <- map
      | RemoveBlock cell ->
        let map = state.BlockMap.Value
        map.Blocks.Remove cell |> ignore
        state.BlockMap.Value <- map

      | SetRotation rotation -> state.CurrentRotation.Value <- rotation

      | ChangeLayer delta ->
        let newLayer = state.CurrentLayer.Value + delta |> max 0
        state.CurrentLayer.Value <- newLayer

      | SetBrushMode mode -> state.BrushMode.Value <- mode)

    pushUndo state action

  let undo(state: EditorState) : unit =
    transact(fun () ->
      match state.UndoStack |> AList.tryLast |> AVal.force with
      | Some action ->
        // Remove from undo, add to redo
        state.UndoStack.RemoveAt(state.UndoStack.Count - 1) |> ignore
        state.RedoStack.Add action |> ignore
      // TODO: Apply inverse action
      | None -> ())

  let redo(state: EditorState) : unit =
    transact(fun () ->
      match state.RedoStack |> AList.tryLast |> AVal.force with
      | Some action ->
        state.RedoStack.RemoveAt(state.RedoStack.Count - 1) |> ignore
        applyAction state action
      | None -> ())

namespace Pomo.Core.Editor

open Pomo.Core
open System.Collections.Generic
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
    let finalAction =
      transact(fun () ->
        match action with
        | PlaceBlock(cell, blockTypeId, _) ->
          let map = state.BlockMap.Value
          let prev = map.Blocks |> Dictionary.tryFindV cell

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
          state.BlockMap.Value <- { map with Version = map.Version + 1 }
          PlaceBlock(cell, blockTypeId, prev)

        | RemoveBlock(cell, _) ->
          let map = state.BlockMap.Value
          let prev = map.Blocks |> Dictionary.tryFindV cell
          map.Blocks.Remove cell |> ignore
          state.BlockMap.Value <- { map with Version = map.Version + 1 }
          RemoveBlock(cell, prev)

        | SetRotation(rotation, _) ->
          let prev = state.CurrentRotation.Value
          state.CurrentRotation.Value <- rotation
          SetRotation(rotation, prev)

        | ChangeLayer delta ->
          let newLayer = state.CurrentLayer.Value + delta |> max 0
          state.CurrentLayer.Value <- newLayer
          ChangeLayer delta

        | SetBrushMode(mode, _) ->
          let prev = state.BrushMode.Value
          state.BrushMode.Value <- mode
          SetBrushMode(mode, prev))

    pushUndo state finalAction

  let undo(state: EditorState) : unit =
    transact(fun () ->
      match state.UndoStack |> AList.tryLast |> AVal.force with
      | Some action ->
        state.UndoStack.RemoveAt(state.UndoStack.Count - 1) |> ignore
        state.RedoStack.Add action |> ignore

        // Apply inverse
        match action with
        | PlaceBlock(cell, _, prev) ->
          let map = state.BlockMap.Value

          match prev with
          | ValueSome pb -> map.Blocks[cell] <- pb
          | ValueNone -> map.Blocks.Remove cell |> ignore

          state.BlockMap.Value <- { map with Version = map.Version + 1 }

        | RemoveBlock(cell, prev) ->
          let map = state.BlockMap.Value

          match prev with
          | ValueSome pb -> map.Blocks[cell] <- pb
          | ValueNone -> () // Wasn't anything there anyway

          state.BlockMap.Value <- { map with Version = map.Version + 1 }

        | SetRotation(_, prev) -> state.CurrentRotation.Value <- prev
        | ChangeLayer delta ->
          state.CurrentLayer.Value <- state.CurrentLayer.Value - delta |> max 0
        | SetBrushMode(_, prev) -> state.BrushMode.Value <- prev

      | None -> ())

  let redo(state: EditorState) : unit =
    transact(fun () ->
      match state.RedoStack |> AList.tryLast |> AVal.force with
      | Some action ->
        state.RedoStack.RemoveAt(state.RedoStack.Count - 1) |> ignore

        // Re-apply. We use a simple apply logic here instead of calling applyAction
        // to avoid double-stacking undos.
        match action with
        | PlaceBlock(cell, blockTypeId, _) ->
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
          state.BlockMap.Value <- { map with Version = map.Version + 1 }
          state.UndoStack.Add action |> ignore

        | RemoveBlock(cell, _) ->
          let map = state.BlockMap.Value
          map.Blocks.Remove cell |> ignore
          state.BlockMap.Value <- { map with Version = map.Version + 1 }
          state.UndoStack.Add action |> ignore

        | SetRotation(rot, _) ->
          state.CurrentRotation.Value <- rot
          state.UndoStack.Add action |> ignore

        | ChangeLayer delta ->
          state.CurrentLayer.Value <- state.CurrentLayer.Value + delta |> max 0
          state.UndoStack.Add action |> ignore

        | SetBrushMode(mode, _) ->
          state.BrushMode.Value <- mode
          state.UndoStack.Add action |> ignore

      | None -> ())

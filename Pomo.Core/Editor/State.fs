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
  ShowHelp: cval<bool>
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
    ShowHelp = cval false
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
        | PlaceBlock(block, _) ->
          let map = state.BlockMap.Value
          let prev = map.Blocks |> Dictionary.tryFindV block.Cell

          map.Blocks[block.Cell] <- block
          state.BlockMap.Value <- { map with Version = map.Version + 1 }
          PlaceBlock(block, prev)

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
        | PlaceBlock(block, prev) ->
          let map = state.BlockMap.Value

          match prev with
          | ValueSome pb -> map.Blocks[block.Cell] <- pb
          | ValueNone -> map.Blocks.Remove block.Cell |> ignore

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
        state.UndoStack.Add action |> ignore

        match action with
        | PlaceBlock(block, _) ->
          let map = state.BlockMap.Value
          map.Blocks[block.Cell] <- block
          state.BlockMap.Value <- { map with Version = map.Version + 1 }

        | RemoveBlock(cell, _) ->
          let map = state.BlockMap.Value
          map.Blocks.Remove cell |> ignore
          state.BlockMap.Value <- { map with Version = map.Version + 1 }

        | SetRotation(rot, _) -> state.CurrentRotation.Value <- rot

        | ChangeLayer delta ->
          state.CurrentLayer.Value <- state.CurrentLayer.Value + delta |> max 0

        | SetBrushMode(mode, _) -> state.BrushMode.Value <- mode

      | None -> ())

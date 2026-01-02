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
  UndoStack: Stack<EditorAction>
  RedoStack: Stack<EditorAction>
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
    UndoStack = Stack()
    RedoStack = Stack()
  }

  let private pushUndo (state: EditorState) (action: EditorAction) =
    state.UndoStack.Push(action)
    state.RedoStack.Clear()

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
    match state.UndoStack.TryPop() with
    | true, action ->
      state.RedoStack.Push(action)

      transact(fun () ->
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
          | ValueNone -> ()

          state.BlockMap.Value <- { map with Version = map.Version + 1 }

        | SetRotation(_, prev) -> state.CurrentRotation.Value <- prev
        | ChangeLayer delta ->
          state.CurrentLayer.Value <- state.CurrentLayer.Value - delta |> max 0
        | SetBrushMode(_, prev) -> state.BrushMode.Value <- prev)
    | false, _ -> ()

  let redo(state: EditorState) : unit =
    match state.RedoStack.TryPop() with
    | true, action ->
      state.UndoStack.Push(action)

      transact(fun () ->
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

        | SetBrushMode(mode, _) -> state.BrushMode.Value <- mode)
    | false, _ -> ()

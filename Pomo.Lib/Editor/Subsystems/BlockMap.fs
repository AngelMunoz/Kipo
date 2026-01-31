namespace Pomo.Lib.Editor.Subsystems

open Microsoft.Xna.Framework
open Mibo.Elmish
open Pomo.Lib
open Pomo.Lib.Services

module BlockMap =
  // Elmish-specific types - local to this subsystem
  [<Struct>]
  type BlockMapModel = {
    Definition: BlockMapDefinition
    Cursor: Vector3 voption
    Dirty: bool
  }

  type Msg =
    | PlaceBlock of cell: Vector3 * blockId: int<BlockTypeId>
    | RemoveBlock of cell: Vector3
    | SetCursor of Vector3 voption
    | SetMap of BlockMapDefinition

  let init _ (mapDef: BlockMapDefinition) : BlockMapModel = {
    Definition = mapDef
    Cursor = ValueNone
    Dirty = false
  }

  let update
    _
    (msg: Msg)
    (model: BlockMapModel)
    : struct (BlockMapModel * Cmd<Msg>) =
    match msg with
    | PlaceBlock(cell, blockId) ->
      model.Definition.Blocks.[cell] <- {
        Cell = cell
        BlockTypeId = blockId
        Rotation = ValueNone
      }

      { model with Dirty = true }, Cmd.none
    | RemoveBlock cell ->
      model.Definition.Blocks.Remove cell |> ignore
      { model with Dirty = true }, Cmd.none
    | SetCursor cursor -> { model with Cursor = cursor }, Cmd.none
    | SetMap map ->
      {
        model with
            Definition = map
            Dirty = false
      },
      Cmd.none

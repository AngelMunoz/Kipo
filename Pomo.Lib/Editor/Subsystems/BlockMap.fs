namespace Pomo.Lib.Editor.Subsystems

open System
open System.Collections.Concurrent
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Rendering
open Mibo.Rendering.Graphics3D
open Mibo.Elmish.Assets
open Pomo.Lib
open Pomo.Lib.Services
open FSharp.UMX

module BlockMap =
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

  let private loadModelCache
    (assets: IAssets)
    (ctx: GameContext)
    : string -> Model voption =
    let cache = ConcurrentDictionary<string, Model voption>()

    fun path ->
      let mutable cached = Unchecked.defaultof<_>

      if cache.TryGetValue(path, &cached) then
        cached
      else
        let model = Assets.model path ctx
        cache.[path] <- ValueSome model
        ValueSome model

  let view
    (env: #AssetsCap & #ModelScalerCap)
    (ctx: GameContext)
    (model: BlockMapModel)
    (buffer: RenderBuffer<unit, RenderCommand>)
    : unit =

    for kvp in model.Definition.Blocks do
      let placedBlock = kvp.Value
      let cell = placedBlock.Cell

      match model.Definition.Palette.TryGetValue placedBlock.BlockTypeId with
      | true, blockType ->
        let model = env.Assets.Model blockType.Model
        let modelMesh = Mesh.fromModel model

        for modelMesh in modelMesh do

          let rot =
            placedBlock.Rotation |> ValueOption.defaultValue Quaternion.Identity

          // Get auto-computed scale and center offset from service
          let autoScale = ModelScaler.getScale env blockType.Model
          let scale = blockType.Scale |> ValueOption.defaultValue autoScale
          let centerOffset = ModelScaler.getCenterOffset env blockType.Model

          // Position at cell center minus the model's center offset
          // This centers the model in the grid cell so edges touch adjacent cells
          let cellSize = GridDimensions.CellSize
          let halfCell = cellSize * 0.5f
          let cellCenter = cell + Vector3(halfCell, halfCell, halfCell)
          let position = cellCenter - (centerOffset * scale)

          let worldMatrix =
            Matrix.CreateScale(scale, scale, scale)
            * Matrix.CreateFromQuaternion(rot)
            * Matrix.CreateTranslation(position)

          buffer.Draw(
            draw {
              mesh modelMesh
              withTransform worldMatrix
              withAlbedo Color.White
            }
          )
      | false, _ -> ()

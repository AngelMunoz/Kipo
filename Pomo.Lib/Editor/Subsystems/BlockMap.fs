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
    (env: #AssetsCap)
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

          let scale = blockType.Scale |> ValueOption.defaultValue 0.01f

          let worldMatrix =
            Matrix.CreateScale(scale, scale, scale)
            * Matrix.CreateFromQuaternion(rot)
            * Matrix.CreateTranslation(cell)

          buffer
            .Draw(
              draw {
                mesh modelMesh
                withTransform worldMatrix
                withAlbedo Color.White
              }
            )
            .Submit()
      | false, _ -> ()

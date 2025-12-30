namespace Pomo.Core.Rendering

open System.Collections.Concurrent
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Graphics

module BlockEmitter =
  open Pomo.Core.Domain.Core

  let createLazyModelLoader
    (content: ContentManager)
    : string -> LoadedModel voption =
    let cache = ConcurrentDictionary<string, Lazy<LoadedModel voption>>()

    fun path ->
      let loader =
        cache.GetOrAdd(
          path,
          fun p ->
            lazy
              (try
                lock content (fun () ->
                  let model = content.Load<Model>(p)
                  let loaded = LoadedModel.fromModel model

                  if not loaded.HasNormals then
                    printfn
                      $"[BlockEmitter] Model '{p}' missing normals, lighting will be disabled"

                  ValueSome loaded)
               with ex ->
                 printfn
                   $"[BlockEmitter] Failed to load model: {p} - {ex.Message}"

                 ValueNone)
        )

      loader.Value

  /// Emits MeshCommands for all visible blocks in the BlockMapDefinition.
  /// Uses 3D frustum culling based on view bounds and camera elevation.
  let emit
    (getLoadedModel: string -> LoadedModel voption)
    (blockMap: BlockMapDefinition)
    (viewBounds: struct (float32 * float32 * float32 * float32))
    (cameraY: float32)
    (visibleHeightRange: float32)
    (pixelsPerUnit: Vector2)
    : MeshCommand[] =
    let cellBounds =
      RenderMath.Camera.getViewCellBounds3D
        viewBounds
        cameraY
        CellSize
        visibleHeightRange

    let squishFactor = RenderMath.WorldMatrix.getSquishFactor pixelsPerUnit

    [|
      for kvp in blockMap.Blocks do
        let block = kvp.Value
        let cell = block.Cell

        if RenderMath.Camera.isInCellBounds cell.X cell.Y cell.Z cellBounds then
          match getBlockType blockMap block with
          | ValueSome blockType ->
            match getLoadedModel blockType.Model with
            | ValueSome loadedModel ->
              let worldPos = cellToWorldPosition cell

              let renderPos =
                RenderMath.LogicRender.toRender worldPos pixelsPerUnit

              let worldMatrix =
                RenderMath.WorldMatrix.createBlock
                  renderPos
                  block.Rotation
                  squishFactor

              {
                LoadedModel = loadedModel
                WorldMatrix = worldMatrix
              }
            | ValueNone -> ()
          | ValueNone -> ()
    |]

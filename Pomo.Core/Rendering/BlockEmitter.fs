namespace Pomo.Core.Rendering

open System.Collections.Concurrent
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Graphics

module BlockEmitter =
  open Pomo.Core.Domain.Core

  [<Literal>]
  let private ModelScale = 0.5f // KayKit models are 2x2 units, scale to 1x1

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
  /// Uses centralized RenderMath.BlockMap3D for coordinate conversion.
  let emit
    (getLoadedModel: string -> LoadedModel voption)
    (blockMap: BlockMapDefinition)
    (viewBounds: struct (float32 * float32 * float32 * float32))
    (cameraY: float32)
    (visibleHeightRange: float32)
    (pixelsPerUnit: Vector2)
    : MeshCommand[] =

    let ppu = pixelsPerUnit.X // Uniform scale for 3D
    let scaleFactor = CellSize / ppu

    let centerOffset =
      RenderMath.BlockMap3D.calcCenterOffset blockMap.Width blockMap.Depth ppu

    // Calculate cell bounds for culling
    // View bounds need to be adjusted for center offset
    let struct (viewLeft, viewRight, viewTop, viewBottom) = viewBounds

    let adjustedBounds =
      struct (viewLeft - centerOffset.X * ppu,
              viewRight - centerOffset.X * ppu,
              viewTop - centerOffset.Z * ppu,
              viewBottom - centerOffset.Z * ppu)

    let cellBounds =
      RenderMath.Camera.getViewCellBounds3D
        adjustedBounds
        cameraY
        CellSize
        visibleHeightRange

    [|
      for kvp in blockMap.Blocks do
        let block = kvp.Value
        let cell = block.Cell

        if RenderMath.Camera.isInCellBounds cell.X cell.Y cell.Z cellBounds then
          match getBlockType blockMap block with
          | ValueSome blockType ->
            match getLoadedModel blockType.Model with
            | ValueSome loadedModel ->
              // Use centralized cell->render conversion
              let pos =
                RenderMath.BlockMap3D.cellToRender
                  cell.X
                  cell.Y
                  cell.Z
                  ppu
                  centerOffset

              // World matrix: scale * rotation * translation
              let scale = Matrix.CreateScale(scaleFactor * ModelScale)

              let rot =
                match block.Rotation with
                | ValueSome q -> Matrix.CreateFromQuaternion q
                | ValueNone -> Matrix.Identity

              let trans = Matrix.CreateTranslation pos

              {
                LoadedModel = loadedModel
                WorldMatrix = scale * rot * trans
              }
            | ValueNone -> ()
          | ValueNone -> ()
    |]

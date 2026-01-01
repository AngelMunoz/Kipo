namespace Pomo.Core.Rendering

open System.Buffers
open System.Collections.Concurrent
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Graphics
open Pomo.Core.Domain.Core.Constants
open Pomo.Core.Algorithms

module BlockEmitter =

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

  /// Emits MeshCommands for all visible blocks using ArrayPool.
  /// Populates the buffer in-place, resizing via pool if needed.
  let emit
    (pool: ArrayPool<MeshCommand>)
    (buffer: byref<MeshCommand[]>)
    (count: byref<int>)
    (getLoadedModel: string -> LoadedModel voption)
    (blockMap: BlockMapDefinition)
    (viewBounds: struct (float32 * float32 * float32 * float32))
    (cameraY: float32)
    (visibleHeightRange: float32)
    (pixelsPerUnit: Vector2)
    : unit =

    let ppu = pixelsPerUnit.X // Uniform scale for 3D
    let scaleFactor = BlockMap.CellSize / ppu

    let centerOffset =
      RenderMath.BlockMap3D.calcCenterOffset blockMap.Width blockMap.Depth ppu

    let cellBounds =
      RenderMath.Camera.getViewCellBounds3D
        viewBounds
        cameraY
        BlockMap.CellSize
        visibleHeightRange

    count <- 0

    for kvp in blockMap.Blocks do
      let block = kvp.Value
      let cell = block.Cell

      if RenderMath.Camera.isInCellBounds cell.X cell.Y cell.Z cellBounds then
        match BlockMap.getBlockType blockMap block with
        | ValueSome blockType ->
          match getLoadedModel blockType.Model with
          | ValueSome loadedModel ->
            // Resize buffer if needed
            if count >= buffer.Length then
              let newSize = buffer.Length * 2
              let newBuffer = pool.Rent(newSize)
              System.Array.Copy(buffer, newBuffer, count)
              pool.Return(buffer)
              buffer <- newBuffer

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

            buffer.[count] <- {
              LoadedModel = loadedModel
              WorldMatrix = scale * rot * trans
            }

            count <- count + 1
          | ValueNone -> ()
        | ValueNone -> ()

  /// Convenience wrapper that returns a fresh array.
  /// Uses internal pooling but copies result to right-sized array.
  /// Use `emit` with explicit pool for zero-allocation hot paths.
  let emitToArray
    (getLoadedModel: string -> LoadedModel voption)
    (blockMap: BlockMapDefinition)
    (viewBounds: struct (float32 * float32 * float32 * float32))
    (cameraY: float32)
    (visibleHeightRange: float32)
    (pixelsPerUnit: Vector2)
    : MeshCommand[] =
    let pool = ArrayPool<MeshCommand>.Shared
    let mutable buffer = pool.Rent(256)
    let mutable count = 0

    try
      emit
        pool
        &buffer
        &count
        getLoadedModel
        blockMap
        viewBounds
        cameraY
        visibleHeightRange
        pixelsPerUnit
      // Copy to right-sized array
      let result = Array.zeroCreate count
      System.Array.Copy(buffer, result, count)
      result
    finally
      pool.Return(buffer)

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

  /// Computes render offset to center the map at origin.
  /// Matches EditorRender.calcRenderOffsets.
  let inline private calcCenterOffset
    (width: int)
    (depth: int)
    (scaleFactor: float32)
    : Vector3 =
    Vector3(
      -float32 width * scaleFactor * 0.5f,
      0f,
      -float32 depth * scaleFactor * 0.5f
    )

  /// Emits MeshCommands for all visible blocks in the BlockMapDefinition.
  /// Coordinate math matches EditorRender.populateCommands exactly.
  let emit
    (getLoadedModel: string -> LoadedModel voption)
    (blockMap: BlockMapDefinition)
    (viewBounds: struct (float32 * float32 * float32 * float32))
    (cameraY: float32)
    (visibleHeightRange: float32)
    (pixelsPerUnit: Vector2)
    : MeshCommand[] =

    // Render scale: CellSize / PPU (same as editor)
    let scaleFactor = CellSize / pixelsPerUnit.X

    let centerOffset =
      calcCenterOffset blockMap.Width blockMap.Depth scaleFactor

    let halfCell = scaleFactor * 0.5f

    // Calculate cell bounds for culling
    // View bounds need to be adjusted for center offset
    let struct (viewLeft, viewRight, viewTop, viewBottom) = viewBounds

    let adjustedBounds =
      struct (viewLeft - centerOffset.X * pixelsPerUnit.X,
              viewRight - centerOffset.X * pixelsPerUnit.X,
              viewTop - centerOffset.Z * pixelsPerUnit.X,
              viewBottom - centerOffset.Z * pixelsPerUnit.X)

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
              // Direct cell -> render position (matches editor)
              let x = float32 cell.X * scaleFactor + halfCell
              let y = float32 cell.Y * scaleFactor + halfCell
              let z = float32 cell.Z * scaleFactor + halfCell
              let pos = Vector3(x, y, z) + centerOffset

              // World matrix: scale * rotation * translation (matches editor)
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

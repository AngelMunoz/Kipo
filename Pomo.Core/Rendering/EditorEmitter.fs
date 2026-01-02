namespace Pomo.Core.Rendering

open System.Buffers
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core.Constants
open Pomo.Core.Graphics

/// Editor-specific rendering emitters for grid, cursor, and ghost block overlays.
/// Uses ArrayPool for GC-friendly per-frame geometry generation.
/// Note: This module takes primitive values as parameters to avoid depending on
/// the Editor namespace (which comes later in compilation order).
module EditorEmitter =

  [<Literal>]
  let private CursorVertCount = 24

  /// Calculate grid vertex count for a given map size
  let inline getGridVertCount (width: int) (depth: int) =
    (depth + 1) * 2 + (width + 1) * 2

  /// Populate grid vertices for the current editing layer.
  /// Returns the number of vertices written.
  let populateGridVerts
    (buffer: VertexPositionColor[])
    (layer: int)
    (width: int)
    (depth: int)
    (scaleFactor: float32)
    (centerOffset: Vector3)
    : int =
    let renderWidth = float32 width * scaleFactor
    let renderDepth = float32 depth * scaleFactor
    let layerHeight = float32 layer * scaleFactor
    let mutable idx = 0

    // Horizontal lines (along X axis)
    for z in 0..depth do
      let zPos = float32 z * scaleFactor

      buffer.[idx] <-
        VertexPositionColor(
          Vector3(0f, layerHeight, zPos) + centerOffset,
          Color.Gray
        )

      idx <- idx + 1

      buffer.[idx] <-
        VertexPositionColor(
          Vector3(renderWidth, layerHeight, zPos) + centerOffset,
          Color.Gray
        )

      idx <- idx + 1

    // Vertical lines (along Z axis)
    for x in 0..width do
      let xPos = float32 x * scaleFactor

      buffer.[idx] <-
        VertexPositionColor(
          Vector3(xPos, layerHeight, 0f) + centerOffset,
          Color.Gray
        )

      idx <- idx + 1

      buffer.[idx] <-
        VertexPositionColor(
          Vector3(xPos, layerHeight, renderDepth) + centerOffset,
          Color.Gray
        )

      idx <- idx + 1

    idx

  /// Populate cursor wireframe box vertices at the given cell.
  let populateCursorVerts
    (buffer: VertexPositionColor[])
    (cell: GridCell3D)
    (scaleFactor: float32)
    (centerOffset: Vector3)
    =
    let x = float32 cell.X * scaleFactor
    let y = float32 cell.Y * scaleFactor
    let z = float32 cell.Z * scaleFactor

    let basePos = Vector3(x, y, z) + centerOffset
    let size = scaleFactor

    // 8 corners of the cube
    let p0 = basePos + Vector3(0f, 0f, 0f)
    let p1 = basePos + Vector3(size, 0f, 0f)
    let p2 = basePos + Vector3(size, 0f, size)
    let p3 = basePos + Vector3(0f, 0f, size)
    let p4 = basePos + Vector3(0f, size, 0f)
    let p5 = basePos + Vector3(size, size, 0f)
    let p6 = basePos + Vector3(size, size, size)
    let p7 = basePos + Vector3(0f, size, size)

    let color = Color.Yellow

    // Bottom face edges
    buffer.[0] <- VertexPositionColor(p0, color)
    buffer.[1] <- VertexPositionColor(p1, color)
    buffer.[2] <- VertexPositionColor(p1, color)
    buffer.[3] <- VertexPositionColor(p2, color)
    buffer.[4] <- VertexPositionColor(p2, color)
    buffer.[5] <- VertexPositionColor(p3, color)
    buffer.[6] <- VertexPositionColor(p3, color)
    buffer.[7] <- VertexPositionColor(p0, color)

    // Top face edges
    buffer.[8] <- VertexPositionColor(p4, color)
    buffer.[9] <- VertexPositionColor(p5, color)
    buffer.[10] <- VertexPositionColor(p5, color)
    buffer.[11] <- VertexPositionColor(p6, color)
    buffer.[12] <- VertexPositionColor(p6, color)
    buffer.[13] <- VertexPositionColor(p7, color)
    buffer.[14] <- VertexPositionColor(p7, color)
    buffer.[15] <- VertexPositionColor(p4, color)

    // Vertical edges connecting top and bottom
    buffer.[16] <- VertexPositionColor(p0, color)
    buffer.[17] <- VertexPositionColor(p4, color)
    buffer.[18] <- VertexPositionColor(p1, color)
    buffer.[19] <- VertexPositionColor(p5, color)
    buffer.[20] <- VertexPositionColor(p2, color)
    buffer.[21] <- VertexPositionColor(p6, color)
    buffer.[22] <- VertexPositionColor(p3, color)
    buffer.[23] <- VertexPositionColor(p7, color)

  /// Emit a ghost block mesh command for the block about to be placed.
  /// Takes explicit parameters rather than EditorState to avoid namespace dependency.
  let emitGhost
    (cursor: GridCell3D voption)
    (selectedTypeId: int<BlockTypeId> voption)
    (blockMap: BlockMapDefinition)
    (currentRotation: Quaternion)
    (getModel: string -> LoadedModel voption)
    (scaleFactor: float32)
    (centerOffset: Vector3)
    : MeshCommand voption =
    match cursor, selectedTypeId with
    | ValueSome cell, ValueSome typeId ->
      match blockMap.Palette.TryGetValue typeId with
      | true, blockType ->
        match getModel blockType.Model with
        | ValueSome loadedModel ->
          let halfCell = scaleFactor * 0.5f
          let x = float32 cell.X * scaleFactor + halfCell
          let y = float32 cell.Y * scaleFactor + halfCell
          let z = float32 cell.Z * scaleFactor + halfCell
          let pos = Vector3(x, y, z) + centerOffset

          let scale =
            Matrix.CreateScale(scaleFactor * BlockMap.KayKitBlockModelScale)

          let rot = Matrix.CreateFromQuaternion(currentRotation)
          let trans = Matrix.CreateTranslation(pos)

          ValueSome {
            LoadedModel = loadedModel
            WorldMatrix = scale * rot * trans
          }
        | _ -> ValueNone
      | _ -> ValueNone
    | _ -> ValueNone

  /// Context for editor rendering, holds pooled buffers.
  type EditorRenderContext = {
    mutable GridBuffer: VertexPositionColor[]
    CursorBuffer: VertexPositionColor[]
    GridBufferPool: ArrayPool<VertexPositionColor>
  }

  /// Create a new editor render context with initial buffer sizes.
  let createContext() : EditorRenderContext =
    let pool = ArrayPool<VertexPositionColor>.Shared

    {
      GridBuffer = pool.Rent(1024)
      CursorBuffer = Array.zeroCreate<VertexPositionColor> CursorVertCount
      GridBufferPool = pool
    }

  /// Dispose of pooled resources.
  let disposeContext(ctx: EditorRenderContext) =
    ctx.GridBufferPool.Return(ctx.GridBuffer)

  /// Ensure grid buffer is large enough, resizing via pool if needed.
  let ensureGridBufferSize (ctx: EditorRenderContext) (needed: int) =
    if needed > ctx.GridBuffer.Length then
      ctx.GridBufferPool.Return(ctx.GridBuffer)
      ctx.GridBuffer <- ctx.GridBufferPool.Rent(needed * 2)

  /// Get the cursor vertex count constant.
  let getCursorVertCount() = CursorVertCount

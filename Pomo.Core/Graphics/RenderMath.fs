namespace Pomo.Core.Graphics

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Core.Constants

/// Coordinate Space Transformations for Isometric Rendering
///
/// Coordinate Spaces:
///   SCREEN  - Pixels, (0,0) at top-left of window
///   LOGIC   - 2D game world in pixels, origin at map top-left
///   RENDER  - 3D units for GPU: X/Z horizontal, Y = height + depth
///   PARTICLE - 3D physics: X/Z = logic plane, Y = altitude
///
/// Isometric Quirks:
///   - PPU ratio is 2:1 (e.g. 64x32) creating visual squish
///   - Altitude affects both height (Y) and depth sorting (Z)
///   - Mesh rotations include 45° offset for camera alignment
module RenderMath =

  /// Camera and projection matrix builders
  module Camera =
    /// Computes camera view bounds in logic space for culling.
    /// Returns struct(left, right, top, bottom).
    let inline getViewBounds
      (cameraPos: WorldPosition)
      (viewportWidth: float32)
      (viewportHeight: float32)
      (zoom: float32)
      : struct (float32 * float32 * float32 * float32) =
      let halfW = viewportWidth / (2.0f * zoom)
      let halfH = viewportHeight / (2.0f * zoom)

      struct (cameraPos.X - halfW,
              cameraPos.X + halfW,
              cameraPos.Z - halfH,
              cameraPos.Z + halfH)

    let inline private tryGetPixelsPerUnit
      (viewport: Viewport)
      (zoom: float32)
      (projection: Matrix)
      : struct (float32 * float32) voption =
      if zoom <= 0.0f || projection.M11 = 0.0f || projection.M22 = 0.0f then
        ValueNone
      else
        let ppuX = float32 viewport.Width * projection.M11 / (2.0f * zoom)
        let ppuZ = float32 viewport.Height * projection.M22 / (2.0f * zoom)

        if
          ppuX <= 0.0f || ppuZ <= 0.0f || Single.IsNaN ppuX || Single.IsNaN ppuZ
        then
          ValueNone
        else
          ValueSome(struct (ppuX, ppuZ))

    let inline private tryIntersectPlaneY
      (viewport: Viewport)
      (view: Matrix)
      (projection: Matrix)
      (screenPos: Vector2)
      (planeYRender: float32)
      : Vector3 voption =
      let nearSource = Vector3(screenPos.X, screenPos.Y, 0f)
      let farSource = Vector3(screenPos.X, screenPos.Y, 1f)

      let nearPoint =
        viewport.Unproject(nearSource, projection, view, Matrix.Identity)

      let farPoint =
        viewport.Unproject(farSource, projection, view, Matrix.Identity)

      let dir = Vector3.Normalize(farPoint - nearPoint)
      let ray = Ray(nearPoint, dir)
      let plane = Plane(Vector3.Up, -planeYRender)
      let dist = ray.Intersects plane

      if dist.HasValue then
        let p = ray.Position + ray.Direction * dist.Value

        if Single.IsNaN p.X || Single.IsNaN p.Y || Single.IsNaN p.Z then
          ValueNone
        else
          ValueSome p
      else
        ValueNone

    let tryGetViewBoundsFromMatrices
      (cameraPos: WorldPosition)
      (viewport: Viewport)
      (zoom: float32)
      (view: Matrix)
      (projection: Matrix)
      (visibleHeightRange: float32)
      : struct (float32 * float32 * float32 * float32) voption =
      tryGetPixelsPerUnit viewport zoom projection
      |> ValueOption.bind(fun struct (ppuX, ppuZ) ->
        let cx = float32 viewport.X + float32 viewport.Width / 2.0f
        let cy = float32 viewport.Y + float32 viewport.Height / 2.0f
        let centerScreen = Vector2(cx, cy)

        let planeYCenter = cameraPos.Y / ppuZ

        tryIntersectPlaneY viewport view projection centerScreen planeYCenter
        |> ValueOption.bind(fun centerHit ->
          let centerOffsetX = centerHit.X - (cameraPos.X / ppuX)
          let centerOffsetZ = centerHit.Z - (cameraPos.Z / ppuZ)

          let x0 = float32 viewport.X
          let y0 = float32 viewport.Y
          let x1 = x0 + float32 viewport.Width
          let y1 = y0 + float32 viewport.Height

          let corners = [|
            Vector2(x0, y0)
            Vector2(x1, y0)
            Vector2(x0, y1)
            Vector2(x1, y1)
          |]

          let minY = cameraPos.Y - visibleHeightRange
          let maxY = cameraPos.Y + visibleHeightRange

          let planeYs =
            if visibleHeightRange > 0.0f then
              [| minY / ppuZ; maxY / ppuZ |]
            else
              [| cameraPos.Y / ppuZ |]

          let mutable minX = Single.PositiveInfinity
          let mutable maxX = Single.NegativeInfinity
          let mutable minZ = Single.PositiveInfinity
          let mutable maxZ = Single.NegativeInfinity

          let mutable ok = true
          let mutable i = 0

          while ok && i < corners.Length do
            let corner = corners.[i]
            let mutable j = 0

            while ok && j < planeYs.Length do
              let planeY = planeYs.[j]

              match
                tryIntersectPlaneY viewport view projection corner planeY
              with
              | ValueSome hit ->
                let lx = (hit.X - centerOffsetX) * ppuX
                let lz = (hit.Z - centerOffsetZ) * ppuZ

                if Single.IsNaN lx || Single.IsNaN lz then
                  ok <- false
                else
                  if lx < minX then
                    minX <- lx

                  if lx > maxX then
                    maxX <- lx

                  if lz < minZ then
                    minZ <- lz

                  if lz > maxZ then
                    maxZ <- lz
              | ValueNone -> ok <- false

              j <- j + 1

            i <- i + 1

          if ok && minX <= maxX && minZ <= maxZ then
            ValueSome(struct (minX, maxX, minZ, maxZ))
          else
            ValueNone))

    /// Computes 3D cell bounds for block map culling.
    /// Converts view bounds to grid cell indices with margin for large models.
    /// Y bounds are based on camera elevation with visible height range.
    /// Returns struct(minX, maxX, minY, maxY, minZ, maxZ).
    let inline getViewCellBounds3D
      (viewBounds: struct (float32 * float32 * float32 * float32))
      (cameraY: float32)
      (cellSize: float32)
      (visibleHeightRange: float32)
      : struct (int * int * int * int * int * int) =
      let struct (viewLeft, viewRight, viewTop, viewBottom) = viewBounds
      let margin = cellSize * 2.0f // Margin for large models

      let minCellX = int((viewLeft - margin) / cellSize)
      let maxCellX = int((viewRight + margin) / cellSize) + 1
      let minCellZ = int((viewTop - margin) / cellSize)
      let maxCellZ = int((viewBottom + margin) / cellSize) + 1

      // Y culling based on camera elevation
      // In isometric, we see from above - cull blocks too far above/below camera focus
      let minCellY = int((cameraY - visibleHeightRange) / cellSize) |> max 0
      let maxCellY = int((cameraY + visibleHeightRange) / cellSize) + 1

      struct (minCellX, maxCellX, minCellY, maxCellY, minCellZ, maxCellZ)

    /// Checks if a cell is within the 3D cell bounds.
    let inline isInCellBounds
      (cellX: int)
      (cellY: int)
      (cellZ: int)
      (bounds: struct (int * int * int * int * int * int))
      : bool =
      let struct (minX, maxX, minY, maxY, minZ, maxZ) = bounds

      cellX >= minX
      && cellX <= maxX
      && cellY >= minY
      && cellY <= maxY
      && cellZ >= minZ
      && cellZ <= maxZ

  module WorldMatrix3D =
    let inline createMesh
      (renderPos: Vector3)
      (facing: float32)
      (scale: float32)
      : Matrix =
      Matrix.CreateRotationY(facing)
      * Matrix.CreateScale(scale)
      * Matrix.CreateTranslation(renderPos)

    let inline createProjectile
      (renderPos: Vector3)
      (facing: float32)
      (tilt: float32)
      (scale: float32)
      : Matrix =
      Matrix.CreateRotationX(tilt)
      * Matrix.CreateRotationY(facing)
      * Matrix.CreateScale(scale)
      * Matrix.CreateTranslation(renderPos)

    let inline createMeshParticle
      (renderPos: Vector3)
      (rotation: Quaternion)
      (baseScale: float32)
      (scaleAxis: Vector3)
      (pivot: Vector3)
      : Matrix =
      let s =
        Matrix.CreateScale(
          1.0f + (baseScale - 1.0f) * scaleAxis.X,
          1.0f + (baseScale - 1.0f) * scaleAxis.Y,
          1.0f + (baseScale - 1.0f) * scaleAxis.Z
        )

      Matrix.CreateFromQuaternion(rotation)
      * s
      * Matrix.CreateTranslation(pivot)
      * Matrix.CreateTranslation(renderPos)

  /// Skeletal animation transforms
  module Rig =
    /// Applies rig node transform with pivot-based rotation.
    /// Used for skeletal animation where rotation should happen around a joint.
    let inline applyNodeTransform
      (pivot: Vector3)
      (offset: Vector3)
      (animation: Matrix)
      : Matrix =
      let pivotT = Matrix.CreateTranslation pivot
      let inversePivotT = Matrix.CreateTranslation -pivot
      let offsetT = Matrix.CreateTranslation offset
      inversePivotT * animation * pivotT * offsetT

  /// Billboard rendering helpers
  module Billboard =
    /// Extracts billboard orientation vectors from view matrix
    let inline getVectors(view: inref<Matrix>) : struct (Vector3 * Vector3) =
      let inverseView = Matrix.Invert(view)
      struct (inverseView.Right, inverseView.Up)

  /// 3D BlockMap coordinate conversions (True 3D, no isometric squish)
  /// Uses uniform scale: all dimensions divided by same PPU value
  module BlockMap3D =

    /// Calculates render offset to center a BlockMap at origin.
    /// Returns render-space center offset vector.
    let inline calcCenterOffset
      (width: int)
      (depth: int)
      (ppu: float32)
      : Vector3 =
      let scaleFactor = BlockMap.CellSize / ppu

      Vector3(
        -float32 width * scaleFactor * 0.5f,
        0f,
        -float32 depth * scaleFactor * 0.5f
      )

    /// Converts WorldPosition to render-space position.
    /// WorldPosition is in units of (cell * CellSize).
    /// Uses uniform scale (PPU.X only) for true 3D rendering.
    let inline toRender
      (pos: WorldPosition)
      (ppu: float32)
      (centerOffset: Vector3)
      : Vector3 =
      Vector3(pos.X / ppu, pos.Y / ppu, pos.Z / ppu) + centerOffset

    /// Converts cell indices to render-space position (centered).
    /// Used by BlockEmitter for block positioning.
    let inline cellToRender
      (cellX: int)
      (cellY: int)
      (cellZ: int)
      (ppu: float32)
      (centerOffset: Vector3)
      : Vector3 =
      let scaleFactor = BlockMap.CellSize / ppu
      let halfCell = scaleFactor * 0.5f
      let x = float32 cellX * scaleFactor + halfCell
      let y = float32 cellY * scaleFactor + halfCell
      let z = float32 cellZ * scaleFactor + halfCell
      Vector3(x, y, z) + centerOffset

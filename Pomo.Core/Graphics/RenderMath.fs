namespace Pomo.Core.Graphics

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.Core

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

  /// Internal coordinate space primitives
  module private CoordSpace =
    let inline toRenderX (ppu: Vector2) (logicX: float32) = logicX / ppu.X
    let inline toRenderZBase (ppu: Vector2) (logicY: float32) = logicY / ppu.Y

    let inline toVisualHeight (zBase: float32) (altitude: float32) =
      altitude + zBase

    let inline toCameraDepth (zBase: float32) (altitude: float32) =
      zBase - altitude

    let inline correctParticleAltitude (ppu: Vector2) (particleY: float32) =
      (particleY / ppu.Y) * 2.0f

  /// Internal isometric matrix pipeline stages
  module private IsoPipeline =
    let isoLookAt =
      Matrix.CreateLookAt(
        Vector3.Zero,
        Vector3.Normalize(Vector3(-1.0f, -1.0f, -1.0f)),
        Vector3.Up
      )

    let topDownLookAt =
      Matrix.CreateLookAt(Vector3.Zero, Vector3.Down, Vector3.Forward)

    let isoCorrection = isoLookAt * Matrix.Invert topDownLookAt

    let inline alignToCamera(facing: float32) =
      Matrix.CreateRotationY(facing + MathHelper.PiOver4)

    let inline applyIsoCorrection(m: Matrix) = m * isoCorrection

    let inline applySquish (squishFactor: float32) (m: Matrix) =
      m * Matrix.CreateScale(1.0f, 1.0f, squishFactor)

    let inline applyScale (scale: float32) (m: Matrix) =
      m * Matrix.CreateScale(scale)

    let inline applyTilt(tilt: float32) = Matrix.CreateRotationX(tilt)

    let inline translateTo (pos: Vector3) (m: Matrix) =
      m * Matrix.CreateTranslation(pos)

    let inline applyAxisScale
      (baseScale: float32)
      (scaleAxis: Vector3)
      (m: Matrix)
      =
      let s =
        Matrix.CreateScale(
          1.0f + (baseScale - 1.0f) * scaleAxis.X,
          1.0f + (baseScale - 1.0f) * scaleAxis.Y,
          1.0f + (baseScale - 1.0f) * scaleAxis.Z
        )

      m * s

  /// Logic ↔ Render space conversions
  module LogicRender =
    open CoordSpace

    /// Converts a WorldPosition to Unified Render Space (3D Units).
    /// X = LogicX / PPU.X
    /// Z = (LogicZ / PPU.Y) - altitude (elevated objects sort behind ground objects)
    /// Y = Altitude + Z_base (visual height includes altitude and depth bias)
    let inline toRender
      (pos: WorldPosition)
      (pixelsPerUnit: Vector2)
      : Vector3 =
      let x = toRenderX pixelsPerUnit pos.X
      let zBase = toRenderZBase pixelsPerUnit pos.Z
      let y = toVisualHeight zBase pos.Y
      let z = toCameraDepth zBase pos.Y
      Vector3(x, y, z)

    /// Converts a Tile position (pixels) with explicit depth to Unified Render Space (3D Units).
    /// Used for terrain tiles where depthY is pre-calculated from tile bottom edge.
    /// X = LogicX / PPU.X, Z = LogicY / PPU.Y, Y = depthY (no addition)
    let inline tileToRender
      (logicPos: Vector2)
      (depthY: float32)
      (pixelsPerUnit: Vector2)
      : Vector3 =
      let x = toRenderX pixelsPerUnit logicPos.X
      let z = toRenderZBase pixelsPerUnit logicPos.Y
      Vector3(x, depthY, z)

  /// Particle space conversions
  module ParticleSpace =
    open CoordSpace

    /// Converts a 3D particle world position to Unified Render Space.
    /// Particles simulate in 3D where: X/Z = horizontal plane (logic space), Y = altitude.
    /// The altitude must be scaled by the isometric correction factor (Y / PPU.Y * 2.0)
    /// to match the visual proportions of the 2:1 isometric projection.
    let inline toRender
      (particlePos: Vector3)
      (pixelsPerUnit: Vector2)
      : Vector3 =
      let pos = {
        X = particlePos.X
        Y = correctParticleAltitude pixelsPerUnit particlePos.Y
        Z = particlePos.Z
      }

      LogicRender.toRender pos pixelsPerUnit

  /// Screen ↔ Logic space conversions (picking, UI)
  module ScreenLogic =
    /// Converts Screen coordinates to Logic coordinates (WorldPosition on ground plane Y=0).
    /// Used for Picking / Mouse interaction.
    let inline toLogic
      (screenPos: Vector2)
      (viewport: Viewport)
      (zoom: float32)
      (cameraPosition: WorldPosition)
      : WorldPosition =
      let screenCenter =
        Vector2(
          float32 viewport.X + float32 viewport.Width / 2.0f,
          float32 viewport.Y + float32 viewport.Height / 2.0f
        )

      let deltaPixels = screenPos - screenCenter
      let logicDelta = deltaPixels / zoom

      {
        X = cameraPosition.X + logicDelta.X
        Y = 0.0f // Project to ground plane
        Z = cameraPosition.Z + logicDelta.Y
      }

    /// Converts Logic coordinates to Screen coordinates.
    /// Inverse of toLogic.
    let inline toScreen
      (logicPos: WorldPosition)
      (viewport: Viewport)
      (zoom: float32)
      (cameraPosition: WorldPosition)
      : Vector2 =
      let screenCenter =
        Vector2(
          float32 viewport.X + float32 viewport.Width / 2.0f,
          float32 viewport.Y + float32 viewport.Height / 2.0f
        )

      let logicDeltaX = logicPos.X - cameraPosition.X
      let logicDeltaY = logicPos.Z - cameraPosition.Z // Use Z as 2D Y
      let deltaPixels = Vector2(logicDeltaX, logicDeltaY) * zoom
      screenCenter + deltaPixels

  /// Camera and projection matrix builders
  module Camera =
    open CoordSpace

    /// Calculates the View Matrix for the camera.
    /// Logic Position is the center of the camera in pixels.
    let getViewMatrix (pos: WorldPosition) (pixelsPerUnit: Vector2) : Matrix =
      let target =
        Vector3(
          toRenderX pixelsPerUnit pos.X,
          0.0f,
          toRenderZBase pixelsPerUnit pos.Z
        )

      let cameraPos = target + Vector3.Up * 100.0f
      Matrix.CreateLookAt(cameraPos, target, Vector3.Forward)

    /// Calculates the Orthographic Projection Matrix.
    let getProjectionMatrix
      (viewport: Viewport)
      (zoom: float32)
      (pixelsPerUnit: Vector2)
      : Matrix =
      let viewWidth = float32 viewport.Width / (zoom * pixelsPerUnit.X)
      let viewHeight = float32 viewport.Height / (zoom * pixelsPerUnit.Y)
      Matrix.CreateOrthographic(viewWidth, viewHeight, 0.1f, 5000.0f)

    /// Calculates the 2D transform matrix for SpriteBatch rendering.
    /// Used for background terrain and UI.
    let get2DViewMatrix
      (cameraPos: WorldPosition)
      (zoom: float32)
      (viewport: Viewport)
      : Matrix =
      Matrix.CreateTranslation(-cameraPos.X, -cameraPos.Z, 0.0f)
      * Matrix.CreateScale(zoom, zoom, 1.0f)
      * Matrix.CreateTranslation(
        float32 viewport.Width / 2.0f,
        float32 viewport.Height / 2.0f,
        0.0f
      )

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

  /// World matrix builders for 3D meshes
  module WorldMatrix =
    open IsoPipeline

    /// Transforms models from top-down orientation to isometric view.
    /// This rotates meshes modeled standing upright to display correctly
    /// in the 2:1 isometric projection.
    let IsometricCorrectionMatrix = isoCorrection

    /// Calculates the Squish Factor used for isometric correction.
    /// defined as PPU.X / PPU.Y
    let inline getSquishFactor(pixelsPerUnit: Vector2) : float32 =
      pixelsPerUnit.X / pixelsPerUnit.Y

    /// Calculates the World Matrix for a 3D entity (Mesh).
    /// Includes PiOver4 offset for isometric camera alignment.
    let createMesh
      (renderPos: Vector3)
      (facing: float32)
      (scale: float32)
      (squishFactor: float32)
      : Matrix =
      alignToCamera facing
      |> applyIsoCorrection
      |> applySquish squishFactor
      |> applyScale scale
      |> translateTo renderPos

    /// Calculates the World Matrix for a projectile with tilt (for descending/ascending).
    /// Tilt rotates around X axis before applying facing.
    /// Includes PiOver4 offset for isometric camera alignment.
    let createProjectile
      (renderPos: Vector3)
      (facing: float32)
      (tilt: float32)
      (scale: float32)
      (squishFactor: float32)
      : Matrix =
      applyTilt tilt
      |> fun m -> m * alignToCamera facing
      |> applyIsoCorrection
      |> applySquish squishFactor
      |> applyScale scale
      |> translateTo renderPos

    /// Calculates World Matrix for a mesh particle with non-uniform scaling.
    /// Used for tumbling debris, projectile trails, etc.
    let createMeshParticle
      (renderPos: Vector3)
      (rotation: Quaternion)
      (baseScale: float32)
      (scaleAxis: Vector3)
      (pivot: Vector3)
      (squishFactor: float32)
      : Matrix =
      Matrix.CreateFromQuaternion(rotation)
      |> applyAxisScale baseScale scaleAxis
      |> applyIsoCorrection
      |> applySquish squishFactor
      |> translateTo pivot
      |> translateTo renderPos

    /// Calculates World Matrix for a block in a block map.
    /// Applies optional rotation, isometric correction, squish, then translation.
    let createBlock
      (renderPos: Vector3)
      (rotation: Quaternion voption)
      (squishFactor: float32)
      : Matrix =
      let rotationMatrix =
        match rotation with
        | ValueSome q -> Matrix.CreateFromQuaternion(q)
        | ValueNone -> Matrix.Identity

      rotationMatrix
      |> applyIsoCorrection
      |> applySquish squishFactor
      |> translateTo renderPos

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

  /// Tile grid coordinate conversions
  module TileGrid =
    /// Converts tile grid coordinates to logic position based on map orientation.
    /// Handles orthogonal, isometric, and staggered map layouts.
    let toLogic
      (orientation: Pomo.Core.Domain.Map.Orientation)
      (staggerAxis: Pomo.Core.Domain.Map.StaggerAxis voption)
      (staggerIndex: Pomo.Core.Domain.Map.StaggerIndex voption)
      (mapWidth: int)
      (x: int)
      (y: int)
      (tileW: float32)
      (tileH: float32)
      : struct (float32 * float32) =
      match orientation, staggerAxis, staggerIndex with
      | Pomo.Core.Domain.Map.Staggered,
        ValueSome Pomo.Core.Domain.Map.X,
        ValueSome index ->
        let xStep = tileW / 2.0f
        let yStep = tileH
        let px = float32 x * xStep

        let isStaggeredCol =
          match index with
          | Pomo.Core.Domain.Map.Odd -> x % 2 = 1
          | Pomo.Core.Domain.Map.Even -> x % 2 = 0

        let yOffset = if isStaggeredCol then tileH / 2.0f else 0.0f
        struct (px, float32 y * yStep + yOffset)

      | Pomo.Core.Domain.Map.Staggered,
        ValueSome Pomo.Core.Domain.Map.Y,
        ValueSome index ->
        let xStep = tileW
        let yStep = tileH / 2.0f
        let py = float32 y * yStep

        let isStaggeredRow =
          match index with
          | Pomo.Core.Domain.Map.Odd -> y % 2 = 1
          | Pomo.Core.Domain.Map.Even -> y % 2 = 0

        let xOffset = if isStaggeredRow then tileW / 2.0f else 0.0f
        struct (float32 x * xStep + xOffset, py)

      | Pomo.Core.Domain.Map.Isometric, _, _ ->
        let originX = float32 mapWidth * tileW / 2.0f
        let px = originX + (float32 x - float32 y) * tileW / 2.0f
        let py = (float32 x + float32 y) * tileH / 2.0f
        struct (px, py)

      | _ -> struct (float32 x * tileW, float32 y * tileH)

  /// 3D BlockMap coordinate conversions (True 3D, no isometric squish)
  /// Uses uniform scale: all dimensions divided by same PPU value
  module BlockMap3D =
    open Pomo.Core.Domain.BlockMap

    /// Calculates render offset to center a BlockMap at origin.
    /// Returns render-space center offset vector.
    let inline calcCenterOffset
      (width: int)
      (depth: int)
      (ppu: float32)
      : Vector3 =
      let scaleFactor = CellSize / ppu

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
      let scaleFactor = CellSize / ppu
      let halfCell = scaleFactor * 0.5f
      let x = float32 cellX * scaleFactor + halfCell
      let y = float32 cellY * scaleFactor + halfCell
      let z = float32 cellZ * scaleFactor + halfCell
      Vector3(x, y, z) + centerOffset

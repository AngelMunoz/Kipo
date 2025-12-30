namespace Pomo.Core.Editor

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open FSharp.Data.Adaptive
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Spatial
open Pomo.Core.Graphics
open Pomo.Core.Rendering

module EditorRender =
  open Pomo.Core.Domain

  type RenderContext = {
    Device: GraphicsDevice
    View: Matrix
    Projection: Matrix
    PixelsPerUnit: Vector2
  }

  let getViewBounds (cam: EditorCameraState) (viewport: Viewport) =
    RenderMath.Camera.getViewBounds
      {
        X = cam.Position.X
        Y = cam.Position.Y
        Z = cam.Position.Z
      }
      (float32 viewport.Width)
      (float32 viewport.Height)
      cam.Zoom

  let emitGridVerts
    (layer: int)
    (width: int)
    (depth: int)
    (pixelsPerUnit: Vector2)
    (centerOffset: Vector3)
    =
    // Render Space dimensions - Uniform Scale
    // Use PPU.X (64) for all dimensions
    let scaleFactor = CellSize / pixelsPerUnit.X
    let renderWidth = float32 width * scaleFactor
    let renderDepth = float32 depth * scaleFactor
    let layerHeight = float32 layer * scaleFactor // Wait. layer * 32 / 64 = layer * 0.5.
    // If CellSize=32. Layer 10 -> 320. 320/64 = 5.
    // layerHeight = 5.

    [|
      for z in 0..depth do
        let zPos = float32 z * scaleFactor

        yield
          VertexPositionColor(
            Vector3(0f, layerHeight, zPos) + centerOffset,
            Color.Gray
          )

        yield
          VertexPositionColor(
            Vector3(renderWidth, layerHeight, zPos) + centerOffset,
            Color.Gray
          )

      for x in 0..width do
        let xPos = float32 x * scaleFactor

        yield
          VertexPositionColor(
            Vector3(xPos, layerHeight, 0f) + centerOffset,
            Color.Gray
          )

        yield
          VertexPositionColor(
            Vector3(xPos, layerHeight, renderDepth) + centerOffset,
            Color.Gray
          )
    |]

  let emitCursorVerts
    (cell: GridCell3D)
    (pixelsPerUnit: Vector2)
    (centerOffset: Vector3)
    =
    let scaleFactor = CellSize / pixelsPerUnit.X

    let x = float32 cell.X * scaleFactor
    let y = float32 cell.Y * scaleFactor
    let z = float32 cell.Z * scaleFactor

    let basePos = Vector3(x, y, z) + centerOffset
    let size = scaleFactor

    let mkV v =
      VertexPositionColor(basePos + v, Color.Yellow)

    // Wireframe Box
    let p0 = Vector3(0f, 0f, 0f)
    let p1 = Vector3(size, 0f, 0f)
    let p2 = Vector3(size, 0f, size)
    let p3 = Vector3(0f, 0f, size)
    let p4 = Vector3(0f, size, 0f)
    let p5 = Vector3(size, size, 0f)
    let p6 = Vector3(size, size, size)
    let p7 = Vector3(0f, size, size)

    [|
      // Bottom
      mkV p0
      mkV p1
      mkV p1
      mkV p2
      mkV p2
      mkV p3
      mkV p3
      mkV p0
      // Top
      mkV p4
      mkV p5
      mkV p5
      mkV p6
      mkV p6
      mkV p7
      mkV p7
      mkV p4
      // Verticals
      mkV p0
      mkV p4
      mkV p1
      mkV p5
      mkV p2
      mkV p6
      mkV p3
      mkV p7
    |]

  let renderMeshes (ctx: RenderContext) (commands: MeshCommand[]) =
    ctx.Device.DepthStencilState <- DepthStencilState.Default
    ctx.Device.BlendState <- BlendState.Opaque
    ctx.Device.RasterizerState <- RasterizerState.CullNone // Show both sides for checking

    for cmd in commands do
      for mesh in cmd.LoadedModel.Model.Meshes do
        for eff in mesh.Effects do
          match eff with
          | :? BasicEffect as be ->
            be.World <- cmd.WorldMatrix
            be.View <- ctx.View
            be.Projection <- ctx.Projection

            if cmd.LoadedModel.HasNormals then
              LightEmitter.applyDefaultLighting &be
            else
              be.LightingEnabled <- false
              be.TextureEnabled <- true
          | _ -> ()

        mesh.Draw()

  let renderLines
    (ctx: RenderContext)
    (effect: BasicEffect)
    (verts: VertexPositionColor[])
    =
    if verts.Length > 0 then
      effect.World <- Matrix.Identity
      effect.View <- ctx.View
      effect.Projection <- ctx.Projection

      for pass in effect.CurrentTechnique.Passes do
        pass.Apply()

        ctx.Device.DrawUserPrimitives(
          PrimitiveType.LineList,
          verts,
          0,
          verts.Length / 2
        )

  let draw
    (getModel: string -> LoadedModel voption)
    (effect: BasicEffect)
    (state: EditorState)
    (cam: EditorCameraState)
    (pixelsPerUnit: Vector2)
    (device: GraphicsDevice)
    =
    let viewport = device.Viewport

    let ctx = {
      Device = device
      View = EditorCamera.getViewMatrix cam
      Projection = EditorCamera.getProjectionMatrix cam viewport pixelsPerUnit
      PixelsPerUnit = pixelsPerUnit
    }

    let blockMap = state.BlockMap |> AVal.force
    let layer = state.CurrentLayer |> AVal.force
    let cursor = state.GridCursor |> AVal.force
    let viewBounds = getViewBounds cam viewport
    let struct (minX, minZ, maxX, maxZ) = viewBounds

    // Center offset to move map origin (0,0) to world center
    // Logic Offset (Huge) for culling
    let logicCenterOffset =
      Vector3(
        -float32 blockMap.Width * CellSize * 0.5f,
        0f,
        -float32 blockMap.Depth * CellSize * 0.5f
      )

    // Render Offset (Small) for visual positioning
    // Enforce UNIFORM SCALING for True 3D (Cube blocks)
    // Use PPU.X (64) as the standard divisor.
    let scaleFactor = CellSize / pixelsPerUnit.X

    let renderCenterOffset =
      Vector3(
        logicCenterOffset.X / pixelsPerUnit.X,
        logicCenterOffset.Y, // Y is 0
        logicCenterOffset.Z / pixelsPerUnit.X // Uniform scale Z
      )

    // Emit blocks with True 3D Logic (No Squish, No Iso Correction)
    let commands =
      // Adjust view bounds to Map Logic Space for correct culling
      // View is centered at 0. Map (0,0) is at logicCenterOffset (negative).
      // MapCoords = ViewCoords - LogicOffset.
      let struct (minX, minZ, maxX, maxZ) = viewBounds

      let adjustedBounds =
        struct (minX - logicCenterOffset.X,
                minZ - logicCenterOffset.Z,
                maxX - logicCenterOffset.X,
                maxZ - logicCenterOffset.Z)

      let cellBounds =
        RenderMath.Camera.getViewCellBounds3D
          adjustedBounds
          cam.Position.Y
          CellSize
          2000.0f // Increased vertical range just in case

      [|
        for kvp in blockMap.Blocks do
          let block = kvp.Value
          let cell = block.Cell

          if
            RenderMath.Camera.isInCellBounds cell.X cell.Y cell.Z cellBounds
          then
            match BlockMap.getBlockType blockMap block with
            | ValueSome typeInfo ->
              match getModel typeInfo.Model with
              | ValueSome loadedModel ->
                // Calculate True 3D Render Position
                // Offset by half cell size to center in grid cell (0..1 range)
                let halfCell = scaleFactor * 0.5f
                let x = float32 cell.X * scaleFactor + halfCell
                let y = float32 cell.Y * scaleFactor + halfCell
                let z = float32 cell.Z * scaleFactor + halfCell
                let pos = Vector3(x, y, z) + renderCenterOffset

                // World Matrix: Scale * Rotation * Translation
                // Scale relative to cell size.
                // scaleFactor = 32 / 64 = 0.5.
                // We use 0.5f because KayKit models are likely 2 units wide by default.
                let modelScale = 0.5f
                let scale = Matrix.CreateScale(scaleFactor * modelScale)

                let rot =
                  match block.Rotation with
                  | ValueSome q -> Matrix.CreateFromQuaternion q
                  | ValueNone -> Matrix.Identity

                let trans = Matrix.CreateTranslation(pos)

                yield {
                  LoadedModel = loadedModel
                  WorldMatrix = scale * rot * trans
                }
              | ValueNone -> ()
            | ValueNone -> ()
      |]

    commands |> renderMeshes ctx

    // Grid with Uniform Z Scale
    emitGridVerts
      layer
      blockMap.Width
      blockMap.Depth
      pixelsPerUnit
      renderCenterOffset
    |> renderLines ctx effect

    cursor
    |> ValueOption.iter(fun c ->
      emitCursorVerts c pixelsPerUnit renderCenterOffset
      |> renderLines ctx effect)

  let createSystem
    (game: Game)
    (state: EditorState)
    (cam: EditorCameraState)
    (pixelsPerUnit: Vector2)
    (content: ContentManager)
    : DrawableGameComponent =

    let mutable getBlockModel: (string -> LoadedModel voption) voption =
      ValueNone

    let mutable lineEffect: BasicEffect voption = ValueNone

    { new DrawableGameComponent(game, DrawOrder = 100) with
        override _.LoadContent() =
          getBlockModel <- ValueSome(BlockEmitter.createLazyModelLoader content)
          let effect = new BasicEffect(game.GraphicsDevice)
          effect.VertexColorEnabled <- true
          effect.LightingEnabled <- false
          lineEffect <- ValueSome effect

        override _.Draw _ =
          match getBlockModel, lineEffect with
          | ValueSome getModel, ValueSome effect ->
            draw getModel effect state cam pixelsPerUnit game.GraphicsDevice
          | _ -> ()
    }

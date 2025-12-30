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

  let emitBlockCommands
    (getModel: string -> LoadedModel voption)
    (blockMap: BlockMapDefinition)
    (cam: EditorCameraState)
    (viewBounds: struct (float32 * float32 * float32 * float32))
    (pixelsPerUnit: Vector2)
    (logicOffset: Vector3)
    (renderOffset: Vector3)
    =
    // Adjust culling bounds to map space (0..W) from centered render space (-W/2..W/2)
    // Culling uses Logic Units.
    let struct (minX, minZ, maxX, maxZ) = viewBounds

    let adjustedBounds =
      struct (minX - logicOffset.X,
              minZ - logicOffset.Z,
              maxX - logicOffset.X,
              maxZ - logicOffset.Z)

    let commands =
      BlockEmitter.emit
        getModel
        blockMap
        adjustedBounds
        cam.Position.Y
        (float32 blockMap.Height * CellSize)
        pixelsPerUnit

    // Apply Render Offset to shift blocks to centered world position
    let translation = Matrix.CreateTranslation(renderOffset)

    commands
    |> Array.map(fun cmd -> {
      cmd with
          WorldMatrix = cmd.WorldMatrix * translation
    })

  let emitGridVerts
    (layer: int)
    (width: int)
    (depth: int)
    (pixelsPerUnit: Vector2)
    (centerOffset: Vector3)
    =
    let squish = RenderMath.WorldMatrix.getSquishFactor pixelsPerUnit

    // Render Space dimensions
    let renderWidth = (float32 width * CellSize) / pixelsPerUnit.X
    let renderDepth = (float32 depth * CellSize) / pixelsPerUnit.Y

    [|
      for z in 0..depth do
        // Map Depth (Z) corresponds to Render Z
        let zPos = (float32 z * CellSize) / pixelsPerUnit.Y

        yield
          VertexPositionColor(
            Vector3(0f, float32 layer * squish, zPos) + centerOffset,
            Color.Gray
          )

        yield
          VertexPositionColor(
            Vector3(renderWidth, float32 layer * squish, zPos) + centerOffset,
            Color.Gray
          )

      for x in 0..width do
        let xPos = (float32 x * CellSize) / pixelsPerUnit.X

        yield
          VertexPositionColor(
            Vector3(xPos, float32 layer * squish, 0f) + centerOffset,
            Color.Gray
          )

        yield
          VertexPositionColor(
            Vector3(xPos, float32 layer * squish, renderDepth) + centerOffset,
            Color.Gray
          )
    |]

  let emitCursorVerts
    (cell: GridCell3D)
    (pixelsPerUnit: Vector2)
    (centerOffset: Vector3)
    =
    let worldPos = GridCell3D.toWorldPosition cell CellSize
    // Apply logic offset to world position before converting to render pos
    let adjustedPos: WorldPosition = {
      X = worldPos.X + centerOffset.X
      Y = worldPos.Y + centerOffset.Y
      Z = worldPos.Z + centerOffset.Z
    }

    let renderPos = RenderMath.LogicRender.toRender adjustedPos pixelsPerUnit

    let size = CellSize * 0.9f

    [|
      VertexPositionColor(
        renderPos + Vector3(-size / 2f, 0f, -size / 2f),
        Color.Yellow
      )
      VertexPositionColor(
        renderPos + Vector3(size / 2f, 0f, -size / 2f),
        Color.Yellow
      )
      VertexPositionColor(
        renderPos + Vector3(size / 2f, 0f, -size / 2f),
        Color.Yellow
      )
      VertexPositionColor(
        renderPos + Vector3(size / 2f, 0f, size / 2f),
        Color.Yellow
      )
      VertexPositionColor(
        renderPos + Vector3(size / 2f, 0f, size / 2f),
        Color.Yellow
      )
      VertexPositionColor(
        renderPos + Vector3(-size / 2f, 0f, size / 2f),
        Color.Yellow
      )
      VertexPositionColor(
        renderPos + Vector3(-size / 2f, 0f, size / 2f),
        Color.Yellow
      )
      VertexPositionColor(
        renderPos + Vector3(-size / 2f, 0f, -size / 2f),
        Color.Yellow
      )
    |]

  let renderMeshes (ctx: RenderContext) (commands: MeshCommand[]) =
    ctx.Device.DepthStencilState <- DepthStencilState.Default
    ctx.Device.BlendState <- BlendState.Opaque
    ctx.Device.RasterizerState <- RasterizerState.CullNone

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

    // Center offset to move map origin (0,0) to world center
    // Logic Offset (Huge) for culling
    let logicCenterOffset =
      Vector3(
        -float32 blockMap.Width * CellSize * 0.5f,
        0f,
        -float32 blockMap.Depth * CellSize * 0.5f
      )

    // Render Offset (Small) for visual positioning
    let renderCenterOffset =
      Vector3(
        logicCenterOffset.X / pixelsPerUnit.X,
        logicCenterOffset.Y, // Y is 0
        logicCenterOffset.Z / pixelsPerUnit.Y
      )

    emitBlockCommands
      getModel
      blockMap
      cam
      viewBounds
      pixelsPerUnit
      logicCenterOffset
      renderCenterOffset
    |> renderMeshes ctx

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

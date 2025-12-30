namespace Pomo.Core.Editor

open System.Collections.Generic
open System.Buffers
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

  [<Struct>]
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

  let private emitGridVerts
    (layer: int)
    (width: int)
    (depth: int)
    (pixelsPerUnit: Vector2)
    (centerOffset: Vector3)
    =
    let scaleFactor = CellSize / pixelsPerUnit.X
    let renderWidth = float32 width * scaleFactor
    let renderDepth = float32 depth * scaleFactor
    let layerHeight = float32 layer * scaleFactor

    [|
      for z in 0..depth do
        let zPos = float32 z * scaleFactor

        VertexPositionColor(
          Vector3(0f, layerHeight, zPos) + centerOffset,
          Color.Gray
        )

        VertexPositionColor(
          Vector3(renderWidth, layerHeight, zPos) + centerOffset,
          Color.Gray
        )
      for x in 0..width do
        let xPos = float32 x * scaleFactor

        VertexPositionColor(
          Vector3(xPos, layerHeight, 0f) + centerOffset,
          Color.Gray
        )

        VertexPositionColor(
          Vector3(xPos, layerHeight, renderDepth) + centerOffset,
          Color.Gray
        )
    |]

  let private emitCursorVerts
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

  let private renderMeshes
    (ctx: inref<RenderContext>)
    (count: int)
    (commands: MeshCommand[])
    =
    ctx.Device.DepthStencilState <- DepthStencilState.Default
    ctx.Device.BlendState <- BlendState.Opaque
    ctx.Device.RasterizerState <- RasterizerState.CullNone

    for i in 0 .. count - 1 do
      let cmd = commands.[i]

      for mesh in cmd.LoadedModel.Model.Meshes do
        for eff in mesh.Effects do
          match eff with
          | :? BasicEffect as be ->
            be.World <- cmd.WorldMatrix
            be.View <- ctx.View
            be.Projection <- ctx.Projection
            be.Alpha <- 1.0f

            if cmd.LoadedModel.HasNormals then
              LightEmitter.applyDefaultLighting &be
            else
              be.LightingEnabled <- false
              be.TextureEnabled <- true
          | _ -> ()

        mesh.Draw()

  let private renderGhost (ctx: inref<RenderContext>) (cmd: MeshCommand) =
    ctx.Device.DepthStencilState <- DepthStencilState.DepthRead
    ctx.Device.BlendState <- BlendState.AlphaBlend
    ctx.Device.RasterizerState <- RasterizerState.CullNone

    for mesh in cmd.LoadedModel.Model.Meshes do
      for eff in mesh.Effects do
        match eff with
        | :? BasicEffect as be ->
          be.World <- cmd.WorldMatrix
          be.View <- ctx.View
          be.Projection <- ctx.Projection
          be.Alpha <- 0.6f

          if cmd.LoadedModel.HasNormals then
            LightEmitter.applyDefaultLighting &be
          else
            be.LightingEnabled <- false
            be.TextureEnabled <- true
        | _ -> ()

      mesh.Draw()

      for eff in mesh.Effects do
        match eff with
        | :? BasicEffect as be -> be.Alpha <- 1.0f
        | _ -> ()

  let private renderLines
    (ctx: inref<RenderContext>)
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

  let inline private calcRenderOffsets
    (width: int)
    (depth: int)
    (ppu: Vector2)
    =
    let logicCenter =
      Vector3(
        -float32 width * CellSize * 0.5f,
        0f,
        -float32 depth * CellSize * 0.5f
      )

    let scale = CellSize / ppu.X

    let renderCenter =
      Vector3(logicCenter.X / ppu.X, logicCenter.Y, logicCenter.Z / ppu.X)

    struct (logicCenter, renderCenter, scale)

  let private populateCommands
    (blockMap: BlockMapDefinition)
    (getModel: string -> LoadedModel voption)
    (cellBounds: struct (int * int * int * int * int * int))
    (scaleFactor: float32)
    (renderCenterOffset: Vector3)
    (pool: ArrayPool<MeshCommand>)
    (commands: byref<MeshCommand[]>)
    (count: byref<int>)
    =
    let struct (minX, maxX, minY, maxY, minZ, maxZ) = cellBounds

    for x in minX..maxX do
      for z in minZ..maxZ do
        for y in minY..maxY do
          let cell = { X = x; Y = y; Z = z }

          match blockMap.Blocks.TryGetValue cell with
          | true, block ->
            match BlockMap.getBlockType blockMap block with
            | ValueSome typeInfo ->
              match getModel typeInfo.Model with
              | ValueSome loadedModel ->
                // Resize pool array if needed
                if count >= commands.Length then
                  let newSize = commands.Length * 2
                  let newArr = pool.Rent(newSize)
                  System.Array.Copy(commands, newArr, count)
                  pool.Return(commands)
                  commands <- newArr

                // Calculate Render Props
                let halfCell = scaleFactor * 0.5f
                let x = float32 cell.X * scaleFactor + halfCell
                let y = float32 cell.Y * scaleFactor + halfCell
                let z = float32 cell.Z * scaleFactor + halfCell
                let pos = Vector3(x, y, z) + renderCenterOffset

                // Scale relative to cell size (KayKit 2x2 assumption -> 0.5 scale)
                let modelScale = 0.5f
                let scale = Matrix.CreateScale(scaleFactor * modelScale)

                let rot =
                  match block.Rotation with
                  | ValueSome q -> Matrix.CreateFromQuaternion q
                  | ValueNone -> Matrix.Identity

                let trans = Matrix.CreateTranslation(pos)

                commands.[count] <- {
                  LoadedModel = loadedModel
                  WorldMatrix = scale * rot * trans
                }

                count <- count + 1
              | ValueNone -> ()
            | ValueNone -> ()
          | false, _ -> ()

  let private emitGhostBlock
    (state: EditorState)
    (getModel: string -> LoadedModel voption)
    (scaleFactor: float32)
    (renderCenterOffset: Vector3)
    : MeshCommand voption =
    match state.GridCursor.Value, state.SelectedBlockType.Value with
    | ValueSome cell, ValueSome typeId ->
      let map = state.BlockMap.Value

      match map.Palette.TryGetValue typeId with
      | true, blockType ->
        match getModel blockType.Model with
        | ValueSome loadedModel ->
          let halfCell = scaleFactor * 0.5f
          let x = float32 cell.X * scaleFactor + halfCell
          let y = float32 cell.Y * scaleFactor + halfCell
          let z = float32 cell.Z * scaleFactor + halfCell
          let pos = Vector3(x, y, z) + renderCenterOffset

          let modelScale = 0.5f
          let scale = Matrix.CreateScale(scaleFactor * modelScale)
          let rot = Matrix.CreateFromQuaternion(state.CurrentRotation.Value)
          let trans = Matrix.CreateTranslation(pos)

          ValueSome {
            LoadedModel = loadedModel
            WorldMatrix = scale * rot * trans
          }
        | _ -> ValueNone
      | _ -> ValueNone
    | _ -> ValueNone

  let draw
    (ctx: inref<RenderContext>)
    (getModel: string -> LoadedModel voption)
    (effect: BasicEffect)
    (state: EditorState)
    (cam: EditorCameraState)
    (pixelsPerUnit: Vector2)
    (viewport: Viewport)
    =
    let blockMap = state.BlockMap |> AVal.force
    let layer = state.CurrentLayer |> AVal.force
    let cursor = state.GridCursor |> AVal.force
    let viewBounds = getViewBounds cam viewport

    let struct (logicCenterOffset, renderCenterOffset, scaleFactor) =
      calcRenderOffsets blockMap.Width blockMap.Depth pixelsPerUnit

    // Calculate Culling Bounds
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
        2000.0f

    // Rent and Populate
    let pool = ArrayPool<MeshCommand>.Shared
    let mutable commands = pool.Rent(256)
    let mutable count = 0

    try
      populateCommands
        blockMap
        getModel
        cellBounds
        scaleFactor
        renderCenterOffset
        pool
        &commands
        &count

      renderMeshes &ctx count commands

      // Ghost Block
      match emitGhostBlock state getModel scaleFactor renderCenterOffset with
      | ValueSome ghost -> renderGhost &ctx ghost
      | ValueNone -> ()

    finally
      pool.Return(commands)

    // Grid and Cursor
    let verts =
      emitGridVerts
        layer
        blockMap.Width
        blockMap.Depth
        pixelsPerUnit
        renderCenterOffset

    renderLines &ctx effect verts

    match cursor with
    | ValueSome c ->
      let cursorVerts = emitCursorVerts c pixelsPerUnit renderCenterOffset
      renderLines &ctx effect cursorVerts
    | ValueNone -> ()

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

    // Mutable context to avoid per-frame allocation
    let mutable renderContext = {
      Device = game.GraphicsDevice
      View = Matrix.Identity
      Projection = Matrix.Identity
      PixelsPerUnit = pixelsPerUnit
    }

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
            let viewport = game.GraphicsDevice.Viewport

            // Update Context In-Place
            renderContext <- {
              renderContext with
                  View = EditorCamera.getViewMatrix cam
                  Projection =
                    EditorCamera.getProjectionMatrix cam viewport pixelsPerUnit
            }

            draw &renderContext getModel effect state cam pixelsPerUnit viewport
          | _ -> ()
    }

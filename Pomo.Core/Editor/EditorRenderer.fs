namespace Pomo.Core.Editor

open System.Buffers
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.Data.Adaptive
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Core.Constants
open Pomo.Core.Graphics
open Pomo.Core.Algorithms
open Pomo.Core.Rendering
open Pomo.Core.Systems

/// Lightweight editor renderer using shared RenderPasses from RenderOrchestrator.
/// Renders blocks via BlockEmitter and editor overlays via EditorEmitter.
module EditorRenderer =

  /// Create a DrawableGameComponent for editor rendering.
  let createSystem
    (game: Game)
    (editorState: EditorState)
    (camera: MutableCamera)
    (pixelsPerUnit: Vector2)
    : DrawableGameComponent =

    let mutable getBlockModel: (string -> LoadedModel voption) voption =
      ValueNone

    let mutable lineEffect: BasicEffect voption = ValueNone

    let mutable editorContext: EditorEmitter.EditorRenderContext voption =
      ValueNone

    let blockPool = ArrayPool<MeshCommand>.Shared
    let mutable blockBuffer = blockPool.Rent(256)
    let mutable blockCount = 0

    { new DrawableGameComponent(game, DrawOrder = 100) with
        override this.LoadContent() =
          getBlockModel <-
            ValueSome(BlockEmitter.createLazyModelLoader game.Content)

          let effect = new BasicEffect(game.GraphicsDevice)
          effect.VertexColorEnabled <- true
          effect.LightingEnabled <- false
          lineEffect <- ValueSome effect

          editorContext <- ValueSome(EditorEmitter.createContext())

        override this.Draw _ =
          match getBlockModel, lineEffect, editorContext with
          | ValueSome getModel, ValueSome effect, ValueSome ctx ->
            let viewport = game.GraphicsDevice.Viewport
            let blockMap = editorState.BlockMap |> AVal.force
            let layer = editorState.CurrentLayer |> AVal.force
            let cursor = editorState.GridCursor |> AVal.force
            let selectedType = editorState.SelectedBlockType |> AVal.force
            let rotation = editorState.CurrentRotation |> AVal.force

            // Calculate rendering parameters
            let ppu = pixelsPerUnit.X
            let scaleFactor = BlockMap.CellSize / ppu

            let centerOffset =
              RenderMath.BlockMap3D.calcCenterOffset
                blockMap.Width
                blockMap.Depth
                ppu

            // Camera matrices
            let view = EditorCamera.getViewMatrix camera

            let projection =
              EditorCamera.getProjectionMatrix camera viewport ppu

            // Clear
            game.GraphicsDevice.Clear Color.Black

            // Calculate view bounds for culling
            // Editor camera lives in render-space (scaled + centered), but culling
            // expects logic-space coordinates (pixels, un-centered).
            let cameraPosLogic: WorldPosition = {
              X = (camera.Params.Position.X - centerOffset.X) * ppu
              Y = (camera.Params.Position.Y - centerOffset.Y) * ppu
              Z = (camera.Params.Position.Z - centerOffset.Z) * ppu
            }

            let viewBoundsFallback =
              RenderMath.Camera.getViewBounds
                cameraPosLogic
                (float32 viewport.Width)
                (float32 viewport.Height)
                camera.Params.Zoom

            let visibleHeightRange = float32 blockMap.Height * BlockMap.CellSize

            let viewBounds =
              RenderMath.Camera.tryGetViewBoundsFromMatrices
                cameraPosLogic
                viewport
                camera.Params.Zoom
                view
                projection
                visibleHeightRange
              |> ValueOption.defaultValue viewBoundsFallback

            // 1. Emit and render blocks using shared RenderPasses
            BlockEmitter.emit
              blockPool
              &blockBuffer
              &blockCount
              getModel
              blockMap
              viewBounds
              cameraPosLogic.Y
              visibleHeightRange
              pixelsPerUnit

            RenderOrchestrator.RenderPasses.renderMeshes
              game.GraphicsDevice
              &view
              &projection
              blockBuffer
              (ValueSome blockCount)

            // 2. Ghost block
            match
              EditorEmitter.emitGhost
                cursor
                selectedType
                blockMap
                rotation
                getModel
                scaleFactor
                centerOffset
            with
            | ValueSome ghost ->
              RenderOrchestrator.RenderPasses.renderGhost
                game.GraphicsDevice
                &view
                &projection
                ghost
            | ValueNone -> ()

            // 3. Grid
            let needed =
              EditorEmitter.getGridVertCount blockMap.Width blockMap.Depth

            EditorEmitter.ensureGridBufferSize ctx needed

            let gridVertCount =
              EditorEmitter.populateGridVerts
                ctx.GridBuffer
                layer
                blockMap.Width
                blockMap.Depth
                scaleFactor
                centerOffset

            RenderOrchestrator.RenderPasses.renderLines
              game.GraphicsDevice
              effect
              &view
              &projection
              ctx.GridBuffer
              gridVertCount

            // 4. Cursor
            match cursor with
            | ValueSome cell ->
              EditorEmitter.populateCursorVerts
                ctx.CursorBuffer
                cell
                scaleFactor
                centerOffset

              RenderOrchestrator.RenderPasses.renderLines
                game.GraphicsDevice
                effect
                &view
                &projection
                ctx.CursorBuffer
                (EditorEmitter.getCursorVertCount())
            | ValueNone -> ()

          | _ -> ()

        override this.Dispose(disposing) =
          if disposing then
            editorContext |> ValueOption.iter EditorEmitter.disposeContext
            blockPool.Return(blockBuffer)
            lineEffect |> ValueOption.iter(fun e -> e.Dispose())

          base.Dispose(disposing)
    }

namespace Pomo.Core.App

open System.Buffers
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.Data.Adaptive
open Mibo.Elmish
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Core.Constants
open Pomo.Core.Graphics
open Pomo.Core.Algorithms
open Pomo.Core.Rendering
open Pomo.Core.Systems
open Pomo.Core.Editor
open Pomo.Core.Systems.UIService

/// Editor renderer wrapped as Mibo IRenderer<AppModel>.
/// Handles both rendering AND input (via direct MonoGame state).
module AppEditorRenderer =

  type RenderState = {
    GetBlockModel: string -> LoadedModel voption
    LineEffect: BasicEffect
    EditorContext: EditorEmitter.EditorRenderContext
    BlockPool: ArrayPool<MeshCommand>
    mutable BlockBuffer: MeshCommand[]
    mutable BlockCount: int
  }

  let private createRenderState(game: Game) : RenderState =
    let effect = new BasicEffect(game.GraphicsDevice)
    effect.VertexColorEnabled <- true
    effect.LightingEnabled <- false

    {
      GetBlockModel = BlockEmitter.createLazyModelLoader game.Content
      LineEffect = effect
      EditorContext = EditorEmitter.createContext()
      BlockPool = ArrayPool<MeshCommand>.Shared
      BlockBuffer = ArrayPool<MeshCommand>.Shared.Rent(256)
      BlockCount = 0
    }

  let private renderEditor
    (gd: GraphicsDevice)
    (state: RenderState)
    (session: EditorSession)
    (pixelsPerUnit: Vector2)
    =
    let viewport = gd.Viewport
    let editorState = session.State
    let camera = session.Camera

    let blockMap = editorState.BlockMap |> AVal.force
    let layer = editorState.CurrentLayer |> AVal.force
    let cursor = editorState.GridCursor |> AVal.force
    let selectedType = editorState.SelectedBlockType |> AVal.force
    let rotation = editorState.CurrentRotation |> AVal.force

    // Calculate rendering parameters
    let ppu = pixelsPerUnit.X
    let scaleFactor = BlockMap.CellSize / ppu

    let centerOffset =
      RenderMath.BlockMap3D.calcCenterOffset blockMap.Width blockMap.Depth ppu

    // Camera matrices
    let view = EditorCamera.getViewMatrix camera
    let projection = EditorCamera.getProjectionMatrix camera viewport ppu

    // Clear
    gd.Clear Color.Black

    // Calculate view bounds for culling
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

    // 1. Emit and render blocks
    BlockEmitter.emit
      state.BlockPool
      &state.BlockBuffer
      &state.BlockCount
      state.GetBlockModel
      blockMap
      viewBounds
      cameraPosLogic.Y
      visibleHeightRange
      pixelsPerUnit

    RenderOrchestrator.RenderPasses.renderMeshes
      gd
      &view
      &projection
      state.BlockBuffer
      (ValueSome state.BlockCount)

    // 2. Ghost block
    match
      EditorEmitter.emitGhost
        cursor
        selectedType
        blockMap
        rotation
        state.GetBlockModel
        scaleFactor
        centerOffset
    with
    | ValueSome ghost ->
      RenderOrchestrator.RenderPasses.renderGhost gd &view &projection ghost
    | ValueNone -> ()

    // 3. Grid
    let needed = EditorEmitter.getGridVertCount blockMap.Width blockMap.Depth

    EditorEmitter.ensureGridBufferSize state.EditorContext needed

    let gridVertCount =
      EditorEmitter.populateGridVerts
        state.EditorContext.GridBuffer
        layer
        blockMap.Width
        blockMap.Depth
        scaleFactor
        centerOffset

    RenderOrchestrator.RenderPasses.renderLines
      gd
      state.LineEffect
      &view
      &projection
      state.EditorContext.GridBuffer
      gridVertCount

    // 4. Cursor
    match cursor with
    | ValueSome cell ->
      EditorEmitter.populateCursorVerts
        state.EditorContext.CursorBuffer
        cell
        scaleFactor
        centerOffset

      RenderOrchestrator.RenderPasses.renderLines
        gd
        state.LineEffect
        &view
        &projection
        state.EditorContext.CursorBuffer
        (EditorEmitter.getCursorVertCount())
    | ValueNone -> ()

  /// Create an IRenderer<AppModel> that renders the Editor scene.
  /// Also processes input via IInput service from context.
  let create
    (game: Game)
    (services: AppServices)
    (pixelsPerUnit: Vector2)
    : IRenderer<AppModel> =
    let mutable renderState: RenderState voption = ValueNone

    { new IRenderer<AppModel> with
        member _.Draw(ctx, model, gameTime) =
          match model.EditorSession with
          | ValueSome session ->
            // Lazy initialize render state
            let state =
              match renderState with
              | ValueSome s -> s
              | ValueNone ->
                let s = createRenderState game
                renderState <- ValueSome s
                s

            // Process input (uses MonoGame direct state)
            let dt = float32 gameTime.ElapsedGameTime.TotalSeconds

            let onPlaytest() =
              let currentMap = session.State.BlockMap |> AVal.force
              let path = $"Content/CustomMaps/{currentMap.Key}.json"

              BlockMapLoader.save
                BlockMapLoader.Resolvers.editor
                path
                currentMap
              |> ignore

              // Use global dispatcher for scene transition
              AppEvents.dispatch(GuiTriggered GuiAction.StartNewGame)

            AppEditorInput.processInput
              ctx.GraphicsDevice
              session.State
              session.Camera
              services.UIService
              onPlaytest
              dt
              session.InputContext

            // Render
            renderEditor ctx.GraphicsDevice state session pixelsPerUnit
          | ValueNone ->
            // Not in editor mode - don't render
            ()
    }

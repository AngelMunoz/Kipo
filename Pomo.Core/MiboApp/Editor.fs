namespace Pomo.Core.MiboApp

open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Graphics
open FSharp.Data.Adaptive
open FSharp.UMX
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.Core.Constants
open Pomo.Core.Graphics
open Pomo.Core.Rendering
open Pomo.Core.Algorithms



module Editor =
  open Pomo.Core.Systems

  // Alias Core types to avoid collision with MiboApp types
  type CoreEditorState = Pomo.Core.Editor.EditorState
  type CoreInputContext = Pomo.Core.Editor.EditorInput.EditorInputContext
  type CoreMutableCamera = Pomo.Core.Editor.MutableCamera


  let private createInputContext(viewport: Viewport) : CoreInputContext = {
    Keyboard = Keyboard.GetState()
    PrevKeyboard = Keyboard.GetState()
    Mouse = Mouse.GetState()
    PrevMouse = Mouse.GetState()
    Viewport = viewport
    PixelsPerUnit =
      Vector2(float32 BlockMap.CellSize, float32 BlockMap.CellSize)
    DeltaTime = 0.0f
    RotationTimer = 0.0f
    UndoRedoTimer = 0.0f
    LastPaintedCell = { X = -1; Y = -1; Z = -1 }
  }

  let loadMap
    (viewport: Viewport)
    (path: string)
    : Result<EditorState, string> =
    match BlockMapLoader.load BlockMapLoader.Resolvers.editor path with
    | Ok blockMap ->
      let logic = Pomo.Core.Editor.EditorState.create blockMap

      // Initialize camera
      let cam = CoreMutableCamera()

      cam.Params <- {
        Camera.Defaults.defaultParams with
            Position = Vector3.Zero
      }

      Ok {
        Logic = logic
        InputContext = createInputContext viewport
        Camera = cam
      }
    | Error err -> Error err

  let createEmpty(viewport: Viewport) : EditorState =
    let palette = Dictionary<int<BlockTypeId>, BlockType>()
    let testBlockId = 1<BlockTypeId>

    palette.Add(
      testBlockId,
      {
        Id = testBlockId
        ArchetypeId = testBlockId
        VariantKey = ValueNone
        Name = "Grass"
        Model = "Tiles/kaykit_blocks/grass"
        Category = "Terrain"
        CollisionType = Box
        Effect = ValueNone
      }
    )

    let blocks = Dictionary<GridCell3D, PlacedBlock>()

    for x = 0 to 4 do
      for z = 0 to 4 do
        let cell: GridCell3D = { X = x; Y = 0; Z = z }

        blocks.Add(
          cell,
          {
            Cell = cell
            BlockTypeId = testBlockId
            Rotation = ValueNone
          }
        )

    let blockMap: BlockMapDefinition = {
      Version = 1
      Key = "test"
      MapKey = ValueNone
      Width = 32
      Height = 8
      Depth = 32
      Palette = palette
      Blocks = blocks
      SpawnCell = ValueNone
      Settings = {
        EngagementRules = PvE
        MaxEnemyEntities = 50
        StartingLayer = 0
      }
      Objects = []
    }

    let logic = Pomo.Core.Editor.EditorState.create blockMap
    let cam = CoreMutableCamera()

    cam.Params <- {
      Camera.Defaults.defaultParams with
          Position = Vector3.Zero
    }

    {
      Logic = logic
      InputContext = createInputContext viewport
      Camera = cam
    }

  let update (env: AppEnv) (gt: GameTime) (state: EditorState) : EditorState =
    // Update input context
    let ctx = state.InputContext
    ctx.PrevKeyboard <- ctx.Keyboard
    ctx.PrevMouse <- ctx.Mouse
    ctx.Keyboard <- Keyboard.GetState()
    ctx.Mouse <- Mouse.GetState()

    // Check viewport changes?
    // ctx.Viewport <- ... (Assuming static viewport for now or passed from somewhere else)

    ctx.DeltaTime <- float32 gt.ElapsedGameTime.TotalSeconds

    // Update UI state and input
    // The UI Service needs to know about the reactive state now!
    // We handle that in UIService.Rebuild, but we need to ensure it's called.
    env.UI.Rebuild(Editor state)
    env.UI.Update()

    // Handle Camera Input
    Pomo.Core.Editor.EditorInput.handleCameraInput
      state.Logic
      state.Camera
      ctx.Keyboard
      ctx.Mouse
      ctx.PrevMouse
      ctx.DeltaTime

    // Handle Editor Input
    let onPlaytest() =
      printfn "Playtest requested (not implemented in MiboApp yet)"

    Pomo.Core.Editor.EditorInput.handleEditorInput
      state.Logic
      state.Camera
      env.Core.UIService // Use Core UI service for IsMouseOverUI check
      onPlaytest
      ctx

    state

  // Line rendering effect (cached)
  let mutable private lineEffect: BasicEffect option = None

  let private getLineEffect(gd: GraphicsDevice) =
    match lineEffect with
    | Some e -> e
    | None ->
      let e = new BasicEffect(gd)
      e.VertexColorEnabled <- true
      lineEffect <- Some e
      e

  // Grid and cursor buffers
  let mutable private gridBuffer: VertexPositionColor[] = Array.zeroCreate 1024
  let mutable private cursorBuffer: VertexPositionColor[] = Array.zeroCreate 24

  let view
    (env: AppEnv)
    (ctx: GameContext)
    (state: EditorState)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    let viewport = ctx.GraphicsDevice.Viewport
    let ppu = BlockMap.CellSize
    let scaleFactor = BlockMap.CellSize / ppu

    // Use MutableCamera params
    let viewMatrix = Pomo.Core.Editor.EditorCamera.getViewMatrix state.Camera

    let projMatrix =
      Pomo.Core.Editor.EditorCamera.getProjectionMatrix
        state.Camera
        viewport
        ppu

    let miboCamera = {
      View = viewMatrix
      Projection = projMatrix
    }

    Draw3D.viewport viewport buffer
    Draw3D.clear (ValueSome Color.CornflowerBlue) true buffer
    Draw3D.camera miboCamera buffer

    // Access Logic state
    let blockMap = state.Logic.BlockMap |> AVal.force

    let centerOffset =
      RenderMath.BlockMap3D.calcCenterOffset blockMap.Width blockMap.Depth ppu

    // Draw blocks
    for block in blockMap.Blocks do
      let cell = block.Key
      let blockData = block.Value

      match blockMap.Palette.TryGetValue(blockData.BlockTypeId) with
      | true, blockType ->
        let rawModel = ctx |> Assets.model blockType.Model

        let pos =
          RenderMath.BlockMap3D.cellToRender
            cell.X
            cell.Y
            cell.Z
            ppu
            centerOffset

        // Use cell.Y from block, verify if we need to account for CurrentLayer visual clipping?
        // The original code drew all blocks.

        let scale =
          Matrix.CreateScale(scaleFactor * BlockMap.KayKitBlockModelScale)

        let rot =
          match blockData.Rotation with
          | ValueSome q -> Matrix.CreateFromQuaternion q
          | ValueNone -> Matrix.Identity

        let trans = Matrix.CreateTranslation(pos)
        let world = scale * rot * trans

        Draw3D.mesh rawModel world
        |> Draw3D.withBasicEffect
        |> Draw3D.submit buffer
      | _ -> ()

    // Ghost block
    let getModel asset =
      let rawModel = ctx |> Assets.model asset
      ValueSome(LoadedModel.fromModel rawModel)

    // Using Logic state values
    let gridCursor = state.Logic.GridCursor |> AVal.force
    let selectedBlockType = state.Logic.SelectedBlockType |> AVal.force
    let currentRotation = state.Logic.CurrentRotation |> AVal.force

    match
      EditorEmitter.emitGhost
        gridCursor
        selectedBlockType
        blockMap
        currentRotation
        getModel
        scaleFactor
        centerOffset
    with
    | ValueSome ghost ->
      Draw3D.custom
        (fun (gameCtx, view, proj) ->
          let gd = gameCtx.GraphicsDevice
          gd.DepthStencilState <- DepthStencilState.DepthRead
          gd.BlendState <- BlendState.AlphaBlend
          gd.RasterizerState <- RasterizerState.CullNone

          for mesh in ghost.LoadedModel.Model.Meshes do
            // Capture state to restore after drawing (since Model effects are shared)
            let restoreOps = System.Collections.Generic.List<unit -> unit>()

            for eff in mesh.Effects do
              match eff with
              | :? BasicEffect as be ->
                let oldAlpha = be.Alpha
                let oldLighting = be.LightingEnabled

                restoreOps.Add(fun () ->
                  be.Alpha <- oldAlpha
                  be.LightingEnabled <- oldLighting)

                be.World <- ghost.WorldMatrix
                be.View <- view
                be.Projection <- proj
                be.Alpha <- 0.6f
                be.LightingEnabled <- false
                be.TextureEnabled <- true
              | _ -> ()

            mesh.Draw()

            // Restore shared effect state
            for restore in restoreOps do
              restore())
        buffer
    | ValueNone -> ()

    // Grid and cursor
    // Grid
    let needed = EditorEmitter.getGridVertCount blockMap.Width blockMap.Depth

    if needed > gridBuffer.Length then
      gridBuffer <- Array.zeroCreate(needed * 2)

    let currentLayer = state.Logic.CurrentLayer |> AVal.force

    let gridVertCount =
      EditorEmitter.populateGridVerts
        gridBuffer
        currentLayer
        blockMap.Width
        blockMap.Depth
        scaleFactor
        centerOffset

    let gridLineCount = gridVertCount / 2

    if gridLineCount > 0 then
      for i in 0..2 .. (gridVertCount - 2) do
        let v1 = gridBuffer.[i]
        let v2 = gridBuffer.[i + 1]
        Draw3D.line v1.Position v2.Position v1.Color buffer

    // Cursor wireframe and points
    match gridCursor with
    | ValueSome cell ->
      EditorEmitter.populateCursorVerts
        cursorBuffer
        cell
        scaleFactor
        centerOffset

      // Draw lines
      for i in 0..2..22 do
        let v1 = cursorBuffer.[i]
        let v2 = cursorBuffer.[i + 1]
        Draw3D.line v1.Position v2.Position v1.Color buffer

    // Also draw points (as small crosses maybe? or lines?)
    // Mibo doesn't have Draw3D.point likely, so skipping points for now unless requested or using tiny lines
    | ValueNone -> ()

    // Render UI last so it appears on top of the 3D scene
    Draw3D.custom (fun _ -> env.UI.Render()) buffer

namespace Pomo.Core.MiboApp

open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.Core.Constants
open Pomo.Core.Graphics

type EditorState = {
  BlockMap: BlockMapDefinition
  CameraParams: Camera.CameraParams
  CurrentLayer: int
  GridCursor: GridCell3D voption
  SelectedBlockType: string voption
  CurrentRotation: int
}

module Editor =
  open Pomo.Core.Systems

  let private createCameraParams(blockMap: BlockMapDefinition) =
    // Blocks are centered at origin via calcCenterOffset, so camera starts at origin
    {
      Camera.Defaults.defaultParams with
          Position = Vector3.Zero
    }

  let loadMap(path: string) : Result<EditorState, string> =
    match BlockMapLoader.load BlockMapLoader.Resolvers.editor path with
    | Ok blockMap ->
      Ok {
        BlockMap = blockMap
        CameraParams = createCameraParams blockMap
        CurrentLayer = blockMap.Settings.StartingLayer
        GridCursor = ValueNone
        SelectedBlockType = ValueNone
        CurrentRotation = 0
      }
    | Error err -> Error err

  let createEmpty() : EditorState =
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

    let blockMap = {
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

    {
      BlockMap = blockMap
      CameraParams = createCameraParams blockMap
      CurrentLayer = 0
      GridCursor = ValueNone
      SelectedBlockType = ValueNone
      CurrentRotation = 0
    }

  let private processCamera
    (state: EditorState)
    (dt: float32)
    (keyboard: KeyboardState)
    =
    let speed = 5f * dt
    let mutable camParams = state.CameraParams

    if keyboard.IsKeyDown Keys.W then
      camParams <- Camera.Transform.panRelative camParams 0f -speed

    if keyboard.IsKeyDown Keys.S then
      camParams <- Camera.Transform.panRelative camParams 0f speed

    if keyboard.IsKeyDown Keys.A then
      camParams <- Camera.Transform.panRelative camParams -speed 0f

    if keyboard.IsKeyDown Keys.D then
      camParams <- Camera.Transform.panRelative camParams speed 0f

    if camParams = state.CameraParams then
      state
    else
      { state with CameraParams = camParams }

  let update (gt: GameTime) (state: EditorState) : EditorState =
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    let keyboard = Keyboard.GetState()
    processCamera state dt keyboard

  let view
    (ctx: GameContext)
    (state: EditorState)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    let viewport = ctx.GraphicsDevice.Viewport
    let ppu = BlockMap.CellSize
    let scaleFactor = BlockMap.CellSize / ppu

    // Compute Mibo Camera from Kipo CameraParams
    let view = Camera.Compute.getViewMatrix state.CameraParams

    let projection =
      Camera.Compute.getProjectionMatrix state.CameraParams viewport ppu

    let miboCamera = { View = view; Projection = projection }

    Draw3D.viewport viewport buffer
    Draw3D.clear (ValueSome Color.CornflowerBlue) true buffer
    Draw3D.camera miboCamera buffer

    let blockMap = state.BlockMap

    let centerOffset =
      RenderMath.BlockMap3D.calcCenterOffset blockMap.Width blockMap.Depth ppu

    for block in blockMap.Blocks do
      let cell = block.Key
      let blockData = block.Value

      match blockMap.Palette.TryGetValue(blockData.BlockTypeId) with
      | true, blockType ->
        let model = ctx |> Assets.model blockType.Model

        let pos =
          RenderMath.BlockMap3D.cellToRender
            cell.X
            cell.Y
            cell.Z
            ppu
            centerOffset

        let scale =
          Matrix.CreateScale(scaleFactor * BlockMap.KayKitBlockModelScale)

        let rot =
          match blockData.Rotation with
          | ValueSome q -> Matrix.CreateFromQuaternion q
          | ValueNone -> Matrix.Identity

        let trans = Matrix.CreateTranslation(pos)
        let world = scale * rot * trans

        Draw3D.mesh model world
        |> Draw3D.withBasicEffect
        |> Draw3D.submit buffer
      | _ -> ()

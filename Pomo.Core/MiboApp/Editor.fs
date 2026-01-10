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

type EditorState = {
  BlockMap: BlockMapDefinition
  Camera: Camera
  CurrentLayer: int
  GridCursor: GridCell3D voption
  SelectedBlockType: string voption
  CurrentRotation: int
}

module Editor =

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

    {
      BlockMap = {
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
      Camera =
        Camera3D.lookAt
          (Vector3(150f, 200f, 300f))
          (Vector3(128f, 0f, 128f))
          Vector3.Up
          MathHelper.PiOver4
          (16f / 9f)
          0.1f
          2000f
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
    let camera = state.Camera
    let speed = 10f * dt
    let currentPos = camera.View.Translation
    let mutable offset = Vector3.Zero

    if keyboard.IsKeyDown Keys.W then
      offset <- offset + Vector3.Forward * speed

    if keyboard.IsKeyDown Keys.S then
      offset <- offset + Vector3.Backward * speed

    if keyboard.IsKeyDown Keys.A then
      offset <- offset + Vector3.Left * speed

    if keyboard.IsKeyDown Keys.D then
      offset <- offset + Vector3.Right * speed

    if offset = Vector3.Zero then
      state
    else
      let newPos = currentPos + offset

      let newCamera =
        Camera3D.lookAt
          newPos
          (newPos + Vector3.Forward)
          Vector3.Up
          MathHelper.PiOver4
          (16f / 9f)
          0.1f
          1000f

      { state with Camera = newCamera }

  let update (gt: GameTime) (state: EditorState) : EditorState =
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    let keyboard = Keyboard.GetState()
    processCamera state dt keyboard

  let view
    (ctx: GameContext)
    (state: EditorState)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    Draw3D.viewport ctx.GraphicsDevice.Viewport buffer
    Draw3D.clear (ValueSome Color.CornflowerBlue) true buffer
    Draw3D.camera state.Camera buffer

    for block in state.BlockMap.Blocks do
      let cell = block.Key
      let blockData = block.Value

      match state.BlockMap.Palette.TryGetValue(blockData.BlockTypeId) with
      | true, blockType ->
        let model = ctx |> Assets.model blockType.Model

        let position =
          Vector3(
            float32 cell.X * BlockMap.CellSize,
            float32 cell.Y * BlockMap.CellSize,
            float32 cell.Z * BlockMap.CellSize
          )

        let scale =
          Matrix.CreateScale(
            BlockMap.CellSize / 32f * BlockMap.KayKitBlockModelScale
          )

        let rot =
          match blockData.Rotation with
          | ValueSome q -> Matrix.CreateFromQuaternion q
          | ValueNone -> Matrix.Identity

        let trans = Matrix.CreateTranslation(position)
        let world = scale * rot * trans

        Draw3D.mesh model world
        |> Draw3D.withBasicEffect
        |> Draw3D.submit buffer
      | _ -> ()

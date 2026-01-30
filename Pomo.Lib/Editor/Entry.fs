namespace Pomo.Lib.Editor

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Rendering
open Mibo.Rendering.Graphics3D
open Pomo.Lib
open Pomo.Lib.Services
open Pomo.Lib.Editor.Subsystems
open Pomo.Lib.Editor.Subsystems.BlockMap
open Pomo.Lib.Editor.Subsystems.Camera

[<Struct>]
type EditorModel = {
  BlockMap: BlockMapModel
  Camera: CameraModel
}

[<Struct>]
type EditorMsg =
  | BlockMapMsg of blockMap: BlockMapMsg
  | CameraMsg of camera: CameraMsg
  | Tick of gt: GameTime

module Entry =

  let init
    (env: #FileSystemCap & #AssetsCap & #BlockMapPersistenceCap)
    (ctx: obj)
    : struct (EditorModel * Cmd<EditorMsg>) =
    // env is available here for any initialization that needs services
    // For now, we just use the basic BlockMap initialization
    {
      BlockMap = BlockMap.init env BlockMapDefinition.empty
      Camera = Camera.init env
    },
    Cmd.none

  let update
    (env: #FileSystemCap & #AssetsCap & #BlockMapPersistenceCap)
    (msg: EditorMsg)
    (model: EditorModel)
    : struct (EditorModel * Cmd<EditorMsg>) =
    match msg with
    | BlockMapMsg subMsg ->
      let struct (subModel, cmd) = BlockMap.update env subMsg model.BlockMap
      { model with BlockMap = subModel }, cmd |> Cmd.map BlockMapMsg

    | CameraMsg subMsg ->
      let struct (subModel, cmd) = Camera.update env subMsg model.Camera
      { model with Camera = subModel }, cmd |> Cmd.map CameraMsg

    | Tick time -> model, Cmd.none

  let subscribe (ctx: obj) (_model: EditorModel) : Sub<EditorMsg> = Sub.none

  let view
    (env: #FileSystemCap & #AssetsCap & #BlockMapPersistenceCap)
    (ctx: obj)
    (model: EditorModel)
    (buffer: RenderBuffer<unit, RenderCommand>)
    : unit =
    // Set up camera and clear screen
    buffer.Camera(model.Camera.Camera).Clear(Color.CornflowerBlue) |> ignore

    // Delegate to subsystem views
    BlockMap.view ctx model.BlockMap buffer

    buffer.Submit()

namespace Pomo.Lib.Editor

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Rendering
open Mibo.Rendering.Graphics3D
open Pomo.Lib
open Pomo.Lib.Services
open Pomo.Lib.Editor.Subsystems
open Pomo.Lib.Editor.Subsystems.BlockMap

[<Struct>]
type EditorModel = { BlockMap: BlockMapModel }

[<Struct>]
type EditorMsg =
  | BlockMapMsg of blockMap: BlockMapMsg
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

    | Tick time -> model, Cmd.none

  let view
    (env: #FileSystemCap & #AssetsCap & #BlockMapPersistenceCap)
    (ctx: obj)
    (model: EditorModel)
    buffer
    : unit =
    // Placeholder view
    // TODO: Implement actual rendering logic using env for asset loading
    ()

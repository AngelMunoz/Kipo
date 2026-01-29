namespace Pomo.Lib

open Mibo.Elmish
open Pomo.Lib.Services

[<Struct>]
type AppEnv = {
  FileSystemService: FileSystem
  BlockMapPersistenceService: BlockMapPersistence
  AssetsService: IAssets
} with
  // Service capability interfaces - these allow generic constraint resolution
  interface FileSystemCap with
    member this.FileSystem = this.FileSystemService

  interface BlockMapPersistenceCap with
    member this.BlockMapPersistence = this.BlockMapPersistenceService

  // Assets capability - provides access to Mibo's IAssets
  interface AssetsCap with
    member this.Assets = this.AssetsService

module AppEnv =
  let create(ctx: GameContext) : AppEnv =
    let fileSystem = FileSystem.live
    let blockMapPersistence = BlockMapPersistence.live fileSystem
    let assets = Mibo.Elmish.Assets.getService ctx

    {
      FileSystemService = fileSystem
      BlockMapPersistenceService = blockMapPersistence
      AssetsService = assets
    }

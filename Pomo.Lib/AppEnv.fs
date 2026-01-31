namespace Pomo.Lib

open Mibo.Elmish
open Pomo.Lib.Services

[<Struct>]
type AppEnv = {
  FileSystemService: FileSystem
  BlockMapPersistenceService: BlockMapPersistence
  AssetsService: IAssets
  EditorCursorService: EditorCursorService
} with
  // Service capability interfaces - these allow generic constraint resolution
  interface FileSystemCap with
    member this.FileSystem = this.FileSystemService

  interface BlockMapPersistenceCap with
    member this.BlockMapPersistence = this.BlockMapPersistenceService

  // Assets capability - provides access to Mibo's IAssets
  interface AssetsCap with
    member this.Assets = this.AssetsService

  // Editor cursor capability
  interface EditorCursorCap with
    member this.EditorCursor = this.EditorCursorService

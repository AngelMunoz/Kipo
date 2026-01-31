namespace Pomo.Lib

open Mibo.Elmish
open Pomo.Lib.Services

module EnvFactory =
  let create(ctx: GameContext) : AppEnv =
    let fileSystem = FileSystem.live
    let blockMapPersistence = BlockMapPersistence.live fileSystem
    let assets = Mibo.Elmish.Assets.getService ctx
    let editorCursor = EditorCursor.live ctx.GraphicsDevice

    {
      FileSystemService = fileSystem
      BlockMapPersistenceService = blockMapPersistence
      AssetsService = assets
      EditorCursorService = editorCursor
    }

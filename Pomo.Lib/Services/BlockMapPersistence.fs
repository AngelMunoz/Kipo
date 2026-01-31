namespace Pomo.Lib.Services

open Pomo.Lib

[<Interface>]
type BlockMapPersistence =
  abstract Save:
    definition: BlockMapDefinition * path: string -> Async<Result<unit, Error>>

  abstract Load: path: string -> Async<Result<BlockMapDefinition, Error>>

[<Interface>]
type BlockMapPersistenceCap =
  abstract BlockMapPersistence: BlockMapPersistence

module BlockMapPersistence =
  let live
    (fileSystem: FileSystem)
    (serialization: Serialization)
    : BlockMapPersistence =
    { new BlockMapPersistence with
        member _.Save(definition, path) = async {
          try
            let json = serialization.Serialize definition
            return! fileSystem.WriteText(path, json)
          with ex ->
            return Error(IOException ex.Message)
        }

        member _.Load path = async {
          try
            let! result = fileSystem.ReadText path
            match result with
            | Ok json ->
              try
                let definition = serialization.Deserialize<BlockMapDefinition> json
                return Ok definition
              with ex ->
                return Error(DeserializationError ex.Message)
            | Error err -> return Error err
          with ex ->
            return Error(IOException ex.Message)
        }
    }

  let save (env: #BlockMapPersistenceCap) definition path =
    env.BlockMapPersistence.Save(definition, path)

  let load (env: #BlockMapPersistenceCap) path =
    env.BlockMapPersistence.Load path

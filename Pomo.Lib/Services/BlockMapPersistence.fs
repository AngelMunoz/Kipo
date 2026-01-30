namespace Pomo.Lib.Services

open Pomo.Lib
open System.Text.Json

[<Interface>]
type BlockMapPersistence =
  abstract Save:
    definition: BlockMapDefinition * path: string -> Async<Result<unit, Error>>

  abstract Load: path: string -> Async<Result<BlockMapDefinition, Error>>

[<Interface>]
type BlockMapPersistenceCap =
  abstract BlockMapPersistence: BlockMapPersistence

module BlockMapPersistence =
  let jsonOptions =
    let options = JsonSerializerOptions()
    options.WriteIndented <- true
    options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
    options

  let live(fileSystem: FileSystem) : BlockMapPersistence =
    { new BlockMapPersistence with
        member _.Save(definition, path) = async {
          try
            // TODO: Implement proper JSON serialization for BlockMapDefinition
            // For now, just create a placeholder JSON structure
            let json =
              sprintf
                """{"version":%d,"key":"%s"}"""
                definition.Version
                definition.Key

            return! fileSystem.WriteText(path, json)
          with ex ->
            return Error(IOException ex.Message)
        }

        member _.Load path = async {
          try
            let! result = fileSystem.ReadText path

            match result with
            | Ok _json ->
              // TODO: Implement proper JSON deserialization for BlockMapDefinition
              return Ok(BlockMapDefinition.empty)
            | Error err -> return Error err
          with ex ->
            return Error(IOException ex.Message)
        }
    }

  let save (env: #BlockMapPersistenceCap) definition path =
    env.BlockMapPersistence.Save(definition, path)

  let load (env: #BlockMapPersistenceCap) path =
    env.BlockMapPersistence.Load path

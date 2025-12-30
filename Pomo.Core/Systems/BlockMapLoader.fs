namespace Pomo.Core.Systems

open System.IO
open Pomo.Core.Domain
open Pomo.Core.Domain.BlockMap

module BlockMapLoader =

  /// Load a BlockMapDefinition from a file
  let load(filePath: string) : Result<BlockMapDefinition, string> =
    try
      if File.Exists filePath then
        let json = File.ReadAllText filePath

        match
          JDeck.Decoding.fromString(
            json,
            Serialization.blockMapDefinitionDecoder
          )
        with
        | Ok map -> Ok map
        | Error err -> Error $"Failed to decode block map: {err.message}"
      else
        Error $"Block map file not found: {filePath}"
    with ex ->
      Error $"Exception loading block map: {ex.Message}"

  /// Save a BlockMapDefinition to a file
  let save (filePath: string) (map: BlockMapDefinition) : Result<unit, string> =
    try
      let json = (Serialization.encodeBlockMapDefinition map).ToJsonString()
      File.WriteAllText(filePath, json)
      Ok()
    with ex ->
      Error $"Exception saving block map: {ex.Message}"

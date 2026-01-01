namespace Pomo.Core.Systems

open System
open System.IO
open Pomo.Core.Domain
open Pomo.Core.Domain.BlockMap

module BlockMapLoader =
  open System.Text.Json

  let private resolvePath(filePath: string) =
    if Path.IsPathRooted filePath then
      filePath
    else
      Path.Combine(AppContext.BaseDirectory, filePath)

  /// Load a BlockMapDefinition from a file
  let load(filePath: string) : Result<BlockMapDefinition, string> =
    try
      let resolvedPath = resolvePath filePath

      if File.Exists resolvedPath then
        let json = File.ReadAllText resolvedPath

        match
          JDeck.Decoding.fromString(
            json,
            Serialization.blockMapDefinitionDecoder
          )
        with
        | Ok map -> Ok map
        | Error err -> Error $"Failed to decode block map: {err.message}"
      else
        Error $"Block map file not found: {resolvedPath}"
    with ex ->
      Error $"Exception loading block map: {ex.Message}"

  /// Save a BlockMapDefinition to a file
  let save (filePath: string) (map: BlockMapDefinition) : Result<unit, string> =
    try
      let resolvedPath = resolvePath filePath

      let json =
        (Serialization.encodeBlockMapDefinition map)
          .ToJsonString(JsonSerializerOptions(WriteIndented = true))

      let dir = Path.GetDirectoryName(resolvedPath)

      if
        not(System.String.IsNullOrWhiteSpace dir) && not(Directory.Exists dir)
      then
        Directory.CreateDirectory(dir) |> ignore

      File.WriteAllText(resolvedPath, json)
      Ok()
    with ex ->
      Error $"Exception saving block map: {ex.Message}"

namespace Pomo.Core.Systems

open System
open System.IO
open Pomo.Core.Domain
open Pomo.Core.Domain.BlockMap

module BlockMapLoader =
  open System.Text.Json

  type PathResolver = string -> string

  module Resolvers =

    [<TailCall>]
    let rec private findSourcesRoot(currentDir: DirectoryInfo) =
      match currentDir.Parent with
      | null -> ValueNone
      | existing ->
        match existing.GetDirectories("Pomo.Core") with
        | [| found |] -> ValueSome found.FullName
        | _ -> findSourcesRoot existing

    let runtime: PathResolver =
      fun path ->
        if Path.IsPathRooted path then
          path
        else
          Path.Combine(AppContext.BaseDirectory, path)

    let editor: PathResolver =
      fun path ->
        if Path.IsPathRooted path then
          path
        else
          let cwd = DirectoryInfo(Directory.GetCurrentDirectory())
          let found = findSourcesRoot cwd

          match found with
          | ValueSome foundRoot -> Path.Combine(foundRoot, path)
          | ValueNone -> path


  /// Load a BlockMapDefinition from a file
  let load
    (resolve: PathResolver)
    (filePath: string)
    : Result<BlockMapDefinition, string> =
    try
      let resolvedPath = resolve filePath |> Path.GetFullPath
      printfn $"[BlockMapLoader] Resolved Path: {resolvedPath}"

      if File.Exists resolvedPath then
        let json = File.ReadAllText resolvedPath

        match
          JDeck.Decoding.fromString(
            json,
            Serialization.blockMapDefinitionDecoder
          )
        with
        | Ok map -> Ok(Pomo.Core.Algorithms.BlockMap.normalizeLoadedMap map)
        | Error err -> Error $"Failed to decode block map: {err.message}"
      else
        Error $"Block map file not found: {resolvedPath}"
    with ex ->
      Error $"Exception loading block map: {ex.Message}"

  /// Save a BlockMapDefinition to a file
  let save
    (resolve: PathResolver)
    (filePath: string)
    (map: BlockMapDefinition)
    : Result<unit, string> =
    try
      let resolvedPath = resolve filePath

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

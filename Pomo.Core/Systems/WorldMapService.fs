namespace Pomo.Core.Systems

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open FSharp.Data.Adaptive

module WorldMapService =

  type WorldMapEntry = {
    fileName: string
    x: int
    y: int
    width: int
    height: int
  }

  type WorldMap = {
    maps: WorldMapEntry list
    onlyShowAdjacentMaps: bool
    [<JsonPropertyName("type")>]
    type': string
  }

  type WorldMapService() =
    let mutable mapCache = HashMap.empty<string, string>

    member _.Initialize() =
      try
        let worldPath =
          Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "Maps",
            "Pomo.world"
          )

        if File.Exists(worldPath) then
          let json = File.ReadAllText(worldPath)

          let options =
            JsonSerializerOptions(PropertyNameCaseInsensitive = true)

          let world = JsonSerializer.Deserialize<WorldMap>(json, options)

          let maps =
            world.maps
            |> List.map(fun m ->
              let fileName = Path.GetFileNameWithoutExtension(m.fileName)
              // Normalize path separators and ensure relative path is correct from Content root
              // Pomo.world has paths like "../Pomo.Core/Content/Maps/Proto.xml"
              // We want "Content/Maps/Proto.xml"
              let fullPath =
                Path.GetFullPath(
                  Path.Combine(Path.GetDirectoryName(worldPath), m.fileName)
                )

              let relativePath =
                Path.GetRelativePath(AppContext.BaseDirectory, fullPath)

              fileName, relativePath)
            |> HashMap.ofList

          mapCache <- maps
      with ex ->
        printfn "Failed to load world map: %s" ex.Message

    member _.GetMapPath(key: string) =
      match mapCache |> HashMap.tryFind key with
      | Some path -> Some path
      | None -> None

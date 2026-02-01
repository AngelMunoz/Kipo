namespace Pomo.Lib.Services

open System
open System.IO

type PathResolver = string -> string

module PathResolvers =

  [<TailCall>]
  let rec private findSourcesRoot(currentDir: DirectoryInfo) =
    match currentDir.Parent with
    | null -> ValueNone
    | existing ->
      match existing.GetDirectories("Pomo.Lib") with
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

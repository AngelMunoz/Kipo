namespace Pomo.Lib.Services

open System
open System.IO
open System.Text
open Mibo.Elmish

type FsError =
  | FileNotFound of path: string
  | AccessDenied of path: string
  | IOException of message: string
  | DeserializationError of message: string

[<Interface>]
type FileSystem =
  abstract ReadText: path: string -> Async<Result<string, FsError>>

  abstract WriteText:
    path: string * content: string -> Async<Result<unit, FsError>>

  abstract CreateDirectory: path: string -> Result<unit, FsError>

[<Interface>]
type FileSystemCap =
  abstract FileSystem: FileSystem

// Assets capability - wraps Mibo's IAssets
[<Interface>]
type AssetsCap =
  abstract Assets: IAssets

module FileSystem =
  let live(resolve: PathResolver) : FileSystem =
    { new FileSystem with
        member _.ReadText path = async {
          try
            let resolvedPath = resolve path

            let! content =
              File.ReadAllTextAsync(resolvedPath) |> Async.AwaitTask

            return Ok content
          with
          | :? FileNotFoundException -> return Error(FileNotFound path)
          | :? UnauthorizedAccessException -> return Error(AccessDenied path)
          | ex -> return Error(IOException ex.Message)
        }

        member _.WriteText(path, content) = async {
          try
            let resolvedPath = resolve path

            do!
              File.WriteAllTextAsync(resolvedPath, content, Encoding.UTF8)
              |> Async.AwaitTask

            return Ok()
          with
          | :? UnauthorizedAccessException -> return Error(AccessDenied path)
          | :? DirectoryNotFoundException -> return Error(FileNotFound path)
          | ex -> return Error(IOException ex.Message)
        }

        member _.CreateDirectory path =
          try
            let resolvedPath = resolve path
            Directory.CreateDirectory resolvedPath |> ignore
            Ok()
          with
          | :? UnauthorizedAccessException -> Error(AccessDenied path)
          | ex -> Error(IOException ex.Message)
    }

  let readText (env: #FileSystemCap) path = env.FileSystem.ReadText path

  let writeText (env: #FileSystemCap) path content =
    env.FileSystem.WriteText(path, content)

  let createDirectory (env: #FileSystemCap) path =
    env.FileSystem.CreateDirectory path

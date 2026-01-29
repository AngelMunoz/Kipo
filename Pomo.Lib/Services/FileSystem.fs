namespace Pomo.Lib.Services

open System
open System.IO
open System.Text
open Mibo.Elmish

type Error =
  | FileNotFound of path: string
  | AccessDenied of path: string
  | IOException of message: string

[<Interface>]
type FileSystem =
  abstract ReadText: path: string -> Async<Result<string, Error>>

  abstract WriteText:
    path: string * content: string -> Async<Result<unit, Error>>

  abstract CreateDirectory: path: string -> Result<unit, Error>

[<Interface>]
type FileSystemCap =
  abstract FileSystem: FileSystem

// Assets capability - wraps Mibo's IAssets
[<Interface>]
type AssetsCap =
  abstract Assets: IAssets

module FileSystem =
  let live: FileSystem =
    { new FileSystem with
        member _.ReadText path = async {
          try
            let! content = File.ReadAllTextAsync(path) |> Async.AwaitTask
            return Ok content
          with
          | :? FileNotFoundException -> return Error(FileNotFound path)
          | :? UnauthorizedAccessException -> return Error(AccessDenied path)
          | ex -> return Error(IOException ex.Message)
        }

        member _.WriteText(path, content) = async {
          try
            do!
              File.WriteAllTextAsync(path, content, Encoding.UTF8)
              |> Async.AwaitTask

            return Ok()
          with
          | :? UnauthorizedAccessException -> return Error(AccessDenied path)
          | :? DirectoryNotFoundException -> return Error(FileNotFound path)
          | ex -> return Error(IOException ex.Message)
        }

        member _.CreateDirectory path =
          try
            Directory.CreateDirectory path |> ignore
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

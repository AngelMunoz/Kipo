namespace Pomo.Core.Domain

open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Spatial

module BlockMap =

  [<Struct>]
  type CollisionType =
    | Box
    | Mesh of path: string
    | NoCollision

  [<Struct>]
  type PlacedBlock = {
    Cell: GridCell3D
    BlockTypeId: int<BlockTypeId>
    Rotation: Quaternion voption
  }

  type BlockType = {
    Id: int<BlockTypeId>
    Name: string
    Model: string
    Category: string
    CollisionType: CollisionType
  }

  type BlockMapDefinition = {
    Version: int
    Key: string
    Width: int
    Height: int
    Depth: int
    Palette: Dictionary<int<BlockTypeId>, BlockType>
    Blocks: Dictionary<GridCell3D, PlacedBlock>
    SpawnCell: GridCell3D voption
  }

  let CellSize = 32.0f

  let inline createEmpty
    (key: string)
    (width: int)
    (height: int)
    (depth: int)
    : BlockMapDefinition =
    {
      Version = 1
      Key = key
      Width = width
      Height = height
      Depth = depth
      Palette = Dictionary()
      Blocks = Dictionary()
      SpawnCell = ValueNone
    }

  let inline cellToWorldPosition(cell: GridCell3D) : WorldPosition = {
    X = float32 cell.X * CellSize + CellSize / 2.0f
    Y = float32 cell.Y * CellSize + CellSize / 2.0f
    Z = float32 cell.Z * CellSize + CellSize / 2.0f
  }

  let inline worldPositionToCell(pos: WorldPosition) : GridCell3D = {
    X = int(pos.X / CellSize)
    Y = int(pos.Y / CellSize)
    Z = int(pos.Z / CellSize)
  }

  let inline tryGetBlock
    (map: BlockMapDefinition)
    (cell: GridCell3D)
    : PlacedBlock voption =
    map.Blocks |> Dictionary.tryFindV cell

  let inline isInBounds (map: BlockMapDefinition) (cell: GridCell3D) : bool =
    cell.X >= 0
    && cell.X < map.Width
    && cell.Y >= 0
    && cell.Y < map.Height
    && cell.Z >= 0
    && cell.Z < map.Depth

  let inline getBlockType
    (map: BlockMapDefinition)
    (block: PlacedBlock)
    : BlockType voption =
    map.Palette |> Dictionary.tryFindV block.BlockTypeId

  module Serialization =
    open System.Text.Json
    open System.Text.Json.Nodes
    open JDeck
    open JDeck.Decode
    open JDeck.Encoding

    let gridCell3DDecoder: Decoder<GridCell3D> =
      fun json -> decode {
        let! x = Required.Property.get ("X", Required.int) json
        and! y = Required.Property.get ("Y", Required.int) json
        and! z = Required.Property.get ("Z", Required.int) json
        return { X = x; Y = y; Z = z }
      }

    let gridCell3DEncoder: Encoder<GridCell3D> =
      fun value ->
        Json.empty()
        |> Encode.property("X", Encode.int value.X)
        |> Encode.property("Y", Encode.int value.Y)
        |> Encode.property("Z", Encode.int value.Z)
        :> JsonNode



    let quaternionDecoder: Decoder<Quaternion> =
      fun json -> decode {
        let! x = Required.Property.get ("X", Decode.Required.float32) json
        and! y = Required.Property.get ("Y", Decode.Required.float32) json
        and! z = Required.Property.get ("Z", Decode.Required.float32) json
        and! w = Required.Property.get ("W", Decode.Required.float32) json
        return Quaternion(x, y, z, w)
      }

    let quaternionEncoder: Encoder<Quaternion> =
      fun value ->
        Json.empty()
        |> Encode.property("X", Encode.float32 value.X)
        |> Encode.property("Y", Encode.float32 value.Y)
        |> Encode.property("Z", Encode.float32 value.Z)
        |> Encode.property("W", Encode.float32 value.W)
        :> JsonNode

    let collisionTypeDecoder: Decoder<CollisionType> =
      fun json -> decode {
        let! typeString = Required.Property.get ("Type", Required.string) json

        match typeString with
        | "Box" -> return Box
        | "NoCollision" -> return NoCollision
        | "Mesh" ->
          let! path = Required.Property.get ("Path", Required.string) json
          return Mesh path
        | _ ->
          return!
            Error(
              DecodeError.ofError(json, $"Unknown CollisionType: {typeString}")
            )
      }

    let collisionTypeEncoder: Encoder<CollisionType> =
      fun value ->
        let struct (typeString, path) =
          match value with
          | Box -> "Box", ValueNone
          | NoCollision -> "NoCollision", ValueNone
          | Mesh path -> "Mesh", ValueSome path

        Json.object [
          "Type", Encode.string typeString

          match path with
          | ValueSome path -> "Path", Encode.string path
          | ValueNone -> ()
        ]

    let blockTypeDecoder: Decoder<BlockType> =
      fun json -> decode {
        let! id = Required.Property.get ("Id", Required.int) json
        and! name = Required.Property.get ("Name", Required.string) json
        and! model = Required.Property.get ("Model", Required.string) json

        and! category =
          VOptional.Property.get ("Category", Required.string) json
          |> Result.map(ValueOption.defaultValue "Terrain")

        and! collisionType =
          VOptional.Property.get ("CollisionType", collisionTypeDecoder) json
          |> Result.map(ValueOption.defaultValue Box)

        return {
          Id = id * 1<BlockTypeId>
          Name = name
          Model = model
          Category = category
          CollisionType = collisionType
        }
      }

    let blockTypeEncoder: Encoder<BlockType> =
      fun value ->
        Json.object [
          "Id", Encode.int(value.Id |> UMX.untag)
          "Name", Encode.string value.Name
          "Model", Encode.string value.Model
          "Category", Encode.string value.Category
          "CollisionType", collisionTypeEncoder value.CollisionType
        ]

    let placedBlockDecoder: Decoder<PlacedBlock> =
      fun json -> decode {
        let! cell = Required.Property.get ("Cell", gridCell3DDecoder) json

        and! blockTypeId =
          Required.Property.get ("BlockTypeId", Required.int) json

        and! rotation =
          VOptional.Property.get ("Rotation", quaternionDecoder) json

        return {
          Cell = cell
          BlockTypeId = %blockTypeId
          Rotation = rotation
        }
      }

    let placedBlockEncoder: Encoder<PlacedBlock> =
      fun value ->
        Json.object [
          "Cell", gridCell3DEncoder value.Cell
          "BlockTypeId", Encode.int(value.BlockTypeId |> UMX.untag)
          match value.Rotation with
          | ValueSome rotation -> "Rotation", quaternionEncoder rotation
          | ValueNone -> ()
        ]

    let blockMapDefinitionDecoder: Decoder<BlockMapDefinition> =
      fun json -> decode {
        let! version =
          VOptional.Property.get ("Version", Required.int) json
          |> Result.map(ValueOption.defaultValue 1)

        and! key = Required.Property.get ("Key", Required.string) json
        and! width = Required.Property.get ("Width", Required.int) json
        and! height = Required.Property.get ("Height", Required.int) json
        and! depth = Required.Property.get ("Depth", Required.int) json

        and! palette =
          Required.Property.get
            ("Palette",
             fun j -> decode {
               let inline decoder key elem = blockTypeDecoder elem

               let! dict =
                 Decode.dict decoder j
                 |> Result.map(fun dict ->
                   let newd = Dictionary()

                   for KeyValue(key, value) in dict do
                     newd.Add(int key |> UMX.tag<BlockTypeId>, value)

                   newd)

               return dict
             })
            json


        and! blocks =
          VOptional.Property.get
            ("Blocks",
             fun j -> decode {
               let inline decoder _ (elem: JsonElement) =
                 placedBlockDecoder elem
                 |> Result.map(fun pb -> struct (pb.Cell, pb))

               let! arr = Decode.array decoder j |> Result.map Dictionary.ofSeqV

               return arr
             })
            json

        and! spawnCell =
          VOptional.Property.get ("SpawnCell", gridCell3DDecoder) json

        return {
          Version = version
          Key = key
          Width = width
          Height = height
          Depth = depth
          Palette = palette
          Blocks = defaultValueArg blocks (Dictionary())
          SpawnCell = spawnCell
        }
      }

    let encodeBlockMapDefinition(map: BlockMapDefinition) : JsonNode =
      let paletteEncoder = fun (key, value) -> $"{key}", blockTypeEncoder value

      let blocks = seq { for kv in map.Blocks -> kv.Value }

      Json.object [
        "Version", Encode.int map.Version
        "Key", Encode.string map.Key
        "Width", Encode.int map.Width
        "Height", Encode.int map.Height
        "Depth", Encode.int map.Depth
        "Palette", Encode.map(map.Palette, paletteEncoder)
        "Blocks", Json.sequence(blocks, placedBlockEncoder)
        match map.SpawnCell with
        | ValueSome cell -> "SpawnCell", gridCell3DEncoder cell
        | ValueNone -> ()
      ]

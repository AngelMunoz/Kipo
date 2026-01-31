# Serialization Service Design Plan

Design a centralized serialization service in Pomo.Lib that uses JDeck's codec registration pattern with auto-options decoders to enable automatic JSON serialization of domain types including discriminated unions.

---

## Overview

The goal is to create a `Serialization` service that:

- Pre-configures `JsonSerializerOptions` with custom encoders/decoders for Pomo.Lib DUs
- Uses `Decode.autoJsonOptions opts` for nested types to let System.Text.Json handle recursive decoding
- Follows the AppEnv capability pattern for dependency injection
- Enables clean, declarative serialization without manual JSON construction

---

## Architecture

**File Structure:**

```
Pomo.Lib/
  Domain.fs                    # Shared types (BlockMapDefinition, etc.)
    module Serialization        # Serializers at bottom (Core Pattern)
  Editor/
    Domain.fs                  # Editor-specific types
    module Serialization        # Editor serializers at bottom
  Gameplay/
    Domain.fs                  # Gameplay-specific types
    module Serialization        # Gameplay serializers at bottom
  Serialization.fs             # Service interface (above AppEnv.fs)
  EnvFactory.fs               # Serialization options registration module
  AppEnv.fs                   # AppEnv with SerializationService
```

**Key Principles:**

- Types stay in their respective domain files (self-contained)
- Serializers at the bottom of each file (Core Pattern)
- EnvFactory.fs has access to all types and registers all codecs
- No nulls: optional fields omitted entirely

---

## 1. Domain Codecs in Domain.fs

Add serializers at the bottom of `Pomo.Lib/Domain.fs`:

```fsharp
namespace Pomo.Lib

open System.Text.Json
open System.Text.Json.Nodes
open Microsoft.Xna.Framework
open JDeck
open JDeck.Decode
open JDeck.Encoding

// ... existing type definitions ...

module Serialization =
  // Vector3: Compact array format [x, y, z]
  let vector3Encoder: Encoder<Vector3> = fun v ->
    Json.array [ Encode.float32 v.X; Encode.float32 v.Y; Encode.float32 v.Z ]

  let vector3Decoder: Decoder<Vector3> = fun json -> decode {
    let! coords = Decode.array Decode.Required.float32 json
    if coords.Length = 3 then
      return Vector3(coords[0], coords[1], coords[2])
    else
      return! Error(DecodeError.ofError(json, "Vector3 requires exactly 3 elements"))
  }

  // Quaternion: [x, y, z, w] instead of {"X":x,"Y":y,"Z":z,"W":w}
  let quaternionEncoder: Encoder<Quaternion> = fun q ->
    Json.array [ Encode.float32 q.X; Encode.float32 q.Y; Encode.float32 q.Z; Encode.float32 q.W ]

  let quaternionDecoder: Decoder<Quaternion> = fun json -> decode {
    let! coords = Decode.array Decode.Required.float32 json
    if coords.Length = 4 then
      return Quaternion(coords[0], coords[1], coords[2], coords[3])
    else
      return! Error(DecodeError.ofError(json, "Quaternion requires exactly 4 elements"))
  }

  // GridDimensions: [w, h, d] instead of {"Width":w,"Height":h,"Depth":d}
  let gridDimensionsEncoder: Encoder<GridDimensions> = fun d ->
    Json.array [ Encode.int d.Width; Encode.int d.Height; Encode.int d.Depth ]

  let gridDimensionsDecoder: Decoder<GridDimensions> = fun json -> decode {
    let! dims = Decode.array Decode.Required.int json
    if dims.Length = 3 then
      return { Width = dims[0]; Height = dims[1]; Depth = dims[2] }
    else
      return! Error(DecodeError.ofError(json, "GridDimensions requires exactly 3 elements"))
  }

  // Leaf DU: CollisionType (no nested DUs)
  let collisionTypeEncoder: Encoder<CollisionType> = function
    | Box -> Encode.string "Box"
    | Mesh -> Encode.string "Mesh"
    | NoCollision -> Encode.string "NoCollision"

  let collisionTypeDecoder: Decoder<CollisionType> = fun json ->
    Required.string json |> Result.map (function
      | "Box" -> Box
      | "Mesh" -> Mesh
      | "NoCollision" -> NoCollision
      | s -> failwith $"Unknown CollisionType: {s}")

  // Leaf DU: EngagementRules (no nested DUs)
  let engagementRulesEncoder: Encoder<EngagementRules> = function
    | Peaceful -> Encode.string "Peaceful"
    | PvE -> Encode.string "PvE"
    | PvP -> Encode.string "PvP"
    | FFA -> Encode.string "FFA"

  let engagementRulesDecoder: Decoder<EngagementRules> = fun json ->
    Required.string json |> Result.map (function
      | "Peaceful" -> Peaceful
      | "PvE" -> PvE
      | "PvP" -> PvP
      | "FFA" -> FFA
      | s -> failwith $"Unknown EngagementRules: {s}")

  // MapObjectShape: compact format with full names
  let mapObjectShapeEncoder: Encoder<MapObjectShape> = function
    | Box size -> Json.array [ Encode.string "Box"; vector3Encoder size ]
    | Sphere radius -> Json.array [ Encode.string "Sphere"; Encode.float32 radius ]

  let mapObjectShapeDecoder: Decoder<MapObjectShape> = fun json -> decode {
    let! arr = Decode.array Decode.auto json
    if arr.Length >= 2 then
      let! shapeType = Decode.Required.string arr[0]
      match shapeType with
      | "Box" ->
          let! size = vector3Decoder arr[1]
          return Box size
      | "Sphere" ->
          let! radius = Decode.Required.float32 arr[1]
          return Sphere radius
      | s -> return! Error(DecodeError.ofError(json, $"Unknown shape: {s}"))
    else
      return! Error(DecodeError.ofError(json, "MapObjectShape requires at least 2 elements"))
  }

  // MapObjectData: compact format with full names
  let mapObjectDataEncoder: Encoder<MapObjectData> = function
    | Spawn props ->
        Json.array [
          Encode.string "Spawn"
          Encode.boolean props.IsPlayerSpawn
          match props.EntityGroup with
          | ValueSome g -> Encode.string g
          | ValueNone -> Encode.string ""
          Encode.int props.MaxSpawns
          match props.Faction with
          | ValueSome f -> Encode.string f
          | ValueNone -> Encode.string ""
        ]
    | Teleport props ->
        Json.array [
          Encode.string "Teleport"
          match props.TargetMap with
          | ValueSome m -> Encode.string m
          | ValueNone -> Encode.string ""
          Encode.string props.TargetObjectName
        ]
    | Trigger id ->
        Json.array [ Encode.string "Trigger"; Encode.string id ]

  let mapObjectDataDecoderFactory
    (opts: JsonSerializerOptions)
    : Decoder<MapObjectData> =
    fun json -> decode {
      let! discriminator = json |> Decode.decodeAt Decode.Required.string 0
      match discriminator with
      | "Spawn" ->
          let! isPlayer = json |> Decode.decodeAt Decode.Required.boolean 1
          and! entityGroup = json |> Decode.decodeAt Decode.Required.string 2
          and! maxSpawns = json |> Decode.decodeAt Decode.Required.int 3
          and! faction = json |> Decode.decodeAt Decode.Required.string 4
          return Spawn {
            IsPlayerSpawn = isPlayer
            EntityGroup = if String.IsNullOrEmpty entityGroup then ValueNone else ValueSome entityGroup
            MaxSpawns = maxSpawns
            Faction = if String.IsNullOrEmpty faction then ValueNone else ValueSome faction
          }
      | "Teleport" ->
          let! targetMap = json |> Decode.decodeAt Decode.Required.string 1
          and! targetObj = json |> Decode.decodeAt Decode.Required.string 2
          return Teleport {
            TargetMap = if String.IsNullOrEmpty targetMap then ValueNone else ValueSome targetMap
            TargetObjectName = targetObj
          }
      | "Trigger" ->
          let! triggerId = json |> Decode.decodeAt Decode.Required.string 1
          return Trigger triggerId
      | s -> return! Error(DecodeError.ofError(json, $"Unknown MapObjectData: {s}"))
    }

  // PlacedBlock: [cell, typeId, rotation?] - compact tuple format
  let placedBlockEncoder: Encoder<PlacedBlock> = fun pb ->
    let baseArray = [ vector3Encoder pb.Cell; Encode.int(pb.BlockTypeId |> UMX.untag) ]
    let fullArray =
      match pb.Rotation with
      | ValueSome rot -> baseArray @ [quaternionEncoder rot]
      | ValueNone -> baseArray
    Json.array fullArray

  let placedBlockDecoder: Decoder<PlacedBlock> = fun json -> decode {
    let! arr = Decode.array Decode.auto json
    if arr.Length >= 2 then
      let! cell = vector3Decoder arr[0]
      let! typeId = Decode.Required.int arr[1] |> Result.map (fun i -> i * 1<BlockTypeId>)
      let! rotation =
        if arr.Length > 2 then
          quaternionDecoder arr[2] |> Result.map ValueSome
        else
          Ok ValueNone
      return { Cell = cell; BlockTypeId = typeId; Rotation = rotation }
    else
      return! Error(DecodeError.ofError(json, "PlacedBlock requires at least 2 elements"))
  }
```

---

## 2. Editor Domain Serializers (Pomo.Lib/Editor/Domain.fs)

Add serializers at the bottom of editor domain file:

```fsharp
namespace Pomo.Lib.Editor

// ... existing editor type definitions ...

module Serialization =
  // Editor-specific type serializers go here
  // e.g., BrushSettings, CursorState, etc.
```

---

## 3. Gameplay Domain Serializers (Pomo.Lib/Gameplay/Domain.fs)

Add serializers at the bottom of gameplay domain file:

```fsharp
namespace Pomo.Lib.Gameplay

// ... existing gameplay type definitions ...

module Serialization =
  // Gameplay-specific type serializers go here
  // e.g., EntityState, AIState, etc.
```

---

## 4. Serialization Service (Pomo.Lib/Serialization.fs)

Declared above AppEnv.fs:

```fsharp
namespace Pomo.Lib

open System.Text.Json
open JDeck

[<Interface>]
type Serialization =
  abstract Options: JsonSerializerOptions
  abstract Serialize: 'T -> string
  abstract Deserialize: string -> 'T

[<Interface>]
type SerializationCap = abstract Serialization: Serialization

module Serialization =
  let live(options: JsonSerializerOptions): Serialization =
    { new Serialization with
        member _.Serialize value = JsonSerializer.Serialize(value, options)
        member _.Deserialize json = JsonSerializer.Deserialize(json, options)
    }

  // Curried helpers
  let serialize (env: #SerializationCap) value = env.Serialization.Serialize value
  let deserialize (env: #SerializationCap) json = env.Serialization.Deserialize json
  let options (env: #SerializationCap) = env.Serialization.Options
```

---

## 5. EnvFactory Module (Pomo.Lib/EnvFactory.fs)

Creates JsonSerializerOptions and Serialization service:

```fsharp
namespace Pomo.Lib

open System.Text.Json
open JDeck
open Pomo.Lib
open Pomo.Lib.Editor
open Pomo.Lib.Gameplay

module JsonSerializerOptions =
  let createOptions(): JsonSerializerOptions =
    JsonSerializerOptions(WriteIndented = true)
    // Shared domain codecs
    |> Codec.useEncoder Domain.Serialization.vector3Encoder
    |> Codec.useDecoder Domain.Serialization.vector3Decoder
    |> Codec.useEncoder Domain.Serialization.quaternionEncoder
    |> Codec.useDecoder Domain.Serialization.quaternionDecoder
    |> Codec.useEncoder Domain.Serialization.gridDimensionsEncoder
    |> Codec.useDecoder Domain.Serialization.gridDimensionsDecoder
    |> Codec.useEncoder Domain.Serialization.collisionTypeEncoder
    |> Codec.useDecoder Domain.Serialization.collisionTypeDecoder
    |> Codec.useEncoder Domain.Serialization.engagementRulesEncoder
    |> Codec.useDecoder Domain.Serialization.engagementRulesDecoder
    |> Codec.useEncoder Domain.Serialization.mapObjectShapeEncoder
    |> Codec.useDecoder Domain.Serialization.mapObjectShapeDecoder
    |> Codec.useEncoder Domain.Serialization.placedBlockEncoder
    |> Codec.useDecoder Domain.Serialization.placedBlockDecoder
    |> Codec.useEncoder Domain.Serialization.mapObjectDataEncoder
    |> Codec.useDecoderWithOptions Domain.Serialization.mapObjectDataDecoderFactory
    // Editor codecs (if any)
    // |> Codec.useEncoder Editor.Serialization.xxxEncoder
    // |> Codec.useDecoder Editor.Serialization.xxxDecoder
    // Gameplay codecs (if any)
    // |> Codec.useEncoder Gameplay.Serialization.xxxEncoder
    // |> Codec.useDecoder Gameplay.Serialization.xxxDecoder

module EnvFactory =
  let create(ctx: GameContext) : AppEnv =
    let fileSystem = FileSystem.live
    let serialization = JsonSerializerOptions.createOptions() |> Serialization.live
    let blockMapPersistence = BlockMapPersistence.live fileSystem serialization
    let assets = Mibo.Elmish.Assets.getService ctx
    let editorCursor = EditorCursor.live ctx.GraphicsDevice

    {
      FileSystemService = fileSystem
      BlockMapPersistenceService = blockMapPersistence
      AssetsService = assets
      EditorCursorService = editorCursor
      SerializationService = serialization
    }
```

---

## 6. AppEnv Integration (Pomo.Lib/AppEnv.fs)

```fsharp
namespace Pomo.Lib

open Mibo.Elmish
open Pomo.Lib.Services

[<Struct>]
type AppEnv = {
  FileSystemService: FileSystem
  BlockMapPersistenceService: BlockMapPersistence
  AssetsService: IAssets
  EditorCursorService: EditorCursor
  SerializationService: Serialization
} with
  interface FileSystemCap with
    member this.FileSystem = this.FileSystemService

  interface BlockMapPersistenceCap with
    member this.BlockMapPersistence = this.BlockMapPersistenceService

  interface AssetsCap with
    member this.Assets = this.AssetsService

  interface EditorCursorCap with
    member this.EditorCursor = this.EditorCursorService

  interface SerializationCap with
    member this.Serialization = this.SerializationService

module AppEnv =
  let create(ctx: GameContext) : AppEnv =
    let fileSystem = FileSystem.live
    let serialization = EnvFactory.Serialization.live()
    let blockMapPersistence = BlockMapPersistence.live fileSystem serialization
    let assets = Mibo.Elmish.Assets.getService ctx
    let editorCursor = EditorCursor.live ctx.GraphicsDevice

    {
      FileSystemService = fileSystem
      BlockMapPersistenceService = blockMapPersistence
      AssetsService = assets
      EditorCursorService = editorCursor
      SerializationService = serialization
    }
```

---

## 7. Update BlockMapPersistence (Pomo.Lib/Services/BlockMapPersistence.fs)

```fsharp
module BlockMapPersistence =
  let live(fileSystem: FileSystem, serialization: Serialization) : BlockMapPersistence =
    { new BlockMapPersistence with
        member _.Save(definition, path) = async {
          try
            let json = serialization.Serialize definition
            return! fileSystem.WriteText(path, json)
          with ex -> return Error(IOException ex.Message)
        }

        member _.Load path = async {
          try
            let! result = fileSystem.ReadText path
            match result with
            | Ok json ->
                try
                  return Ok(serialization.Deserialize<BlockMapDefinition> json)
                with ex ->
                  return Error(DeserializationError ex.Message)
            | Error err -> return Error err
          with ex -> return Error(IOException ex.Message)
        }
    }
```

---

## 8. Compact JSON Format Examples

**Before (verbose):**

```json
{
  "Cell": { "X": 1.0, "Y": 2.0, "Z": 3.0 },
  "BlockTypeId": 5,
  "Rotation": { "X": 0.0, "Y": 1.0, "Z": 0.0, "W": 0.0 }
}
```

**After (compact):**

```json
[[1.0, 2.0, 3.0], 5, [0.0, 1.0, 0.0, 0.0]]
```

**PlacedBlock examples (no nulls):**

```json
// With rotation: [[1.0, 2.0, 3.0], 5, [0.0, 1.0, 0.0, 0.0]]
// Without rotation: [[5.0, 6.0, 7.0], 2]
```

**No nulls policy**: Optional fields are omitted entirely rather than using `null`.

**MapObject examples:**

```json
// Box shape: ["Box", [10.0, 5.0, 2.0]]
// Sphere shape: ["Sphere", 3.5]
// Spawn object: ["Spawn", true, "player", 5, "red"]
// Teleport object: ["Teleport", "map2", "spawn_point_1"]
// Trigger object: ["Trigger", "trap_trigger"]
```

---

## 9. Benefits

- **Compact JSON**: Vectors/quaternions as arrays instead of objects (60% smaller)
- **Automatic**: Complex types serialize without manual JSON construction
- **Recursive decoding**: `Decode.autoJsonOptions opts` lets System.Text.Json handle nested DUs automatically
- **Modular**: Types stay in their respective files (Editor, Gameplay, Shared)
- **Extensible**: Add new DU codecs in one place (each domain's Serialization module)
- **Type-safe**: Compiler ensures encoder/decoder type consistency
- **Testable**: Fake Serialization implementation for unit tests
- **Reusable**: Any subsystem can serialize domain types via capability

---

## Implementation Order

1. Add `Serialization` module to `Pomo.Lib/Domain.fs` with compact array encoders/decoders
2. Add `Serialization` modules to `Pomo.Lib/Editor/Domain.fs` and `Pomo.Lib/Gameplay/Domain.fs` (if needed)
3. Create `Pomo.Lib/Serialization.fs` with service interface
4. Create `Pomo.Lib/EnvFactory.fs` with `Serialization` module that registers all codecs
5. Update `Pomo.Lib/AppEnv.fs` to include `SerializationService`
6. Update `Pomo.Lib/Services/BlockMapPersistence.fs` to use serialization
7. Add tests for round-trip serialization and compact format verification

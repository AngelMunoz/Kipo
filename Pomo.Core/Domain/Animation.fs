namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive

module Animation =

  [<Struct>]
  type RigNode = {
    ModelAsset: string
    Parent: string voption
    Offset: Vector3
    Pivot: Vector3
  }

  [<Struct>]
  type Keyframe = {
    Time: TimeSpan
    Rotation: Quaternion
  // Position/Scale can be added here later
  }

  [<Struct>]
  type Track = {
    NodeName: string
    Keyframes: Keyframe[]
  }

  [<Struct>]
  type AnimationClip = {
    Name: string
    Duration: TimeSpan
    IsLooping: bool
    Tracks: Track[]
  }

  [<Struct>]
  type AnimationState = {
    ClipId: string
    Time: TimeSpan
    Speed: float32
  }

  [<Struct>]
  type AnimationLayer =
    | Base
    | Override
    | Additive

  module Serialization =
    open JDeck
    open JDeck.Decode
    open System.Text.Json

    module RigNode =
      let decoder: Decoder<RigNode> =
        fun json -> decode {
          let! model = Required.Property.get ("Model", Required.string) json

          and! parent = VOptional.Property.get ("Parent", Required.string) json

          and! offset =
            VOptional.Property.get ("Offset", Required.map Required.float) json

          and! pivot =
            VOptional.Property.get ("Pivot", Required.map Required.float) json

          let offsetVector =
            match offset with
            | ValueSome map ->
              let x = map |> Map.tryFind "X" |> Option.defaultValue 0.0
              let y = map |> Map.tryFind "Y" |> Option.defaultValue 0.0
              let z = map |> Map.tryFind "Z" |> Option.defaultValue 0.0
              Vector3(float32 x, float32 y, float32 z)
            | ValueNone -> Vector3.Zero

          let pivotVector =
            match pivot with
            | ValueSome map ->
              let x = map |> Map.tryFind "X" |> Option.defaultValue 0.0
              let y = map |> Map.tryFind "Y" |> Option.defaultValue 0.0
              let z = map |> Map.tryFind "Z" |> Option.defaultValue 0.0
              Vector3(float32 x, float32 y, float32 z)
            | ValueNone -> Vector3.Zero

          return {
            ModelAsset = model
            Parent = parent
            Offset = offsetVector
            Pivot = pivotVector
          }
        }

    module Rig =
      let decoder: Decoder<HashMap<string, RigNode>> =
        fun json -> decode {
          let! map = Required.map RigNode.decoder json
          return map |> HashMap.ofMap
        }

    module Keyframe =
      let private degToRad(deg: float) = float32(Math.PI * deg / 180.0)

      let decoder: Decoder<Keyframe> =
        fun json -> decode {
          let! timeSeconds = Required.Property.get ("Time", Required.float) json

          and! rot =
            Required.Property.get ("Rotation", Required.map Required.float) json

          let x = rot |> Map.tryFind "X" |> Option.defaultValue 0.0 |> degToRad

          let y = rot |> Map.tryFind "Y" |> Option.defaultValue 0.0 |> degToRad

          let z = rot |> Map.tryFind "Z" |> Option.defaultValue 0.0 |> degToRad

          let rotation = Quaternion.CreateFromYawPitchRoll(y, x, z)

          return {
            Time = TimeSpan.FromSeconds timeSeconds
            Rotation = rotation
          }
        }

    module Track =
      // JSON: "Tracks": { "Arm_L": [Keyframe, Keyframe] }
      // The key is the NodeName. The value is the array of keyframes.
      // We deserialize the map and then convert to Track[].
      let decoderMap: Decoder<Track[]> =
        fun json -> decode {

          let inline decoder index json =
            Keyframe.decoder json
            |> Result.mapError(fun e ->
              DecodeError.ofIndexed(
                json.Clone(),
                index,
                $"Keyframe decode error at index {index}: {e.message}"
              ))

          let! map = Required.map (Decode.array decoder) json

          return
            map
            |> Map.toArray
            |> Array.map(fun (nodeName, keyframes) -> {
              NodeName = nodeName
              Keyframes = keyframes
            })
        }

    module AnimationClip =
      let decoder: Decoder<AnimationClip> =
        fun json -> decode {
          // The Name is typically the key in the outer map, so we might set it later
          // or just rely on ID lookup.
          let! durationSeconds =
            Required.Property.get ("Duration", Required.float) json

          and! isLooping =
            VOptional.Property.get ("Loop", Required.boolean) json
            |> Result.map(ValueOption.defaultValue false)

          and! tracks = Required.Property.get ("Tracks", Track.decoderMap) json

          return {
            Name = "" // Will be set by store
            Duration = TimeSpan.FromSeconds durationSeconds
            IsLooping = isLooping
            Tracks = tracks
          }
        }

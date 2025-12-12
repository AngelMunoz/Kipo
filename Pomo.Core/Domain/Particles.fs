namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open Pomo.Core.Domain.Units

module Particles =

  [<Struct>]
  type BlendMode =
    | Additive
    | AlphaBlend

  [<Struct>]
  type EmitterShape =
    | Point
    | Sphere of radius: float32
    | Cone of angle: float32 * radius: float32

  [<Struct>]
  type SimulationSpace =
    | World
    | Local

  [<Struct>]
  type ParticleConfig = {
    Lifetime: struct (float32 * float32)
    Speed: struct (float32 * float32)
    SizeStart: float32
    SizeEnd: float32
    ColorStart: Color
    ColorEnd: Color
    Gravity: float32
    RandomVelocity: Vector3
  }

  type EmitterConfig = {
    Name: string
    Texture: string
    BlendMode: BlendMode
    SimulationSpace: SimulationSpace
    InheritVelocity: float32
    Rate: int
    Burst: int
    Shape: EmitterShape
    LocalOffset: Vector3
    Particle: ParticleConfig
  }

  // Runtime Types

  [<Struct>]
  type Particle = {
    Position: Vector3
    Velocity: Vector3
    Size: float32
    Color: Color
    Life: float32
    MaxLife: float32
  }

  module Particle =
    let inline withPosition (value: Vector3) (p: Particle) = {
      p with
          Position = value
    }

    let inline withVelocity (value: Vector3) (p: Particle) = {
      p with
          Velocity = value
    }

    let inline withSize (value: float32) (p: Particle) = { p with Size = value }
    let inline withColor (value: Color) (p: Particle) = { p with Color = value }
    let inline withLife (value: float32) (p: Particle) = { p with Life = value }

  type ActiveEmitter = {
    Config: EmitterConfig
    Particles: ResizeArray<Particle>
    Accumulator: float32 ref
    BurstDone: bool ref
  }

  type ActiveEffect = {
    Id: string
    Emitters: ActiveEmitter list
    Position: Vector3 ref
    Rotation: Quaternion ref
    Scale: Vector3 ref
    IsAlive: bool ref
    Owner: Guid<EntityId> voption
  }

  module Serialization =
    open JDeck
    open JDeck.Decode
    open System.Globalization

    module BlendModeCodec =
      let decoder: Decoder<BlendMode> =
        fun json -> decode {
          let! str = Required.string json

          match str.ToLowerInvariant() with
          | "additive" -> return Additive
          | "alphablend" -> return AlphaBlend
          | _ ->
            return!
              DecodeError.ofError(json.Clone(), $"Unknown BlendMode: {str}")
              |> Error
        }

    module SimulationSpaceCodec =
      let decoder: Decoder<SimulationSpace> =
        fun json -> decode {
          let! str = Required.string json

          match str.ToLowerInvariant() with
          | "world" -> return World
          | "local" -> return Local
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown SimulationSpace: {str}"
              )
              |> Error
        }

    module Helper =
      let vec3FromDict: Decoder<Vector3> =
        fun json -> decode {
          let! x = VOptional.Property.get ("X", Required.float) json
          let! y = VOptional.Property.get ("Y", Required.float) json
          let! z = VOptional.Property.get ("Z", Required.float) json

          let xVal =
            match x with
            | ValueSome v -> float32 v
            | ValueNone -> 0.0f

          let yVal =
            match y with
            | ValueSome v -> float32 v
            | ValueNone -> 0.0f

          let zVal =
            match z with
            | ValueSome v -> float32 v
            | ValueNone -> 0.0f

          return Vector3(xVal, yVal, zVal)
        }

      let colorFromHex: Decoder<Color> =
        fun json -> decode {
          let! hex = Required.string json

          let parseHex(s: string) =
            if s.Length = 9 && s.StartsWith("#") then
              let r = Byte.Parse(s.Substring(1, 2), NumberStyles.HexNumber)
              let g = Byte.Parse(s.Substring(3, 2), NumberStyles.HexNumber)
              let b = Byte.Parse(s.Substring(5, 2), NumberStyles.HexNumber)
              let a = Byte.Parse(s.Substring(7, 2), NumberStyles.HexNumber)
              Ok(Color(int r, int g, int b, int a))
            elif s.Length = 7 && s.StartsWith("#") then
              let r = Byte.Parse(s.Substring(1, 2), NumberStyles.HexNumber)
              let g = Byte.Parse(s.Substring(3, 2), NumberStyles.HexNumber)
              let b = Byte.Parse(s.Substring(5, 2), NumberStyles.HexNumber)
              Ok(Color(int r, int g, int b))
            else
              Error $"Invalid Hex Color: {s}"

          match parseHex hex with
          | Ok c -> return c
          | Error e -> return! DecodeError.ofError(json.Clone(), e) |> Error
        }

    module EmitterShapeCodec =
      let decoder: Decoder<EmitterShape> =
        fun json -> decode {
          // If shape is just a string "Point"
          match Required.string json with
          | Ok "Point" -> return Point
          | _ ->
            // If shape is a property in the parent object, we handle it in EmitterConfig.
            // But if we want a decoder for just EmitterShape, it depends on structure.
            // Given the JSON structure "Shape": "Sphere", "Radius": 1.0 (mixed keys),
            // this usually implies the parent decoder handles the discrimination.
            // But here let's try to decode "Shape" property.
            let! type' = Required.Property.get ("Shape", Required.string) json

            match type'.ToLowerInvariant() with
            | "point" -> return Point
            | "sphere" ->
              let! radius =
                Required.Property.get ("Radius", Required.float) json

              return Sphere(float32 radius)
            | "cone" ->
              let! angle = Required.Property.get ("Angle", Required.float) json

              let! radius =
                VOptional.Property.get ("Radius", Required.float) json

              let r =
                match radius with
                | ValueSome v -> float32 v
                | ValueNone -> 0.0f

              return Cone(float32 angle, r)
            | _ ->
              return!
                DecodeError.ofError(json.Clone(), $"Unknown Shape: {type'}")
                |> Error
        }

    module ParticleConfigCodec =
      let rangeDecoder: Decoder<struct (float32 * float32)> =
        fun json -> decode {
          let! arr = Decode.array (fun _ json -> Required.float json) json

          if arr.Length >= 2 then
            return struct (float32 arr[0], float32 arr[1])
          else
            return struct (0.0f, 0.0f)
        }

      let decoder: Decoder<ParticleConfig> =
        fun json -> decode {
          let! lifetime = Required.Property.get ("Lifetime", rangeDecoder) json
          let! speed = Required.Property.get ("Speed", rangeDecoder) json

          let! sizeStart =
            Required.Property.get ("SizeStart", Required.float) json

          let! sizeEnd = Required.Property.get ("SizeEnd", Required.float) json

          let! colorStart =
            Required.Property.get ("ColorStart", Helper.colorFromHex) json

          let! colorEnd =
            Required.Property.get ("ColorEnd", Helper.colorFromHex) json

          let! gravity = VOptional.Property.get ("Gravity", Required.float) json

          let! randomVelocity =
            VOptional.Property.get ("RandomVelocity", Helper.vec3FromDict) json

          return {
            Lifetime = lifetime
            Speed = speed
            SizeStart = float32 sizeStart
            SizeEnd = float32 sizeEnd
            ColorStart = colorStart
            ColorEnd = colorEnd
            Gravity =
              match gravity with
              | ValueSome v -> float32 v
              | ValueNone -> 0.0f
            RandomVelocity =
              match randomVelocity with
              | ValueSome v -> v
              | ValueNone -> Vector3.Zero
          }
        }

    module EmitterConfigCodec =

      let decoder: Decoder<EmitterConfig> =
        fun json -> decode {
          let! name = Required.Property.get ("Name", Required.string) json
          let! texture = Required.Property.get ("Texture", Required.string) json

          let! blendMode =
            Required.Property.get ("BlendMode", BlendModeCodec.decoder) json

          let! simulationSpace =
            VOptional.Property.get
              ("SimulationSpace", SimulationSpaceCodec.decoder)
              json

          let! inheritVelocity =
            VOptional.Property.get ("InheritVelocity", Required.float) json

          let! rate = Required.Property.get ("Rate", Required.int) json
          let! burst = VOptional.Property.get ("Burst", Required.int) json

          // Decode shape from the SAME json object
          let! shape = EmitterShapeCodec.decoder json

          let! localOffset =
            VOptional.Property.get ("LocalOffset", Helper.vec3FromDict) json

          let! particle =
            Required.Property.get ("Particle", ParticleConfigCodec.decoder) json

          return {
            Name = name
            Texture = texture
            BlendMode = blendMode
            SimulationSpace =
              match simulationSpace with
              | ValueSome s -> s
              | ValueNone -> World
            InheritVelocity =
              match inheritVelocity with
              | ValueSome v -> float32 v
              | ValueNone -> 0.0f
            Rate = rate
            Burst =
              match burst with
              | ValueSome v -> v
              | ValueNone -> 0
            Shape = shape
            LocalOffset =
              match localOffset with
              | ValueSome v -> v
              | ValueNone -> Vector3.Zero
            Particle = particle
          }
        }

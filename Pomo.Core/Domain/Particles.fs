namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Skill

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
    | Line of width: float32 * length: float32

  [<Struct>]
  type SimulationSpace =
    | World
    | Local

  /// Controls how particles spawn and flow within shaped areas
  [<Struct>]
  type EmissionMode =
    | Uniform // Fill area instantly (current behavior)
    | Outward // Projection: spawn at origin, flow outward
    | Inward // Convergence: spawn at edge, flow inward
    | EdgeOnly // Ring: spawn only at outer edge

  /// Determines how particles are rendered
  [<Struct>]
  type RenderMode =
    | Billboard of texture: string
    | Mesh of modelAsset: string

  /// Controls rotation behavior for mesh particles
  [<Struct>]
  type MeshRotationMode =
    | Fixed // No rotation - stays upright (pillars, beams)
    | Tumbling // Random initial + angular velocity (flying debris)
    | RandomStatic // Random initial, no spinning (settled debris)

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
    Drag: float32
  }

  type EmitterConfig = {
    Name: string
    RenderMode: RenderMode
    BlendMode: BlendMode
    SimulationSpace: SimulationSpace
    InheritVelocity: float32
    Rate: int
    Burst: int
    Shape: EmitterShape
    LocalOffset: Vector3
    Particle: ParticleConfig
    FloorHeight: float32
    EmissionRotation: Vector3
    EmissionMode: EmissionMode
    MeshRotation: MeshRotationMode
    ScalePivot: Vector3 // Pivot point for mesh scaling (e.g., 0,-0.5,0 = bottom anchor)
    ScaleAxis: Vector3 // Per-axis scale multiplier (e.g., 0,1,0 = height-only growth)
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

  type VisualEmitter = {
    Config: EmitterConfig
    Particles: ResizeArray<Particle>
    Accumulator: float32 ref
    BurstDone: bool ref
  }

  /// Mesh particle with 3D rotation for tumbling debris effects
  [<Struct>]
  type MeshParticle = {
    Position: Vector3
    Velocity: Vector3
    Rotation: Quaternion
    AngularVelocity: Vector3
    Scale: float32
    Life: float32
    MaxLife: float32
  }

  module MeshParticle =
    let inline withPosition (value: Vector3) (p: MeshParticle) = {
      p with
          Position = value
    }

    let inline withVelocity (value: Vector3) (p: MeshParticle) = {
      p with
          Velocity = value
    }

    let inline withRotation (value: Quaternion) (p: MeshParticle) = {
      p with
          Rotation = value
    }

    let inline withScale (value: float32) (p: MeshParticle) = {
      p with
          Scale = value
    }

    let inline withLife (value: float32) (p: MeshParticle) = {
      p with
          Life = value
    }

  type VisualMeshEmitter = {
    Config: EmitterConfig
    ModelPath: string // Pre-extracted from RenderMode at creation
    Particles: ResizeArray<MeshParticle>
    Accumulator: float32 ref
    BurstDone: bool ref
  }

  /// Splits emitter configs by RenderMode in a single pass
  /// Returns (billboardEmitters, meshEmitters)
  let splitEmittersByRenderMode
    (configs: EmitterConfig array)
    : struct (VisualEmitter array * VisualMeshEmitter array) =
    configs
    |> Array.partitionMap(fun config ->
      match config.RenderMode with
      | Billboard _ ->
        Choice1Of2 {
          Config = config
          Particles = ResizeArray<Particle>()
          Accumulator = ref 0.0f
          BurstDone = ref false
        }
      | Mesh modelPath ->
        Choice2Of2 {
          Config = config
          ModelPath = modelPath
          Particles = ResizeArray<MeshParticle>()
          Accumulator = ref 0.0f
          BurstDone = ref false
        })

  [<Struct>]
  type EffectOverrides = {
    Rotation: Quaternion voption
    Scale: float32 voption
    Color: Color voption
    Area: SkillArea voption
    EmissionMode: EmissionMode voption
  }

  module EffectOverrides =
    let empty = {
      Rotation = ValueNone
      Scale = ValueNone
      Color = ValueNone
      Area = ValueNone
      EmissionMode = ValueNone
    }

  type VisualEffect = {
    Id: string
    Emitters: VisualEmitter array
    MeshEmitters: VisualMeshEmitter array
    Position: Vector3 ref
    Rotation: Quaternion ref
    Scale: Vector3 ref
    IsAlive: bool ref
    Owner: Guid<EntityId> voption
    Overrides: EffectOverrides
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

    module EmissionModeCodec =
      let decoder: Decoder<EmissionMode> =
        fun json -> decode {
          let! str = Required.string json

          match str.ToLowerInvariant() with
          | "uniform" -> return Uniform
          | "outward" -> return Outward
          | "inward" -> return Inward
          | "edgeonly" -> return EdgeOnly
          | _ ->
            return!
              DecodeError.ofError(json.Clone(), $"Unknown EmissionMode: {str}")
              |> Error
        }

    module MeshRotationModeCodec =
      let decoder: Decoder<MeshRotationMode> =
        fun json -> decode {
          let! str = Required.string json

          match str.ToLowerInvariant() with
          | "fixed" -> return Fixed
          | "tumbling" -> return Tumbling
          | "randomstatic" -> return RandomStatic
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown MeshRotationMode: {str}"
              )
              |> Error
        }

    module RenderModeCodec =
      let decoder: Decoder<RenderMode> =
        fun json -> decode {
          let! type' = Required.Property.get ("Type", Required.string) json

          match type'.ToLowerInvariant() with
          | "billboard" ->
            let! texture =
              Required.Property.get ("Texture", Required.string) json

            return Billboard texture
          | "mesh" ->
            let! modelAsset =
              Required.Property.get ("Model", Required.string) json

            return Mesh modelAsset
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown RenderMode type: {type'}"
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
            | "line" ->
              let! width = Required.Property.get ("Width", Required.float) json

              let! length =
                Required.Property.get ("Length", Required.float) json

              return Line(float32 width, float32 length)
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
          and! speed = Required.Property.get ("Speed", rangeDecoder) json

          and! sizeStart =
            Required.Property.get ("SizeStart", Required.float) json

          and! sizeEnd = Required.Property.get ("SizeEnd", Required.float) json

          and! colorStart =
            Required.Property.get ("ColorStart", Helper.colorFromHex) json

          and! colorEnd =
            Required.Property.get ("ColorEnd", Helper.colorFromHex) json

          and! gravity = VOptional.Property.get ("Gravity", Required.float) json

          and! randomVelocity =
            VOptional.Property.get ("RandomVelocity", Helper.vec3FromDict) json

          and! drag = VOptional.Property.get ("Drag", Required.float) json

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
            Drag =
              match drag with
              | ValueSome v -> float32 v
              | ValueNone -> 0.0f
          }
        }

    module EmitterConfigCodec =

      let decoder: Decoder<EmitterConfig> =
        fun json -> decode {
          let! name = VOptional.Property.get ("Name", Required.string) json

          let! renderModeType =
            VOptional.Property.get ("RenderMode", Required.string) json

          let! texture =
            VOptional.Property.get ("Texture", Required.string) json

          // Support both "Model" and "ModelAsset" for mesh particles
          let! modelAsset =
            VOptional.Property.get ("Model", Required.string) json

          let renderMode =
            match renderModeType with
            | ValueSome "Mesh" ->
              match modelAsset with
              | ValueSome model -> Mesh model
              | ValueNone -> Billboard "Particles/error" // Fallback or error?
            | ValueSome "Billboard" ->
              match texture with
              | ValueSome tex -> Billboard tex
              | ValueNone -> Billboard "Particles/default"
            | _ ->
              // Fallback / Inference logic
              match modelAsset, texture with
              | ValueSome model, _ -> Mesh model
              | ValueNone, ValueSome tex -> Billboard tex
              | ValueNone, ValueNone -> Billboard "Particles/default"

          // BlendMode is optional for mesh particles (defaults to AlphaBlend)
          let! blendModeOpt =
            VOptional.Property.get ("BlendMode", BlendModeCodec.decoder) json

          let blendMode =
            match blendModeOpt with
            | ValueSome bm -> bm
            | ValueNone -> AlphaBlend // Default for mesh particles

          let! simulationSpace =
            VOptional.Property.get
              ("SimulationSpace", SimulationSpaceCodec.decoder)
              json

          let! inheritVelocity =
            VOptional.Property.get ("InheritVelocity", Required.float) json

          let! rate = Required.Property.get ("Rate", Required.int) json
          let! burst = VOptional.Property.get ("Burst", Required.int) json

          let! shape = EmitterShapeCodec.decoder json

          let! localOffset =
            VOptional.Property.get ("LocalOffset", Helper.vec3FromDict) json

          let! particle =
            Required.Property.get ("Particle", ParticleConfigCodec.decoder) json

          let! floorHeight =
            VOptional.Property.get ("FloorHeight", Required.float) json

          let! emissionRotation =
            VOptional.Property.get
              ("EmissionRotation", Helper.vec3FromDict)
              json

          let! emissionMode =
            VOptional.Property.get
              ("EmissionMode", EmissionModeCodec.decoder)
              json

          let! meshRotation =
            VOptional.Property.get
              ("MeshRotation", MeshRotationModeCodec.decoder)
              json

          let! meshRotationMode =
            VOptional.Property.get
              ("MeshRotationMode", MeshRotationModeCodec.decoder)
              json

          let! scalePivot =
            VOptional.Property.get ("ScalePivot", Helper.vec3FromDict) json

          let! scaleAxis =
            VOptional.Property.get ("ScaleAxis", Helper.vec3FromDict) json

          return {
            Name =
              match name with
              | ValueSome n -> n
              | ValueNone -> ""
            RenderMode = renderMode
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
            FloorHeight =
              match floorHeight with
              | ValueSome v -> float32 v
              | ValueNone -> 0.0f
            EmissionRotation =
              match emissionRotation with
              | ValueSome v -> v
              | ValueNone -> Vector3.Zero
            EmissionMode =
              match emissionMode with
              | ValueSome m -> m
              | ValueNone -> Uniform
            MeshRotation =
              meshRotation
              |> ValueOption.orElse meshRotationMode
              |> ValueOption.defaultValue Tumbling
            ScalePivot =
              match scalePivot with
              | ValueSome v -> v
              | ValueNone -> Vector3.Zero
            ScaleAxis =
              match scaleAxis with
              | ValueSome v -> v
              | ValueNone -> Vector3.One // Default: uniform scaling
          }
        }

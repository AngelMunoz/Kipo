namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core

module Orbital =

  [<Struct>]
  type OrbitalConfig = {
    Count: int
    Radius: float32
    CenterOffset: Vector3
    RotationAxis: Vector3
    PathScale: Vector2
    StartSpeed: float32
    EndSpeed: float32
    Duration: float32
    Visual: VisualManifest
  }

  let calculatePosition
    (config: OrbitalConfig)
    (elapsed: float32)
    (index: int)
    =
    let accel = (config.EndSpeed - config.StartSpeed) / config.Duration
    let angle = config.StartSpeed * elapsed + 0.5f * accel * elapsed * elapsed

    let indexOffset = (MathHelper.TwoPi / float32 config.Count) * float32 index
    let totalAngle = angle + indexOffset

    let x = MathF.Cos totalAngle * config.Radius * config.PathScale.X
    let z = MathF.Sin totalAngle * config.Radius * config.PathScale.Y
    // Horizontal plane (X-Z): flat halo around entity
    // RotationAxis tilts this to add verticalness
    let localPos = Vector3(x, 0.0f, z)

    let rotation =
      if config.RotationAxis = Vector3.UnitZ then
        Quaternion.Identity
      else
        let axis = Vector3.Cross(Vector3.UnitZ, config.RotationAxis)

        if axis.LengthSquared() < 0.001f then
          if Vector3.Dot(Vector3.UnitZ, config.RotationAxis) < 0.0f then
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.Pi)
          else
            Quaternion.Identity
        else
          let angle =
            MathF.Acos(Vector3.Dot(Vector3.UnitZ, config.RotationAxis))

          Quaternion.CreateFromAxisAngle(Vector3.Normalize axis, angle)

    Vector3.Transform(localPos, rotation)

  [<Struct>]
  type OrbitalCenter =
    | EntityCenter of entityId: Guid<EntityId>
    | PositionCenter of position: Vector2

  [<Struct>]
  type ActiveOrbital = {
    Center: OrbitalCenter
    Config: OrbitalConfig
    StartTime: float32
  }


  module Serialization =
    open JDeck
    open JDeck.Decode
    open Pomo.Core.Domain.Core.Serialization

    let decoder: Decoder<OrbitalConfig> =
      fun json -> decode {
        let! count = Required.Property.get ("Count", Required.int) json
        and! radius = Required.Property.get ("Radius", Required.float) json

        and! centerOffset =
          VOptional.Property.get ("CenterOffset", Helper.vec3FromDict) json

        and! rotationAxis =
          VOptional.Property.get ("RotationAxis", Helper.vec3FromDict) json

        and! pathScaleArr =
          VOptional.Property.array ("PathScale", Required.float) json

        and! startSpeed =
          Required.Property.get ("StartSpeed", Required.float) json

        and! endSpeed = Required.Property.get ("EndSpeed", Required.float) json
        and! duration = Required.Property.get ("Duration", Required.float) json

        and! visual =
          VOptional.Property.get ("Visual", VisualManifest.decoder) json

        let pathScale =
          match pathScaleArr with
          | ValueSome [| x; y |] -> Vector2(float32 x, float32 y)
          | _ -> Vector2.One

        return {
          Count = count
          Radius = float32 radius
          CenterOffset =
            match centerOffset with
            | ValueSome v -> v
            | ValueNone -> Vector3.Zero
          RotationAxis =
            match rotationAxis with
            | ValueSome v -> v
            | ValueNone -> Vector3.Up
          PathScale = pathScale
          StartSpeed = float32 startSpeed
          EndSpeed = float32 endSpeed
          Duration = float32 duration
          Visual =
            match visual with
            | ValueSome v -> v
            | ValueNone -> VisualManifest.empty
        }
      }

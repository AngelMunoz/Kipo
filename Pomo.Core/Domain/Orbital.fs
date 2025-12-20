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

namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Entity


module Projectile =

  [<Struct>]
  type CollisionMode =
    | IgnoreTerrain
    | BlockedByTerrain

  [<Struct>]
  type ExtraVariations =
    | Chained of jumpsLeft: int * maxRange: float32
    | Bouncing of bouncesLeft: int
    | Descending of currentAltitude: float32 * fallSpeed: float32

  [<Struct>]
  type ProjectileTarget =
    | EntityTarget of entity: Guid<EntityId>
    | PositionTarget of position: Vector2

  [<Struct>]
  type ProjectileInfo = {
    Speed: float32
    Collision: CollisionMode
    Variations: ExtraVariations voption
    Visuals: VisualManifest
  }

  [<Struct>]
  type LiveProjectile = {
    Caster: Guid<EntityId>
    Target: ProjectileTarget
    SkillId: int<SkillId>
    Info: ProjectileInfo
  }

  module Serialization =
    open System.Text.Json
    open System.Text.Json.Serialization
    open JDeck
    open JDeck.Decode
    open Pomo.Core.Domain.Core.Serialization

    module CollisionMode =
      let decoder: Decoder<CollisionMode> =
        fun json -> decode {
          let! modeStr = Required.string json

          match modeStr.ToLowerInvariant() with
          | "ignoreterrain" -> return IgnoreTerrain
          | "blockedbyterrain" -> return BlockedByTerrain
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown CollisionMode: {modeStr}"
              )
              |> Error
        }

    module ProjectileKind =
      let decoder: Decoder<ExtraVariations> =
        fun json -> decode {
          let! kindStr = Required.Property.get ("Type", Required.string) json

          match kindStr.ToLowerInvariant() with
          | "chained" ->
            let! jumpsLeft =
              Required.Property.get ("JumpsLeft", Required.int) json

            and! maxRange =
              Required.Property.get ("MaxRange", Required.float) json

            return Chained(jumpsLeft, float32 maxRange)
          | "bouncing" ->
            let! bouncesLeft =
              Required.Property.get ("BouncesLeft", Required.int) json

            return Bouncing(bouncesLeft)
          | "descending" ->
            let! startAltitude =
              Required.Property.get ("StartAltitude", Required.float) json

            and! fallSpeed =
              Required.Property.get ("FallSpeed", Required.float) json

            return Descending(float32 startAltitude, float32 fallSpeed)
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown ProjectileKind: {kindStr}"
              )
              |> Error
        }

    module ProjectileInfo =

      let decoder: Decoder<ProjectileInfo> =
        fun json -> decode {
          let! speed = Required.Property.get ("Speed", Required.float) json

          and! collision =
            Required.Property.get ("CollisionMode", CollisionMode.decoder) json

          and! kind =
            VOptional.Property.get ("Kind", ProjectileKind.decoder) json

          and! visuals =
            VOptional.Property.get ("Visuals", VisualManifest.decoder) json

          return {
            Speed = float32 speed
            Collision = collision
            Variations = kind
            Visuals =
              match visuals with
              | ValueSome v -> v
              | ValueNone -> VisualManifest.empty
          }
        }

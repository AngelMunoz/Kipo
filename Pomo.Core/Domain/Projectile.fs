namespace Pomo.Core.Domain

open System
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
    // e.g. chained to another target
    | Chained of jumpsLeft: int * maxRange: float32
    // e.g. blocked by terrain, bounces instead of disapearing
    | Bouncing of bouncesLeft: int

  [<Struct>]
  type ProjectileInfo = {
    Speed: float32
    Collision: CollisionMode
    Variations: ExtraVariations voption
  }

  [<Struct>]
  type LiveProjectile = {
    Caster: Guid<EntityId>
    Target: Guid<EntityId>
    SkillId: int<SkillId>
    Info: ProjectileInfo
  }

  module Serialization =
    open System.Text.Json
    open System.Text.Json.Serialization
    open JDeck
    open JDeck.Decode

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

          return {
            Speed = float32 speed
            Collision = collision
            Variations = kind
          }
        }

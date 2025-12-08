module Pomo.Core.Serialization

open System.Text.Json
open System.Text.Json.Serialization
open JDeck
open Pomo.Core.Domain
open Pomo.Core.Domain.Core.Serialization
open Pomo.Core.Domain.Entity.Serialization
open Pomo.Core.Domain.Skill.Serialization
open Pomo.Core.Domain.Projectile.Serialization
open Pomo.Core.Domain.Item.Serialization
open Pomo.Core.Domain.AI.Serialization
open Pomo.Core.Domain.Animation.Serialization

let private JsonSerializerOptions =
  JsonSerializerOptions()
  |> Codec.useDecoder Stat.decoder
  |> Codec.useDecoder StatModifier.decoder
  |> Codec.useDecoder Element.decoder
  |> Codec.useDecoder Status.decoder
  |> Codec.useDecoder ResourceType.decoder
  |> Codec.useDecoder Faction.decoder
  |> Codec.useDecoder Family.decoder
  |> Codec.useDecoder Stage.decoder
  |> Codec.useDecoder Formula.decoder
  |> Codec.useDecoder EffectKind.decoder
  |> Codec.useDecoder StackingRule.decoder
  |> Codec.useDecoder Duration.decoder
  |> Codec.useDecoder EffectModifier.decoder
  |> Codec.useDecoder Effect.decoder
  |> Codec.useDecoder SkillIntent.decoder
  |> Codec.useDecoder DamageSource.decoder
  |> Codec.useDecoder PassiveSkill.decoder
  |> Codec.useDecoder ActiveSkill.decoder
  |> Codec.useDecoder Skill.decoder
  |> Codec.useDecoder ResourceCost.decoder
  |> Codec.useDecoder GroundAreaKind.decoder
  |> Codec.useDecoder Targeting.decoder
  |> Codec.useDecoder CastOrigin.decoder
  |> Codec.useDecoder CollisionMode.decoder
  |> Codec.useDecoder ProjectileKind.decoder
  |> Codec.useDecoder ProjectileInfo.decoder
  |> Codec.useDecoder Delivery.decoder
  |> Codec.useDecoder ItemDefinition.decoder
  |> Codec.useDecoder AIArchetype.decoder
  |> Codec.useDecoder Rig.decoder
  |> Codec.useDecoder ModelConfig.decoder
  |> Codec.useDecoder AnimationClip.decoder


type JDeckDeserializer =
  abstract member Deserialize<'T> : json: string -> Result<'T, DecodeError>
  abstract member Deserialize<'T> : json: byte[] -> Result<'T, DecodeError>


let create() : JDeckDeserializer =
  { new JDeckDeserializer with
      member _.Deserialize<'T>(json: string) : Result<'T, DecodeError> =
        Decoding.fromString(json, Decode.autoJsonOptions JsonSerializerOptions)

      member _.Deserialize<'T>(json: byte[]) : Result<'T, DecodeError> =
        Decoding.fromBytes(json, Decode.autoJsonOptions JsonSerializerOptions)
  }

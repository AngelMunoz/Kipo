namespace Pomo.Core.Domain

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core

module Entity =

  [<Struct>]
  type EntitySnapshot = {
    Id: Guid<EntityId>
    Position: Vector2
    Velocity: Vector2
  }

  [<Struct>]
  type Status =
    | Alive
    | Dead

  [<Struct>]
  type ResourceType =
    | HP
    | MP

  [<Struct>]
  type Resource = { HP: int; MP: int; Status: Status }

  [<Struct>]
  type Faction =
    | Player
    | NPC
    | Ally
    | Enemy
    | AIControlled

  [<Struct>]
  type Family =
    | Power
    | Magic
    | Charm
    | Sense

  [<Struct>]
  type Stage =
    | First
    | Second
    | Third

  [<Struct>]
  type Profession = { Family: Family; Stage: Stage }

  [<Struct>]
  type BaseStats = {
    Power: int
    Magic: int
    Sense: int
    Charm: int
  }

  [<Struct>]
  type DerivedStats = {
    // Power derived stats
    AP: int
    AC: int
    DX: int
    // Magic derived stats
    MP: int
    MA: int
    MD: int
    // Sense derived stats
    WT: int
    DA: int
    LK: int
    // Charm derived stats
    HP: int
    DP: int
    HV: int
    // Movement
    MS: int
    // Regeneration
    HPRegen: int
    MPRegen: int

    // Element % of attributes and resistances
    ElementAttributes: HashMap<Element, float>
    ElementResistances: HashMap<Element, float>
  }


  module Serialization =
    open JDeck

    module Status =
      let decoder: Decoder<Status> =
        fun json -> decode {
          let! statusStr = Required.string json

          match statusStr with
          | "Alive" -> return Alive
          | "Dead" -> return Dead
          | other ->
            return!
              DecodeError.ofError(json.Clone(), $"Unknown Status: {other}")
              |> Error
        }

    module ResourceType =
      let decoder: Decoder<ResourceType> =
        fun json -> decode {
          let! resTypeStr = Required.string json

          match resTypeStr with
          | "HP" -> return HP
          | "MP" -> return MP
          | other ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown ResourceType: {other}"
              )
              |> Error
        }

    module Faction =
      let decoder: Decoder<Faction> =
        fun json -> decode {
          let! factionStr = Required.string json

          match factionStr with
          | "Player" -> return Player
          | "NPC" -> return NPC
          | "Ally" -> return Ally
          | "Enemy" -> return Enemy
          | "AIControlled" -> return AIControlled
          | other ->
            return!
              DecodeError.ofError(json.Clone(), $"Unknown Faction: {other}")
              |> Error
        }

    module Family =
      let decoder: Decoder<Family> =
        fun json -> decode {
          let! familyStr = Required.string json

          match familyStr with
          | "Power" -> return Power
          | "Magic" -> return Magic
          | "Charm" -> return Charm
          | "Sense" -> return Sense
          | other ->
            return!
              DecodeError.ofError(json.Clone(), $"Unknown Family: {other}")
              |> Error
        }

    module Stage =
      let decoder: Decoder<Stage> =
        fun json -> decode {
          let! stageStr = Required.string json

          match stageStr with
          | "First" -> return First
          | "Second" -> return Second
          | "Third" -> return Third
          | other ->
            return!
              DecodeError.ofError(json.Clone(), $"Unknown Stage: {other}")
              |> Error
        }

    module BaseStats =
      let decoder: Decoder<BaseStats> =
        fun json -> decode {
          let! power = Required.Property.get ("Power", Required.int) json
          and! magic = Required.Property.get ("Magic", Required.int) json
          and! sense = Required.Property.get ("Sense", Required.int) json
          and! charm = Required.Property.get ("Charm", Required.int) json

          return {
            Power = power
            Magic = magic
            Sense = sense
            Charm = charm
          }
        }

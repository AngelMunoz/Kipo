namespace Pomo.Core.Domain

open System
open FSharp.UMX
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Skill

module Item =

  [<Struct>]
  type Slot =
    | Head
    | Chest
    | Legs
    | Feet
    | Hands
    | Weapon
    | Shield
    | Accessory

  [<Struct>]
  type EquipmentProperties = {
    Slot: Slot
    Stats: StatModifier array
  }

  [<Struct>]
  type UsabilityProperties = { Effect: Effect }

  [<Struct>]
  type ItemKind =
    | Wearable of wearable: EquipmentProperties
    | Usable of usable: UsabilityProperties
    | NonUsable

  [<Struct>]
  type ItemDefinition = {
    Id: int<ItemId>
    Name: string
    Weight: int
    Kind: ItemKind
  }

  [<Struct>]
  type ItemInstance = {
    InstanceId: Guid<ItemInstanceId>
    ItemId: int<ItemId>
    UsesLeft: int voption
  }

  module Serialization =
    open JDeck
    open JDeck.Decode
    open Pomo.Core.Domain.Core.Serialization
    open Pomo.Core.Domain.Skill.Serialization

    module Slot =
      let decoder: Decoder<Slot> =
        fun json -> decode {
          let! slotStr = Required.string json

          match slotStr with
          | "Head" -> return Head
          | "Chest" -> return Chest
          | "Legs" -> return Legs
          | "Feet" -> return Feet
          | "Hands" -> return Hands
          | "Weapon" -> return Weapon
          | "Shield" -> return Shield
          | "Accessory" -> return Accessory
          | other ->
            return!
              DecodeError.ofError(json.Clone(), $"Unknown Slot: {other}")
              |> Error
        }

    module EquipmentProperties =
      let decoder: Decoder<EquipmentProperties> =
        fun json -> decode {
          let! slot = Required.Property.get ("Slot", Slot.decoder) json

          and! stats =
            Required.Property.array ("Stats", StatModifier.decoder) json

          return { Slot = slot; Stats = stats }
        }

    module UsabilityProperties =
      let decoder: Decoder<UsabilityProperties> =
        fun json -> decode {
          let! effect = Required.Property.get ("Effect", Effect.decoder) json
          return { Effect = effect }
        }

    module ItemKind =
      let decoder: Decoder<ItemKind> =
        fun json -> decode {
          let! kindType = Required.Property.get ("Type", Required.string) json

          match kindType with
          | "Wearable" ->
            let! props = EquipmentProperties.decoder json
            return Wearable props
          | "Usable" ->
            let! props = UsabilityProperties.decoder json
            return Usable props
          | "NonUsable" -> return NonUsable
          | other ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown ItemKind type: {other}"
              )
              |> Error
        }

    module ItemDefinition =
      let decoder: Decoder<ItemDefinition> =
        fun json -> decode {
          let! id = Required.Property.get ("Id", Required.int) json
          and! name = Required.Property.get ("Name", Required.string) json
          and! weight = Required.Property.get ("Weight", Required.int) json
          and! kind = Required.Property.get ("Kind", ItemKind.decoder) json

          return {
            Id = UMX.tag id
            Name = name
            Weight = weight
            Kind = kind
          }
        }

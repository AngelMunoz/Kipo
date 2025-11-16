namespace Pomo.Core.Domain

module Units =

  [<Measure>]
  type EntityId

  [<Measure>]
  type EffectId

  [<Measure>]
  type SkillId

  [<Measure>]
  type ItemId

  [<Measure>]
  type ItemInstanceId


module Core =
  [<Struct>]
  type Element =
    | Fire
    | Water
    | Earth
    | Air
    | Lightning
    | Light
    | Dark
    | Neutral

  [<Struct>]
  type CombatStatus =
    | Stunned
    | Silenced
    | Rooted

  [<Struct>]
  type Stat =
    // Derived stats (using game definition names)
    | AP // Attack Power
    | AC // Accuracy
    | DX // Dexterity
    | MP // Mana Pool
    | MA // Magic Attack
    | MD // Magic Defense
    | WT // Weight
    | DA // Detect Ability
    | LK // Luck
    | HP // Health Pool
    | DP // Defense Points
    | HV // Evasion
    | MS // Movement Speed
    | HPRegen
    | MPRegen
    | ElementResistance of ofElement: Element
    | ElementAttribute of ofElement: Element

  [<Struct>]
  type StatModifier =
    | Additive of addStat: Stat * adStatValue: float
    | Multiplicative of mulStat: Stat * mulStatValue: float


  type CoreEventListener =

    abstract member StartListening: unit -> System.IDisposable


  module Serialization =
    open JDeck

    module Element =
      let decoder: Decoder<Element> =
        fun json -> decode {
          let! elemStr = Required.string json

          match elemStr with
          | "Fire" -> return Fire
          | "Water" -> return Water
          | "Earth" -> return Earth
          | "Air" -> return Air
          | "Lightning" -> return Lightning
          | "Light" -> return Light
          | "Dark" -> return Dark
          | "Neutral" -> return Neutral
          | other ->
            return!
              DecodeError.ofError(json.Clone(), $"Unknown Element: {other}")
              |> Error
        }

    module CombatStatus =
      let decoder: Decoder<CombatStatus> =
        fun json -> decode {
          let! statusStr = Required.string json

          match statusStr.ToLowerInvariant() with
          | "stunned" -> return Stunned
          | "silenced" -> return Silenced
          | "rooted" -> return Rooted
          | other ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown CombatStatus: {other}"
              )
              |> Error
        }


    module Stat =
      let decoder: Decoder<Stat> =
        fun json -> decode {
          let! statStr = Required.string json

          match statStr with
          | "AP" -> return AP
          | "AC" -> return AC
          | "DX" -> return DX
          | "MP" -> return MP
          | "MA" -> return MA
          | "MD" -> return MD
          | "WT" -> return WT
          | "DA" -> return DA
          | "LK" -> return LK
          | "HP" -> return HP
          | "DP" -> return DP
          | "HV" -> return HV
          | "MS" -> return MS
          | "HPRegen" -> return HPRegen
          | "MPRegen" -> return MPRegen
          | "ElementRes:Fire" -> return ElementResistance Fire
          | "ElementRes:Water" -> return ElementResistance Water
          | "ElementRes:Earth" -> return ElementResistance Earth
          | "ElementRes:Air" -> return ElementResistance Air
          | "ElementRes:Lightning" -> return ElementResistance Lightning
          | "ElementRes:Light" -> return ElementResistance Light
          | "ElementRes:Dark" -> return ElementResistance Dark
          | "ElementRes:Neutral" -> return ElementResistance Neutral
          | "ElementAttr:Fire" -> return ElementAttribute Fire
          | "ElementAttr:Water" -> return ElementAttribute Water
          | "ElementAttr:Earth" -> return ElementAttribute Earth
          | "ElementAttr:Air" -> return ElementAttribute Air
          | "ElementAttr:Lightning" -> return ElementAttribute Lightning
          | "ElementAttr:Light" -> return ElementAttribute Light
          | "ElementAttr:Dark" -> return ElementAttribute Dark
          | "ElementAttr:Neutral" -> return ElementAttribute Neutral
          | _ ->
            return!
              DecodeError.ofError(json.Clone(), $"Unknown Stat: {statStr}")
              |> Error
        }

    module StatModifier =
      /// Examples:
      /// { "type": "Additive", "stat": "MA", "value": 50 }
      /// { "type": "Multiplicative", "stat": "AP", "value": 1.2 }
      let decoder: Decoder<StatModifier> =
        fun json -> decode {
          let! modifierType =
            Required.Property.get ("type", Required.string) json

          match modifierType with
          | "Additive" ->
            let! stat = Required.Property.get ("stat", Stat.decoder) json
            and! value = Required.Property.get ("value", Required.float) json
            return Additive(stat, value)
          | "Multiplicative" ->
            let! stat = Required.Property.get ("stat", Stat.decoder) json
            and! value = Required.Property.get ("value", Required.float) json
            return Multiplicative(stat, value)
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown StatModifier type: {modifierType}"
              )
              |> Error
        }

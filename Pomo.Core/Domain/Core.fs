namespace Pomo.Core.Domain

module Units =

  [<Measure>]
  type EntityId

  [<Measure>]
  type EffectId

  [<Measure>]
  type SkillId


module Core =

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
    | MovementSpeed // Movement Speed

  [<Struct>]
  type StatModifier =
    | Additive of addStat: Stat * adStatValue: int
    | Multiplicative of mulStat: Stat * mulStatValue: float



  module Serialization =
    open JDeck

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
          | "MovementSpeed" -> return MovementSpeed
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
            let! value = Required.Property.get ("value", Required.int) json
            return Additive(stat, value)
          | "Multiplicative" ->
            let! stat = Required.Property.get ("stat", Stat.decoder) json
            let! value = Required.Property.get ("value", Required.float) json
            return Multiplicative(stat, value)
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown StatModifier type: {modifierType}"
              )
              |> Error
        }

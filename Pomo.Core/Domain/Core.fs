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

  [<Measure>]
  type AiArchetypeId

  [<Measure>]
  type TileId

  [<Measure>]
  type LayerId

  [<Measure>]
  type ObjectId

  [<Measure>]
  type ScenarioId


module Render =
  [<RequireQualifiedAccess>]
  module Layer =
    [<Literal>]
    let TerrainBase = 0

    [<Literal>]
    let Items = 100

    [<Literal>]
    let Entities = 200

    [<Literal>]
    let Projectiles = 250

    [<Literal>]
    let VFX = 300

    [<Literal>]
    let UI = 1000

    [<Literal>]
    let Debug = 9999


module Core =
  open FSharp.UMX
  open System
  open Microsoft.Xna.Framework
  open Microsoft.Xna.Framework.Graphics
  open Units



  module Constants =
    let DefaultPixelsPerUnit = Vector2(64.0f, 32.0f)

    module Entity =
      let Size = Vector2(16.0f, 16.0f)
      let ModelScale = 0.25f

      [<Literal>]
      let CollisionRadius = 8.0f

      [<Literal>]
      let CollisionDistance = 32.0f

      [<Literal>]
      let SkillActivationRangeBuffer = 5.0f

    module Projectile =
      let Size = Vector2(8.0f, 8.0f)

    module UI =
      let TargetingIndicatorSize = Vector2(20.0f, 20.0f)

    module Collision =
      [<Literal>]
      let GridCellSize = 64.0f

    module Navigation =
      [<Literal>]
      let GridCellSize = 8.0f

      let EntitySize = Vector2(4.0f, 4.0f)

    module Spawning =
      let DefaultDuration = TimeSpan.FromSeconds 1.0

    module AI =
      [<Literal>]
      let WaypointReachedThreshold = 8.0f

      /// Margin multiplier for AI active zone (1.3 = 30% beyond screen edges)
      [<Literal>]
      let ActiveZoneMargin = 1.3f

    module Debug =
      [<Literal>]
      let StatYOffset = -20.0f

      [<Literal>]
      let EffectYOffset = -15.0f

      [<Literal>]
      let InventoryYOffset = 150.0f

      let TransientCommandDuration = TimeSpan.FromSeconds 2.0



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

  [<Struct>]
  type SlotProcessing =
    | Skill of skillId: int<Units.SkillId>
    | Item of itemInstanceId: Guid<Units.ItemInstanceId>

  [<Struct>]
  type VisualManifest = {
    ModelId: string voption
    VfxId: string voption
    AnimationId: string voption
    AttachmentPoint: string voption
  }

  module VisualManifest =
    let empty = {
      ModelId = ValueNone
      VfxId = ValueNone
      AnimationId = ValueNone
      AttachmentPoint = ValueNone
    }

  type CoreEventListener =

    abstract member StartListening: unit -> IDisposable


  module Serialization =
    open JDeck
    open System.Globalization

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
      /// { "Type": "Additive", "Stat": "MA", "Value": 50 }
      /// { "Type": "Multiplicative", "Stat": "AP", "Value": 1.2 }
      let decoder: Decoder<StatModifier> =
        fun json -> decode {
          let! modifierType =
            Required.Property.get ("Type", Required.string) json

          match modifierType with
          | "Additive" ->
            let! stat = Required.Property.get ("Stat", Stat.decoder) json
            and! value = Required.Property.get ("Value", Required.float) json
            return Additive(stat, value)
          | "Multiplicative" ->
            let! stat = Required.Property.get ("Stat", Stat.decoder) json
            and! value = Required.Property.get ("Value", Required.float) json
            return Multiplicative(stat, value)
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown StatModifier type: {modifierType}"
              )
              |> Error
        }

    module VisualManifest =
      let decoder: Decoder<VisualManifest> =
        fun json -> decode {
          let! modelId = VOptional.Property.get ("Model", Required.string) json

          and! vfxId = VOptional.Property.get ("Vfx", Required.string) json

          and! animationId =
            VOptional.Property.get ("Animation", Required.string) json

          and! attachmentPoint =
            VOptional.Property.get ("Attachment", Required.string) json

          return {
            ModelId = modelId
            VfxId = vfxId
            AnimationId = animationId
            AttachmentPoint = attachmentPoint
          }
        }

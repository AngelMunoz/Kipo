namespace Pomo.Core.Domain.AI

open System
open FSharp.UMX
open Microsoft.Xna.Framework
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Units
open FSharp.Data.Adaptive

// --- Core AI Types ---

[<Struct>]
type BehaviorType =
  | Patrol
  | Aggressive
  | Defensive
  | Supporter
  | Ambusher
  | Turret
  | Passive

[<Struct>]
type CueType =
  | Visual
  | Audio
  | Damage
  | Communication
  | Memory

[<Struct>]
type CueStrength =
  | Weak
  | Moderate
  | Strong
  | Overwhelming

[<Struct>]
type PerceptionConfig = {
  visualRange: float32
  fov: float32 // Field of View in degrees
  memoryDuration: TimeSpan
  leashDistance: float32
}

[<Struct>]
type ResponseType =
  | Ignore
  | Investigate
  | Engage
  | Flee
  | Evade

[<Struct>]
type CuePriority = {
  cueType: CueType
  minStrength: CueStrength
  priority: int
  response: ResponseType
}

type AIArchetype = {
  id: int<AiArchetypeId>
  name: string
  behaviorType: BehaviorType
  perceptionConfig: PerceptionConfig
  cuePriorities: CuePriority[]
  decisionInterval: TimeSpan
  baseStats: BaseStats
}

// --- Runtime State ---

[<Struct>]
type PerceptionCue = {
  cueType: CueType
  strength: CueStrength
  sourceEntityId: Guid<EntityId> voption
  position: Vector2
  timestamp: TimeSpan
}

[<Struct>]
type MemoryEntry = {
  entityId: Guid<EntityId>
  lastSeenTick: TimeSpan
  lastKnownPosition: Vector2
  confidence: float32
}

[<Struct>]
type AIState =
  | Idle
  | Patrolling
  | Investigating
  | Chasing
  | Attacking
  | Fleeing

type AIController = {
  controlledEntityId: Guid<EntityId>
  archetypeId: int<AiArchetypeId>

  // State Machine
  currentState: AIState
  stateEnterTime: TimeSpan

  // Navigation
  spawnPosition: Vector2
  absoluteWaypoints: Vector2[] voption
  waypointIndex: int

  // Decision Making
  lastDecisionTime: TimeSpan
  currentTarget: Guid<EntityId> voption

  // Skills (from AIEntityDefinition)
  skills: int<SkillId>[]

  // Memory
  memories: HashMap<Guid<EntityId>, MemoryEntry>
}

type AIFamilyConfig = {
  StatScaling: HashMap<string, float32>
  SkillPool: int<SkillId>[]
  PreferredIntent: SkillIntent
  DecisionTree: string
}

type AIEntityDefinition = {
  Key: string
  Name: string
  ArchetypeId: int<AiArchetypeId>
  Family: Family
  Skills: int<SkillId>[]
  DecisionTree: string
  Model: string
  StatOverrides: BaseStats voption
}

[<Struct>]
type MapEntityOverride = {
  StatMultiplier: float32 voption
  SkillRestrictions: int<SkillId>[] voption
  ExtraSkills: int<SkillId>[] voption
}

type MapEntityGroup = {
  Entities: string[]
  Weights: float32[] voption
  Overrides: HashMap<string, MapEntityOverride>
}


module Serialization =
  open JDeck
  open JDeck.Decode

  module BehaviorType =
    let decoder: Decoder<BehaviorType> =
      fun json -> decode {
        let! str = Required.string json

        match str with
        | "Patrol" -> return Patrol
        | "Aggressive" -> return Aggressive
        | "Defensive" -> return Defensive
        | "Supporter" -> return Supporter
        | "Ambusher" -> return Ambusher
        | "Turret" -> return Turret
        | "Passive" -> return Passive
        | other ->
          return!
            DecodeError.ofError(json.Clone(), $"Unknown BehaviorType: {other}")
            |> Error
      }

  module CueType =
    let decoder: Decoder<CueType> =
      fun json -> decode {
        let! str = Required.string json

        match str with
        | "Visual" -> return Visual
        | "Audio" -> return Audio
        | "Damage" -> return Damage
        | "Communication" -> return Communication
        | "Memory" -> return Memory
        | other ->
          return!
            DecodeError.ofError(json.Clone(), $"Unknown CueType: {other}")
            |> Error
      }

  module CueStrength =
    let decoder: Decoder<CueStrength> =
      fun json -> decode {
        let! str = Required.string json

        match str with
        | "Weak" -> return Weak
        | "Moderate" -> return Moderate
        | "Strong" -> return Strong
        | "Overwhelming" -> return Overwhelming
        | other ->
          return!
            DecodeError.ofError(json.Clone(), $"Unknown CueStrength: {other}")
            |> Error
      }

  module ResponseType =
    let decoder: Decoder<ResponseType> =
      fun json -> decode {
        let! str = Required.string json

        match str with
        | "Ignore" -> return Ignore
        | "Investigate" -> return Investigate
        | "Engage" -> return Engage
        | "Flee" -> return Flee
        | "Evade" -> return Evade
        | other ->
          return!
            DecodeError.ofError(json.Clone(), $"Unknown ResponseType: {other}")
            |> Error
      }

  module PerceptionConfig =
    let decoder: Decoder<PerceptionConfig> =
      fun json -> decode {
        let! visualRange =
          Required.Property.array ("VisualRange", Required.float) json
          |> Result.bind(fun arr ->
            match arr with
            | [| value; size |] -> float32(value * size) |> Ok
            | [| value |] -> float32 value |> Ok
            | _ ->
              let joined = String.Join(", ", arr)

              DecodeError.ofError(
                json.Clone(),
                $"VisualRange array must have either one or two float values: {joined}"
              )
              |> Error)

        and! fov = Required.Property.get ("Fov", Required.float) json

        and! memoryDurationSeconds =
          Required.Property.get ("MemoryDuration", Required.float) json

        and! leashDistance =
          Optional.Property.get ("LeashDistance", Required.float) json
          |> Result.map(Option.defaultValue 300.0)

        return {
          visualRange = float32 visualRange
          fov = float32 fov
          memoryDuration = TimeSpan.FromSeconds memoryDurationSeconds
          leashDistance = float32 leashDistance
        }
      }

  module CuePriority =
    let decoder: Decoder<CuePriority> =
      fun json -> decode {
        let! cueType = Required.Property.get ("CueType", CueType.decoder) json

        and! minStrength =
          Required.Property.get ("MinStrength", CueStrength.decoder) json

        and! priority = Required.Property.get ("Priority", Required.int) json

        and! response =
          Required.Property.get ("Response", ResponseType.decoder) json

        return {
          cueType = cueType
          minStrength = minStrength
          priority = priority
          response = response
        }
      }

  module AIArchetype =
    let decoder: Decoder<AIArchetype> =
      fun json -> decode {
        let! id = Required.Property.get ("Id", Required.int) json
        and! name = Required.Property.get ("Name", Required.string) json

        and! behaviorType =
          Required.Property.get ("BehaviorType", BehaviorType.decoder) json

        and! perceptionConfig =
          Required.Property.get
            ("PerceptionConfig", PerceptionConfig.decoder)
            json

        and! cuePriorities =
          Required.Property.array ("CuePriorities", CuePriority.decoder) json

        and! decisionIntervalSeconds =
          Required.Property.get ("DecisionInterval", Required.float) json

        and! baseStats =
          Required.Property.get
            ("BaseStats", Serialization.BaseStats.decoder)
            json

        return {
          id = UMX.tag id
          name = name
          behaviorType = behaviorType
          perceptionConfig = perceptionConfig
          cuePriorities = cuePriorities
          decisionInterval = TimeSpan.FromSeconds decisionIntervalSeconds
          baseStats = baseStats
        }
      }

  module AIFamilyConfig =
    let decoder: Decoder<AIFamilyConfig> =
      fun json -> decode {
        let! skillPoolInts =
          Required.Property.array ("SkillPool", Required.int) json

        and! preferredIntentStr =
          Required.Property.get ("PreferredIntent", Required.string) json

        and! decisionTree =
          Required.Property.get ("DecisionTree", Required.string) json

        let preferredIntent =
          match preferredIntentStr.ToLowerInvariant() with
          | "offensive" -> SkillIntent.Offensive
          | _ -> SkillIntent.Supportive

        let skillPool = skillPoolInts |> Array.map UMX.tag<SkillId>

        let statScaling = HashMap.empty<string, float32>

        return {
          StatScaling = statScaling
          SkillPool = skillPool
          PreferredIntent = preferredIntent
          DecisionTree = decisionTree
        }
      }

  module AIEntityDefinition =
    let decoder(key: string) : Decoder<AIEntityDefinition> =
      fun json -> decode {
        let! name = Required.Property.get ("Name", Required.string) json

        and! archetypeId =
          Required.Property.get ("ArchetypeId", Required.int) json

        and! familyStr = Required.Property.get ("Family", Required.string) json
        and! skillInts = Required.Property.array ("Skills", Required.int) json

        and! decisionTree =
          Required.Property.get ("DecisionTree", Required.string) json

        and! model = Required.Property.get ("Model", Required.string) json

        and! statOverrides =
          VOptional.Property.get
            ("StatOverrides",
             Pomo.Core.Domain.Entity.Serialization.BaseStats.decoder)
            json

        let family =
          match familyStr with
          | "Power" -> Family.Power
          | "Magic" -> Family.Magic
          | "Charm" -> Family.Charm
          | "Sense" -> Family.Sense
          | _ -> Family.Power

        let skills = skillInts |> Array.map UMX.tag<SkillId>

        return {
          Key = key
          Name = name
          ArchetypeId = UMX.tag archetypeId
          Family = family
          Skills = skills
          DecisionTree = decisionTree
          Model = model
          StatOverrides = statOverrides
        }
      }

  module MapEntityOverride =
    let decoder: Decoder<MapEntityOverride> =
      fun json -> decode {
        let! statMultiplierOpt =
          VOptional.Property.get ("StatMultiplier", Required.float) json

        and! skillRestrictionsOpt =
          VOptional.Property.array ("SkillRestrictions", Required.int) json

        and! extraSkillsOpt =
          VOptional.Property.array ("ExtraSkills", Required.int) json

        return {
          StatMultiplier = statMultiplierOpt |> ValueOption.map float32
          SkillRestrictions =
            skillRestrictionsOpt |> ValueOption.map(Array.map UMX.tag<SkillId>)
          ExtraSkills =
            extraSkillsOpt |> ValueOption.map(Array.map UMX.tag<SkillId>)
        }
      }

  module MapEntityGroup =
    let decoder: Decoder<MapEntityGroup> =
      fun json -> decode {
        let! entities =
          Required.Property.array ("Entities", Required.string) json

        and! weightsOpt =
          VOptional.Property.array ("Weights", Required.float) json

        return {
          Entities = entities
          Weights = weightsOpt |> ValueOption.map(Array.map float32)
          Overrides = HashMap.empty
        }
      }

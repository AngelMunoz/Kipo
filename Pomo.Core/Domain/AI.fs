namespace Pomo.Core.Domain.AI

open System
open FSharp.UMX
open Microsoft.Xna.Framework
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Entity
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

  // Memory
  memories: HashMap<Guid<EntityId>, MemoryEntry>
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
          Required.Property.get ("VisualRange", Required.float) json

        and! fov = Required.Property.get ("Fov", Required.float) json

        and! memoryDurationSeconds =
          Required.Property.get ("MemoryDuration", Required.float) json

        return {
          visualRange = float32 visualRange
          fov = float32 fov
          memoryDuration = TimeSpan.FromSeconds memoryDurationSeconds
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

        return {
          id = UMX.tag id
          name = name
          behaviorType = behaviorType
          perceptionConfig = perceptionConfig
          cuePriorities = cuePriorities
          decisionInterval = TimeSpan.FromSeconds decisionIntervalSeconds
        }
      }

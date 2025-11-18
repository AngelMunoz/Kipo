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

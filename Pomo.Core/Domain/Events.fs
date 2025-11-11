namespace Pomo.Core.Domain.Events

open System
open System.Collections.Generic
open Microsoft.Xna.Framework

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Projectile
open Pomo.Core.Domain.RawInput
open Pomo.Core.Domain.Skill

[<Struct>]
type MovementState =
  | Idle
  | MovingTo of targetPosition: Vector2

[<Struct>]
type Selection =
  | SelectedEntity of entity: Guid<EntityId>
  | SelectedPosition of position: Vector2

[<RequireQualifiedAccess>]
module SystemCommunications =
  [<Struct>]
  type ShowNotification = { Message: string; Position: Vector2 }

  [<Struct>]
  type SlotActivated = {
    Slot: GameAction
    CasterId: Guid<EntityId>
  }

  [<Struct>]
  type AbilityIntent = {
    Caster: Guid<EntityId>
    SkillId: int<SkillId>
    Target: Guid<EntityId> voption
  }

  [<Struct>]
  type EffectApplicationIntent = {
    SourceEntity: Guid<EntityId>
    TargetEntity: Guid<EntityId>
    Effect: Effect
  }

  [<Struct>]
  type EffectDamageIntent = {
    SourceEntity: Guid<EntityId>
    TargetEntity: Guid<EntityId>
    Effect: Effect
  }

  [<Struct>]
  type AttackIntent = {
    Attacker: Guid<EntityId>
    Target: Guid<EntityId>
  }

  [<Struct>]
  type SetMovementTarget = {
    EntityId: Guid<EntityId>
    Target: Vector2
  }

  [<Struct>]
  type TargetSelected = {
    Selector: Guid<EntityId>
    Selection: Selection
  }

  [<Struct>]
  type DamageDealt = { Target: Guid<EntityId>; Amount: int }

  [<Struct>]
  type EntityDied = { Target: Guid<EntityId> }

  [<Struct>]
  type ProjectileImpacted = {
    ProjectileId: Guid<EntityId>
    CasterId: Guid<EntityId>
    TargetId: Guid<EntityId>
    SkillId: int<SkillId>
  }

// --- State Change Events ---

type EntityLifecycleEvents =
  | Created of created: EntitySnapshot
  | Removed of removed: Guid<EntityId>

type InputEvents =
  | RawStateChanged of rawIChanged: struct (Guid<EntityId> * RawInputState)
  | MapChanged of iMapChanged: struct (Guid<EntityId> * InputMap)
  | GameActionStatesChanged of
    gAChanged: struct (Guid<EntityId> * HashMap<GameAction, InputActionState>)
  | QuickSlotsChanged of
    qsChanged: struct (Guid<EntityId> * HashMap<GameAction, int<SkillId>>)

type PhysicsEvents =
  | PositionChanged of posChanged: struct (Guid<EntityId> * Vector2)
  | VelocityChanged of velChanged: struct (Guid<EntityId> * Vector2)
  | MovementStateChanged of
    mStateChanged: struct (Guid<EntityId> * MovementState)

type CombatEvents =
  | ResourcesChanged of resChanged: struct (Guid<EntityId> * Resource)
  | FactionsChanged of facChanged: struct (Guid<EntityId> * Faction HashSet)
  | BaseStatsChanged of statsChanged: struct (Guid<EntityId> * BaseStats)
  | StatsChanged of entity: Guid<EntityId> * newStats: DerivedStats
  | EffectApplied of effectApplied: struct (Guid<EntityId> * ActiveEffect)
  | EffectExpired of effectExpired: struct (Guid<EntityId> * Guid<EffectId>)
  | EffectRefreshed of effectRefreshed: struct (Guid<EntityId> * Guid<EffectId>)
  | EffectStackChanged of
    effectStackChanged: struct (Guid<EntityId> * Guid<EffectId> * int)
  | CooldownsChanged of
    cdChanged: struct (Guid<EntityId> * HashMap<int<SkillId>, TimeSpan>)
  | InCombatTimerRefreshed of entityId: Guid<EntityId>

[<Struct>]
type StateChangeEvent =
  | EntityLifecycle of entityLifeCycle: EntityLifecycleEvents
  | Input of input: InputEvents
  | Physics of physics: PhysicsEvents
  | Combat of combat: CombatEvents
  // Uncategorized
  | CreateProjectile of projParams: struct (Guid<EntityId> * LiveProjectile)

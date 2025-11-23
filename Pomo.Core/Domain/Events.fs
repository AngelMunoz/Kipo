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
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.AI
open Pomo.Core.Domain.Core

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
  [<RequireQualifiedAccess>]
  type SpawnType =
    | Player of playerIndex: int
    | Enemy of archetypeId: int<AiArchetypeId>

  [<Struct>]
  type SpawnEntityIntent = {
    EntityId: Guid<EntityId>
    Type: SpawnType
    Position: Vector2
  }

  [<Struct>]
  type ShowNotification = { Message: string; Position: Vector2 }

  [<Struct>]
  type SlotActivated = {
    Slot: GameAction
    CasterId: Guid<EntityId>
  }

  [<Struct>]
  type SkillTarget =
    | TargetSelf
    | TargetEntity of entity: Guid<EntityId>
    | TargetPosition of position: Vector2

  [<Struct>]
  type AbilityIntent = {
    Caster: Guid<EntityId>
    SkillId: int<SkillId>
    Target: SkillTarget
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
  type EffectResourceIntent = {
    SourceEntity: Guid<EntityId>
    TargetEntity: Guid<EntityId>
    Effect: Effect
    ActiveEffectId: Guid<EffectId>
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
  type ResourceRestored = {
    Target: Guid<EntityId>
    ResourceType: ResourceType
    Amount: int
  }

  [<Struct>]
  type EntityDied = { Target: Guid<EntityId> }

  [<Struct>]
  type ProjectileImpacted = {
    ProjectileId: Guid<EntityId>
    CasterId: Guid<EntityId>
    TargetId: Guid<EntityId>
    SkillId: int<SkillId>
    RemainingJumps: int voption
  }

  [<Struct>]
  type PickUpItemIntent = {
    Picker: Guid<EntityId>
    Item: ItemInstance
  }

  [<Struct>]
  type EquipItemIntent = {
    EntityId: Guid<EntityId>
    ItemInstanceId: Guid<ItemInstanceId>
    Slot: Slot
  }

  [<Struct>]
  type UnequipItemIntent = { EntityId: Guid<EntityId>; Slot: Slot }

  [<Struct>]
  type DropItemIntent = {
    EntityId: Guid<EntityId>
    ItemInstanceId: Guid<ItemInstanceId>
    Amount: int voption // ValueNone or higher than instance's usage left means drop all
  }

  type UseItemIntent = {
    EntityId: Guid<EntityId>
    ItemInstanceId: Guid<ItemInstanceId>
  }

  [<Struct>]
  type CollisionEvents =
    | EntityCollision of entity: struct (Guid<EntityId> * Guid<EntityId>)
    | MapObjectCollision of
      object: struct (Guid<EntityId> * MapObject * Vector2)

// --- State Change Events ---

type EntityLifecycleEvents =
  | Spawning of
    spawning: struct (Guid<EntityId> * SystemCommunications.SpawnType * Vector2)
  | Created of created: EntitySnapshot
  | Removed of removed: Guid<EntityId>

type InputEvents =
  | RawStateChanged of rawIChanged: struct (Guid<EntityId> * RawInputState)
  | MapChanged of iMapChanged: struct (Guid<EntityId> * InputMap)
  | GameActionStatesChanged of
    gAChanged: struct (Guid<EntityId> * HashMap<GameAction, InputActionState>)
  | ActionSetsChanged of
    asChanged:
      struct (Guid<EntityId> * HashMap<int, HashMap<GameAction, SlotProcessing>>)
  | ActiveActionSetChanged of aasChanged: struct (Guid<EntityId> * int)

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
  | PendingSkillCastSet of
    entityId: Guid<EntityId> *
    skillId: int<SkillId> *
    target: SystemCommunications.SkillTarget
  | PendingSkillCastCleared of entityId: Guid<EntityId>

type InventoryEvents =
  | ItemInstanceCreated of itemInstance: ItemInstance
  | ItemInstanceRemoved of itemInstanceId: Guid<ItemInstanceId>
  | UpdateItemInstance of itemInstance: ItemInstance
  | ItemAddedToInventory of
    itemAdded: struct (Guid<EntityId> * Guid<ItemInstanceId>)
  | ItemRemovedFromInventory of
    itemRemoved: struct (Guid<EntityId> * Guid<ItemInstanceId>)
  | ItemEquipped of
    itemEquipped: struct (Guid<EntityId> * Slot * Guid<ItemInstanceId>)
  | ItemUnequipped of
    itemUnequipped: struct (Guid<EntityId> * Slot * Guid<ItemInstanceId>)

type AIStateChange =
  | ControllerUpdated of
    struct (Guid<EntityId> * Pomo.Core.Domain.AI.AIController)

[<Struct>]
type StateChangeEvent =
  | EntityLifecycle of entityLifeCycle: EntityLifecycleEvents
  | Input of input: InputEvents
  | Physics of physics: PhysicsEvents
  | Combat of combat: CombatEvents
  | Inventory of inventory: InventoryEvents
  | AI of ai: AIStateChange
  // Uncategorized
  | CreateProjectile of
    projParams: struct (Guid<EntityId> * LiveProjectile * Vector2 voption)

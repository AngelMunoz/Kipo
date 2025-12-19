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
  | MovingAlongPath of path: Vector2 list

[<Struct>]
type Selection =
  | SelectedEntity of entity: Guid<EntityId>
  | SelectedPosition of position: Vector2

[<RequireQualifiedAccess>]
module SystemCommunications =
  [<Struct>]
  type FactionSpawnInfo = {
    ArchetypeId: int<AiArchetypeId>
    EntityDefinitionKey: string voption
    MapOverride: MapEntityOverride voption
    Faction: Faction voption
    SpawnZoneName: string voption
  }

  [<RequireQualifiedAccess; Struct>]
  type SpawnType =
    | Player of playerIndex: int
    | Faction of FactionSpawnInfo

  [<Struct>]
  type SpawnEntityIntent = {
    EntityId: Guid<EntityId>
    ScenarioId: Guid<ScenarioId>
    Type: SpawnType
    Position: Vector2
  }

  [<Struct>]
  type SpawnZoneData = {
    ZoneName: string
    ScenarioId: Guid<ScenarioId>
    MaxSpawns: int
    SpawnInfo: FactionSpawnInfo
    SpawnPositions: Vector2[]
  }

  [<Struct>]
  type RegisterSpawnZones = {
    ScenarioId: Guid<ScenarioId>
    MaxEnemies: int
    Zones: SpawnZoneData[]
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
    | TargetDirection of position: Vector2

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
  type PortalTravel = {
    EntityId: Guid<EntityId>
    TargetMap: string
    TargetSpawn: string
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
  type EntityDied = {
    EntityId: Guid<EntityId>
    ScenarioId: Guid<ScenarioId>
  }

  [<Struct>]
  type ProjectileImpacted = {
    ProjectileId: Guid<EntityId>
    CasterId: Guid<EntityId>
    ImpactPosition: Vector2
    TargetEntity: Guid<EntityId> voption
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

  [<Struct>]
  type SceneTransition = { Scene: Pomo.Core.Domain.Scenes.Scene }

// --- State Change Events ---

/// Bundle of all components for spawning an entity atomically.
/// Reduces 17+ individual events to a single event publication.
[<Struct>]
type EntitySpawnBundle = {
  Snapshot: EntitySnapshot
  Resources: Resource voption
  Factions: Faction HashSet voption
  BaseStats: BaseStats voption
  ModelConfig: string voption
  InputMap: InputMap voption
  ActionSets: HashMap<int, HashMap<GameAction, SlotProcessing>> voption
  ActiveActionSet: int voption
  /// Item instances to create, with their instance data
  InventoryItems: ItemInstance[] voption
  /// Slots to equip after items are created
  EquippedSlots: struct (Slot * Guid<ItemInstanceId>)[] voption
  AIController: AIController voption
}

type EntityLifecycleEvents =
  | Spawning of
    spawning:
      struct (Guid<EntityId> *
      Guid<ScenarioId> *
      SystemCommunications.SpawnType *
      Vector2)
  | Created of created: EntitySnapshot
  | Removed of removed: Guid<EntityId>
  /// Bundled spawn event - applies all components atomically
  | EntitySpawned of bundle: EntitySpawnBundle

type InputEvents =
  | RawStateChanged of rawIChanged: struct (Guid<EntityId> * RawInputState)
  | GameActionStatesChanged of
    gAChanged: struct (Guid<EntityId> * HashMap<GameAction, InputActionState>)
  | ActiveActionSetChanged of aasChanged: struct (Guid<EntityId> * int)

type PhysicsEvents =
  | MovementStateChanged of
    mStateChanged: struct (Guid<EntityId> * MovementState)




[<Struct>]
type StateChangeEvent =
  | EntityLifecycle of entityLifeCycle: EntityLifecycleEvents
  | Input of input: InputEvents
  | Physics of physics: PhysicsEvents
// Uncategorized

// --- Intent Events (user actions and commands) ---
[<Struct>]
type IntentEvent =
  | Ability of ability: SystemCommunications.AbilityIntent
  | Attack of attack: SystemCommunications.AttackIntent
  | EffectApplication of effectApp: SystemCommunications.EffectApplicationIntent
  | EffectDamage of effectDmg: SystemCommunications.EffectDamageIntent
  | EffectResource of effectRes: SystemCommunications.EffectResourceIntent
  | MovementTarget of movement: SystemCommunications.SetMovementTarget
  | TargetSelection of target: SystemCommunications.TargetSelected
  | Portal of portal: SystemCommunications.PortalTravel
  | SlotActivated of slot: SystemCommunications.SlotActivated

// --- Item Intent Events ---
[<Struct>]
type ItemIntentEvent =
  | PickUp of pickUp: SystemCommunications.PickUpItemIntent
  | Equip of equip: SystemCommunications.EquipItemIntent
  | Unequip of unequip: SystemCommunications.UnequipItemIntent
  | Drop of drop: SystemCommunications.DropItemIntent
  | Use of useItem: SystemCommunications.UseItemIntent

// --- Notification Events (UI feedback) ---
[<Struct>]
type NotificationEvent =
  | ShowMessage of message: SystemCommunications.ShowNotification
  | DamageDealt of damage: SystemCommunications.DamageDealt
  | ResourceRestored of restored: SystemCommunications.ResourceRestored

// --- Lifecycle Events (entity state changes for systems) ---
[<Struct>]
type LifecycleEvent =
  | EntityDied of died: SystemCommunications.EntityDied
  | ProjectileImpacted of impact: SystemCommunications.ProjectileImpacted

// --- Spawning Events ---
[<Struct>]
type SpawningEvent =
  | SpawnEntity of spawn: SystemCommunications.SpawnEntityIntent
  | RegisterZones of zones: SystemCommunications.RegisterSpawnZones

// --- Collision Events (already exists, just rename for consistency) ---
type CollisionEvent = SystemCommunications.CollisionEvents

// --- Scene Events ---
[<Struct>]
type SceneEvent = Transition of transition: SystemCommunications.SceneTransition

// === Top-Level GameEvent ===
[<Struct>]
type GameEvent =
  | State of state: StateChangeEvent
  | Intent of intent: IntentEvent
  | ItemIntent of itemIntent: ItemIntentEvent
  | Notification of notification: NotificationEvent
  | Lifecycle of lifecycle: LifecycleEvent
  | Spawn of spawning: SpawningEvent
  | Collision of collision: CollisionEvent
  | Scene of scene: SceneEvent

namespace Pomo.Core

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Content
open FSharp.Data.Adaptive
open FSharp.UMX

open Pomo.Core.EventBus
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Camera
open Pomo.Core.Stores
open Pomo.Core.Projections
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.Projectile
open Pomo.Core.Domain.UI


module Environment =

  type IUIService =
    abstract member IsMouseOverUI: bool aval
    abstract member SetMouseOverUI: bool -> unit

  type TargetingService =
    inherit CoreEventListener
    abstract member TargetingMode: Targeting voption aval with get

  type IPhysicsWriteService =
    abstract UpdatePosition: Guid<EntityId> * WorldPosition -> unit
    abstract UpdateVelocity: Guid<EntityId> * Vector2 -> unit
    abstract UpdateRotation: Guid<EntityId> * float32 -> unit

  type IEntityLifecycleService =
    abstract RemoveEntity: Guid<EntityId> -> unit
    abstract ApplyEntitySpawnBundle: EntitySpawnBundle -> unit

    abstract CreateProjectile:
      Guid<EntityId> * LiveProjectile * WorldPosition voption -> unit

  type IMovementWriteService =
    abstract UpdateMovementState: Guid<EntityId> * Events.MovementState -> unit

  type IInputWriteService =
    abstract UpdateRawInputState:
      Guid<EntityId> * RawInput.RawInputState -> unit

    abstract UpdateGameActionStates:
      Guid<EntityId> * HashMap<Action.GameAction, Action.InputActionState> ->
        unit

    abstract UpdateActiveActionSet: Guid<EntityId> * int -> unit

  type ICombatWriteService =
    abstract UpdateResources: Guid<EntityId> * Entity.Resource -> unit

    abstract UpdateCooldowns:
      Guid<EntityId> * HashMap<int<SkillId>, TimeSpan> -> unit

    abstract UpdateInCombatTimer: Guid<EntityId> -> unit

    abstract SetPendingSkillCast:
      Guid<EntityId> * int<SkillId> * SystemCommunications.SkillTarget -> unit

    abstract ClearPendingSkillCast: Guid<EntityId> -> unit

    abstract UpdateActiveCharge: Guid<EntityId> * ActiveCharge -> unit
    abstract RemoveActiveCharge: Guid<EntityId> -> unit

  type IEffectsWriteService =
    abstract ApplyEffect: Guid<EntityId> * Skill.ActiveEffect -> unit
    abstract ExpireEffect: Guid<EntityId> * Guid<EffectId> -> unit
    abstract RefreshEffect: Guid<EntityId> * Guid<EffectId> -> unit
    abstract ChangeEffectStack: Guid<EntityId> * Guid<EffectId> * int -> unit

  type IInventoryWriteService =
    abstract CreateItemInstance: ItemInstance -> unit
    abstract UpdateItemInstance: ItemInstance -> unit
    abstract AddItemToInventory: Guid<EntityId> * Guid<ItemInstanceId> -> unit
    abstract EquipItem: Guid<EntityId> * Slot * Guid<ItemInstanceId> -> unit
    abstract UnequipItem: Guid<EntityId> * Slot -> unit

  type IAnimationWriteService =
    abstract UpdateActiveAnimations:
      Guid<EntityId> * Animation.AnimationState[] -> unit

    abstract UpdatePose: Guid<EntityId> * Dictionary<string, Matrix> -> unit

    abstract RemoveAnimationState: Guid<EntityId> -> unit
    abstract UpdateModelConfig: Guid<EntityId> * string -> unit

  type IAIWriteService =
    abstract UpdateAIController: Guid<EntityId> * AI.AIController -> unit

  type IOrbitalWriteService =
    abstract UpdateActiveOrbital: Guid<EntityId> * Orbital.ActiveOrbital -> unit
    abstract RemoveActiveOrbital: Guid<EntityId> -> unit

  type INotificationWriteService =
    abstract AddNotification: WorldText -> unit
    abstract SetNotifications: WorldText[] -> unit

  type IStateWriteService =
    inherit IDisposable
    inherit IPhysicsWriteService
    inherit IEntityLifecycleService
    inherit IMovementWriteService
    inherit IInputWriteService
    inherit ICombatWriteService
    inherit IEffectsWriteService
    inherit IInventoryWriteService
    inherit IAnimationWriteService
    inherit IAIWriteService
    inherit IOrbitalWriteService
    inherit INotificationWriteService
    abstract FlushWrites: unit -> unit

  type CoreServices =
    abstract EventBus: EventBus
    abstract World: World
    abstract StateWrite: IStateWriteService
    abstract Random: Random
    abstract UIService: IUIService
    abstract HUDService: IHUDService

  type StoreServices =
    abstract SkillStore: SkillStore
    abstract ItemStore: ItemStore
    abstract MapStore: MapStore
    abstract AIArchetypeStore: AIArchetypeStore
    abstract AIFamilyStore: AIFamilyStore
    abstract AIEntityStore: AIEntityStore
    abstract DecisionTreeStore: DecisionTreeStore
    abstract ModelStore: ModelStore
    abstract AnimationStore: AnimationStore
    abstract ParticleStore: ParticleStore

  type GameplayServices =
    abstract Projections: ProjectionService
    abstract TargetingService: TargetingService
    abstract CameraService: CameraService

  type ListenerServices =
    abstract EffectApplication: CoreEventListener
    abstract ActionHandler: CoreEventListener
    abstract NavigationService: CoreEventListener
    abstract InventoryService: CoreEventListener
    abstract EquipmentService: CoreEventListener

  type MonoGameServices =
    abstract GraphicsDevice: GraphicsDevice
    abstract Content: ContentManager

  [<AutoOpen>]
  module Patterns =
    let inline (|Core|)(env: #CoreServices) = env :> CoreServices
    let inline (|Stores|)(env: #StoreServices) = env :> StoreServices
    let inline (|Gameplay|)(env: #GameplayServices) = env :> GameplayServices
    let inline (|Listeners|)(env: #ListenerServices) = env :> ListenerServices
    let inline (|MonoGame|)(env: #MonoGameServices) = env :> MonoGameServices

  type PomoSystem =
    abstract member Update: GameTime -> unit
    abstract member Draw: GameTime -> unit
    abstract member Enabled: bool with get, set
    abstract member UpdateOrder: int with get, set
    abstract member DrawOrder: int with get, set

  type PomoEnvironment =
    abstract member CoreServices: CoreServices
    abstract member StoreServices: StoreServices
    abstract member GameplayServices: GameplayServices
    abstract member ListenerServices: ListenerServices
    abstract member MonoGameServices: MonoGameServices

namespace Pomo.Core

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Content
open FSharp.Data.Adaptive

open Pomo.Core.EventBus
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Camera
open Pomo.Core.Stores
open Pomo.Core.Projections
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Core


module Environment =

  type IUIService =
    abstract member IsMouseOverUI: bool aval
    abstract member SetMouseOverUI: bool -> unit

  type TargetingService =
    inherit CoreEventListener
    abstract member TargetingMode: Targeting voption aval with get

  type CoreServices =
    abstract EventBus: EventBus
    abstract World: World
    abstract Random: Random
    abstract UIService: IUIService

  type StoreServices =
    abstract SkillStore: SkillStore
    abstract ItemStore: ItemStore
    abstract MapStore: MapStore
    abstract AIArchetypeStore: AIArchetypeStore
    abstract AIFamilyStore: AIFamilyStore
    abstract AIEntityStore: AIEntityStore
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

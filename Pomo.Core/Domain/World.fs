namespace Pomo.Core.Domain

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open System.Collections.Generic
open System.Collections.Concurrent
open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.AI
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.Animation
open Pomo.Core.Domain.Particles


module World =
  open Pomo.Core.Domain.Events
  open RawInput
  open Action
  open Core
  open System.Collections.Generic

  [<Struct>]
  type Scenario = {
    Id: Guid<ScenarioId>
    Map: MapDefinition
    BlockMap: BlockMap.BlockMapDefinition voption
  }

  [<Struct>]
  type Time = {
    Delta: TimeSpan
    TotalGameTime: TimeSpan
    Previous: TimeSpan
  }

  [<Struct>]
  type ActiveCharge = {
    SkillId: int<SkillId>
    Target: SystemCommunications.SkillTarget
    StartTime: TimeSpan
    Duration: TimeSpan
  }

  /// World-anchored text (notifications, damage numbers, etc.)
  [<Struct>]
  type WorldText = {
    Text: string
    Type: SystemCommunications.NotificationType
    Position: WorldPosition
    Velocity: Vector2
    Life: float32
    MaxLife: float32
  }

  type MutableWorld = {
    Time: Time cval
    RawInputStates: cmap<Guid<EntityId>, RawInputState>
    InputMaps: cmap<Guid<EntityId>, InputMap>
    GameActionStates:
      cmap<Guid<EntityId>, HashMap<GameAction, InputActionState>>
    ActionSets:
      cmap<Guid<EntityId>, HashMap<int, HashMap<GameAction, SlotProcessing>>>
    ActiveActionSets: cmap<Guid<EntityId>, int>
    // entity components
    EntityExists: HashSet<Guid<EntityId>>
    Positions: Dictionary<Guid<EntityId>, WorldPosition>
    Velocities: Dictionary<Guid<EntityId>, Vector2>
    MovementStates: cmap<Guid<EntityId>, MovementState>
    Resources: cmap<Guid<EntityId>, Entity.Resource>
    Factions: cmap<Guid<EntityId>, FSharp.Data.Adaptive.HashSet<Entity.Faction>>
    BaseStats: cmap<Guid<EntityId>, Entity.BaseStats>
    DerivedStats: cmap<Guid<EntityId>, Entity.DerivedStats>
    ActiveEffects: cmap<Guid<EntityId>, Skill.ActiveEffect IndexList>
    AbilityCooldowns: cmap<Guid<EntityId>, HashMap<int<SkillId>, TimeSpan>>
    LiveProjectiles: cmap<Guid<EntityId>, Projectile.LiveProjectile>
    InCombatUntil: cmap<Guid<EntityId>, TimeSpan>
    PendingSkillCast:
      cmap<
        Guid<EntityId>,
        struct (int<SkillId> * SystemCommunications.SkillTarget)
       >
    ItemInstances: ConcurrentDictionary<Guid<ItemInstanceId>, ItemInstance>
    EntityInventories:
      cmap<Guid<EntityId>, FSharp.Data.Adaptive.HashSet<Guid<ItemInstanceId>>>
    EquippedItems: cmap<Guid<EntityId>, HashMap<Slot, Guid<ItemInstanceId>>>
    AIControllers: cmap<Guid<EntityId>, AI.AIController>
    SpawningEntities:
      cmap<
        Guid<EntityId>,
        struct (SystemCommunications.SpawnType * WorldPosition * TimeSpan)
       >
    // Scenario State
    Scenarios: cmap<Guid<ScenarioId>, Scenario>
    EntityScenario: cmap<Guid<EntityId>, Guid<ScenarioId>>
    // 3D Integration
    Rotations: Dictionary<Guid<EntityId>, float32>
    ModelConfigId: cmap<Guid<EntityId>, string>
    // Animation
    Poses: Dictionary<Guid<EntityId>, Dictionary<string, Matrix>>
    ActiveAnimations: Dictionary<Guid<EntityId>, AnimationState[]>
    // Orbitals
    ActiveOrbitals: cmap<Guid<EntityId>, Orbital.ActiveOrbital>
    ActiveCharges: cmap<Guid<EntityId>, ActiveCharge>
    // VFX
    VisualEffects: ResizeArray<VisualEffect>
    // World Text (notifications, damage numbers, etc.)
    Notifications: ResizeArray<WorldText>
  }

  type World =
    abstract Rng: Random
    abstract Time: Time aval
    abstract RawInputStates: amap<Guid<EntityId>, RawInputState>
    abstract InputMaps: amap<Guid<EntityId>, InputMap>

    abstract GameActionStates:
      amap<Guid<EntityId>, HashMap<GameAction, InputActionState>>

    abstract ActionSets:
      amap<Guid<EntityId>, HashMap<int, HashMap<GameAction, SlotProcessing>>>

    abstract ActiveActionSets: amap<Guid<EntityId>, int>
    // entity components
    abstract EntityExists: IReadOnlySet<Guid<EntityId>>
    abstract Positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>
    abstract Velocities: IReadOnlyDictionary<Guid<EntityId>, Vector2>
    abstract MovementStates: amap<Guid<EntityId>, MovementState>
    abstract Resources: amap<Guid<EntityId>, Entity.Resource>

    abstract Factions:
      amap<Guid<EntityId>, FSharp.Data.Adaptive.HashSet<Entity.Faction>>

    abstract BaseStats: amap<Guid<EntityId>, Entity.BaseStats>
    abstract DerivedStats: amap<Guid<EntityId>, Entity.DerivedStats>
    abstract ActiveEffects: amap<Guid<EntityId>, Skill.ActiveEffect IndexList>

    abstract AbilityCooldowns:
      amap<Guid<EntityId>, HashMap<int<SkillId>, TimeSpan>>

    abstract LiveProjectiles: amap<Guid<EntityId>, Projectile.LiveProjectile>
    abstract InCombatUntil: amap<Guid<EntityId>, TimeSpan>

    abstract PendingSkillCast:
      amap<
        Guid<EntityId>,
        struct (int<SkillId> * SystemCommunications.SkillTarget)
       >

    abstract ItemInstances:
      IReadOnlyDictionary<Guid<ItemInstanceId>, ItemInstance>

    abstract EntityInventories:
      amap<Guid<EntityId>, FSharp.Data.Adaptive.HashSet<Guid<ItemInstanceId>>>

    abstract EquippedItems:
      amap<Guid<EntityId>, HashMap<Slot, Guid<ItemInstanceId>>>

    abstract AIControllers: amap<Guid<EntityId>, AI.AIController>

    abstract SpawningEntities:
      amap<
        Guid<EntityId>,
        struct (SystemCommunications.SpawnType * WorldPosition * TimeSpan)
       >

    abstract Scenarios: amap<Guid<ScenarioId>, Scenario>
    abstract EntityScenario: amap<Guid<EntityId>, Guid<ScenarioId>>
    abstract Rotations: IReadOnlyDictionary<Guid<EntityId>, float32>
    abstract ModelConfigId: amap<Guid<EntityId>, string>

    abstract Poses:
      IReadOnlyDictionary<Guid<EntityId>, Dictionary<string, Matrix>>

    abstract ActiveAnimations:
      IReadOnlyDictionary<Guid<EntityId>, AnimationState[]>

    // Orbitals
    abstract ActiveOrbitals: amap<Guid<EntityId>, Orbital.ActiveOrbital>
    abstract ActiveCharges: amap<Guid<EntityId>, ActiveCharge>

    // VFX
    abstract VisualEffects: ResizeArray<VisualEffect>
    // World Text (notifications, damage numbers, etc.)
    abstract Notifications: IReadOnlyList<WorldText>


  let create(rng: Random) =
    let mutableWorld: MutableWorld = {
      Time =
        cval {
          Delta = TimeSpan.Zero
          TotalGameTime = TimeSpan.Zero
          Previous = TimeSpan.Zero
        }
      EntityExists = HashSet()
      Positions = Dictionary()
      Velocities = Dictionary()
      MovementStates = cmap()
      RawInputStates = cmap()
      InputMaps = cmap()
      GameActionStates = cmap()
      ActionSets = cmap()
      ActiveActionSets = cmap()
      Resources = cmap()
      Factions = cmap()
      BaseStats = cmap()
      DerivedStats = cmap()
      ActiveEffects = cmap()
      AbilityCooldowns = cmap()
      LiveProjectiles = cmap()
      InCombatUntil = cmap()
      PendingSkillCast = cmap()
      ItemInstances = ConcurrentDictionary()
      EntityInventories = cmap()
      EquippedItems = cmap()
      AIControllers = cmap()
      SpawningEntities = cmap()
      Scenarios = cmap()
      EntityScenario = cmap()
      Rotations = Dictionary()
      ModelConfigId = cmap()
      Poses = Dictionary()
      ActiveAnimations = Dictionary()
      ActiveOrbitals = cmap()
      ActiveCharges = cmap()
      VisualEffects = ResizeArray()
      Notifications = ResizeArray()
    }

    let worldView =
      { new World with
          member _.Rng = rng
          member _.Time = mutableWorld.Time
          member _.EntityExists = mutableWorld.EntityExists
          member _.Positions = mutableWorld.Positions
          member _.Velocities = mutableWorld.Velocities
          member _.MovementStates = mutableWorld.MovementStates
          member _.RawInputStates = mutableWorld.RawInputStates
          member _.InputMaps = mutableWorld.InputMaps
          member _.GameActionStates = mutableWorld.GameActionStates
          member _.ActionSets = mutableWorld.ActionSets
          member _.ActiveActionSets = mutableWorld.ActiveActionSets
          member _.Resources = mutableWorld.Resources
          member _.Factions = mutableWorld.Factions
          member _.BaseStats = mutableWorld.BaseStats
          member _.DerivedStats = mutableWorld.DerivedStats
          member _.ActiveEffects = mutableWorld.ActiveEffects
          member _.AbilityCooldowns = mutableWorld.AbilityCooldowns
          member _.LiveProjectiles = mutableWorld.LiveProjectiles
          member _.InCombatUntil = mutableWorld.InCombatUntil
          member _.PendingSkillCast = mutableWorld.PendingSkillCast
          member _.ItemInstances = mutableWorld.ItemInstances
          member _.EntityInventories = mutableWorld.EntityInventories
          member _.EquippedItems = mutableWorld.EquippedItems
          member _.AIControllers = mutableWorld.AIControllers
          member _.SpawningEntities = mutableWorld.SpawningEntities
          member _.Scenarios = mutableWorld.Scenarios
          member _.EntityScenario = mutableWorld.EntityScenario
          member _.Rotations = mutableWorld.Rotations
          member _.ModelConfigId = mutableWorld.ModelConfigId

          member _.Poses = mutableWorld.Poses

          member _.ActiveAnimations = mutableWorld.ActiveAnimations
          member _.ActiveOrbitals = mutableWorld.ActiveOrbitals
          member _.ActiveCharges = mutableWorld.ActiveCharges
          member _.VisualEffects = mutableWorld.VisualEffects
          member _.Notifications = mutableWorld.Notifications
      }

    struct (mutableWorld, worldView)

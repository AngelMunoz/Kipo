namespace Pomo.Core.Domain

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open System.Collections.Concurrent
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.AI


module World =
  open Pomo.Core.Domain.Events
  open RawInput
  open Action
  open Core

  [<Struct>]
  type Time = {
    Delta: TimeSpan
    TotalGameTime: TimeSpan
    Previous: TimeSpan
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
    Positions: cmap<Guid<EntityId>, Vector2>
    Velocities: cmap<Guid<EntityId>, Vector2>
    MovementStates: cmap<Guid<EntityId>, MovementState>
    Resources: cmap<Guid<EntityId>, Entity.Resource>
    Factions: cmap<Guid<EntityId>, Entity.Faction HashSet>
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
    EntityInventories: cmap<Guid<EntityId>, HashSet<Guid<ItemInstanceId>>>
    EquippedItems: cmap<Guid<EntityId>, HashMap<Slot, Guid<ItemInstanceId>>>
    AIControllers: cmap<Guid<EntityId>, AI.AIController>
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
    abstract Positions: amap<Guid<EntityId>, Vector2>
    abstract Velocities: amap<Guid<EntityId>, Vector2>
    abstract MovementStates: amap<Guid<EntityId>, MovementState>
    abstract Resources: amap<Guid<EntityId>, Entity.Resource>
    abstract Factions: amap<Guid<EntityId>, Entity.Faction HashSet>
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
      Collections.Generic.IReadOnlyDictionary<Guid<ItemInstanceId>, ItemInstance>

    abstract EntityInventories:
      amap<Guid<EntityId>, HashSet<Guid<ItemInstanceId>>>

    abstract EquippedItems:
      amap<Guid<EntityId>, HashMap<Slot, Guid<ItemInstanceId>>>

    abstract AIControllers: amap<Guid<EntityId>, AI.AIController>


  let create(rng: Random) =
    let mutableWorld: MutableWorld = {
      Time =
        cval {
          Delta = TimeSpan.Zero
          TotalGameTime = TimeSpan.Zero
          Previous = TimeSpan.Zero
        }
      Positions = cmap()
      Velocities = cmap()
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
    }

    let worldView =
      { new World with
          member _.Rng = rng
          member _.Time = mutableWorld.Time
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
      }

    struct (mutableWorld, worldView)

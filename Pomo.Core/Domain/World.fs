namespace Pomo.Core.Domain

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open System.Collections.Concurrent
open Pomo.Core.Domain.Units


module World =
  open Pomo.Core.Domain.Events
  open RawInput
  open Action

  [<Struct>]
  type Time = {
    Delta: TimeSpan
    TotalGameTime: TimeSpan
    Previous: TimeSpan
  }

  [<Struct>]
  type MutableWorld = {
    Time: Time cval
    RawInputStates: cmap<Guid<EntityId>, RawInputState>
    InputMaps: cmap<Guid<EntityId>, InputMap>
    GameActionStates:
      cmap<Guid<EntityId>, HashMap<GameAction, InputActionState>>
    QuickSlots: cmap<Guid<EntityId>, HashMap<GameAction, int<SkillId>>>
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
  }

  type World =
    abstract Rng: Random
    abstract Time: Time aval
    abstract RawInputStates: amap<Guid<EntityId>, RawInputState>
    abstract InputMaps: amap<Guid<EntityId>, InputMap>

    abstract GameActionStates:
      amap<Guid<EntityId>, HashMap<GameAction, InputActionState>>

    abstract QuickSlots: amap<Guid<EntityId>, HashMap<GameAction, int<SkillId>>>
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
      QuickSlots = cmap()
      Resources = cmap()
      Factions = cmap()
      BaseStats = cmap()
      DerivedStats = cmap()
      ActiveEffects = cmap()
      AbilityCooldowns = cmap()
      LiveProjectiles = cmap()
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
          member _.QuickSlots = mutableWorld.QuickSlots
          member _.Resources = mutableWorld.Resources
          member _.Factions = mutableWorld.Factions
          member _.BaseStats = mutableWorld.BaseStats
          member _.DerivedStats = mutableWorld.DerivedStats
          member _.ActiveEffects = mutableWorld.ActiveEffects
          member _.AbilityCooldowns = mutableWorld.AbilityCooldowns
          member _.LiveProjectiles = mutableWorld.LiveProjectiles
      }

    struct (mutableWorld, worldView)

module Systems =
  open World

  [<Struct>]
  type SystemKind =
    | Game
    | Movement
    | RawInput
    | InputMapping
    | Combat
    | Effects
    | ResourceManager

  type GameSystem(game: Game) =
    inherit GameComponent(game)

    abstract Kind: SystemKind
    default _.Kind = Game

    member val World = game.Services.GetService<World>() with get

    member val EventBus =
      game.Services.GetService<Pomo.Core.EventBus.EventBus>() with get

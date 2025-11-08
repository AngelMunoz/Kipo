namespace Pomo.Core.Domain

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open System.Collections.Concurrent
open Pomo.Core.Domain.Units


module World =
  open RawInput
  open Action

  [<Struct>]
  type MovementState =
    | Idle
    | MovingTo of targetPosition: Vector2

  [<Struct>]
  type MutableWorld = {
    DeltaTime: cval<TimeSpan>
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
    ActiveEffects: cmap<Guid<EntityId>, Skill.Effect IndexList>
    AbilityCooldowns: cmap<Guid<EntityId>, HashMap<int<SkillId>, TimeSpan>>
    LiveProjectiles: cmap<Guid<EntityId>, Projectile.LiveProjectile>
  }

  type World =
    abstract DeltaTime: TimeSpan aval
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
    abstract ActiveEffects: amap<Guid<EntityId>, Skill.Effect IndexList>
    abstract AbilityCooldowns: amap<Guid<EntityId>, HashMap<int<SkillId>, TimeSpan>>
    abstract LiveProjectiles: amap<Guid<EntityId>, Projectile.LiveProjectile>


  let create() =
    let mutableWorld: MutableWorld = {
      DeltaTime = cval TimeSpan.Zero
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
          member _.DeltaTime = mutableWorld.DeltaTime
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

  [<Struct>]
  type ProjectileImpact = {
    ProjectileId: Guid<EntityId>
    CasterId: Guid<EntityId>
    TargetId: Guid<EntityId>
    SkillId: int<SkillId>
  }

  [<Struct>]
  type Selection =
    | SelectedEntity of entity: Guid<EntityId>
    | SelectedPosition of position: Vector2

  [<Struct>]
  type WorldEvent =
    | EntityCreated of created: Entity.EntitySnapshot
    | EntityRemoved of removed: Guid<EntityId>
    | PositionChanged of posChanged: struct (Guid<EntityId> * Vector2)
    | VelocityChanged of velChanged: struct (Guid<EntityId> * Vector2)
    | RawInputStateChanged of
      rawIChanged: struct (Guid<EntityId> * RawInputState)
    | InputMapChanged of iMapChanged: struct (Guid<EntityId> * InputMap)
    | GameActionStatesChanged of
      gAChanged: struct (Guid<EntityId> * HashMap<GameAction, InputActionState>)
    | ResourcesChanged of resChanged: struct (Guid<EntityId> * Entity.Resource)
    | FactionsChanged of
      facChanged: struct (Guid<EntityId> * Entity.Faction HashSet)
    | BaseStatsChanged of
      statsChanged: struct (Guid<EntityId> * Entity.BaseStats)
    | QuickSlotsChanged of
      qsChanged: struct (Guid<EntityId> * HashMap<GameAction, int<SkillId>>)
    | CooldownsChanged of
      cdChanged: struct (Guid<EntityId> * HashMap<int<SkillId>, TimeSpan>)
    // UI Events
    | ShowNotification of message: string * position: Vector2
    // Combat and Skill events
    | SlotActivated of slot: GameAction * casterId: Guid<EntityId>
    | AbilityIntent of
      caster: Guid<EntityId> *
      skillId: int<SkillId> *
      abilityIntentTarget: Guid<EntityId> voption
    | AttackIntent of attacker: Guid<EntityId> * target: Guid<EntityId>
    | SetMovementTarget of setMTarget: struct (Guid<EntityId> * Vector2)
    | MovementStateChanged of
      mStateChanged: struct (Guid<EntityId> * MovementState)
    | TargetSelected of selector: Guid<EntityId> * selection: Selection
    | DamageDealt of target: Guid<EntityId> * amount: int
    | EntityDied of target: Guid<EntityId>
    // Attribute and Effect events
    | StatsChanged of entity: Guid<EntityId> * newStats: Entity.DerivedStats
    | EffectApplied of entity: Guid<EntityId> * effect: Skill.Effect
    | CreateProjectile of
      projParams: struct (Guid<EntityId> * Projectile.LiveProjectile)
    | ProjectileImpacted of impact: ProjectileImpact

module EventBus =
  open World

  type EventBus() =
    let eventQueue = ConcurrentQueue<WorldEvent>()

    let observers =
      Collections.Generic.Dictionary<Guid, IObserver<WorldEvent>>()

    member _.Publish(event) = eventQueue.Enqueue(event)

    member _.Publish(events: WorldEvent seq) =
      for event in events do
        eventQueue.Enqueue(event)

    member _.TryDequeue(event: byref<WorldEvent>) =
      match eventQueue.TryDequeue() with
      | true, ev ->
        observers.Values |> Seq.iter(fun obs -> obs.OnNext(ev))
        event <- ev
        true
      | false, _ -> false

    interface IObservable<WorldEvent> with
      member _.Subscribe(observer: IObserver<WorldEvent>) =
        let id = Guid.NewGuid()
        observers.Add(id, observer)

        { new IDisposable with
            member _.Dispose() = observers.Remove(id) |> ignore
        }


module Systems =
  open World
  open EventBus

  [<Struct>]
  type SystemKind =
    | Game
    | Movement
    | RawInput
    | InputMapping
    | Combat

  type GameSystem(game: Game) =
    inherit GameComponent(game)

    abstract Kind: SystemKind
    default _.Kind = Game

    member val World = game.Services.GetService<World>() with get
    member val EventBus = game.Services.GetService<EventBus>() with get

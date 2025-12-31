namespace Pomo.Core

open System
open FSharp.UMX
open Microsoft.Xna.Framework
open System.Collections.Generic
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Events
open Pomo.Core.Stores
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.Projectile
open Pomo.Core.Domain.Skill

module Projections =
  let private liveEntities(world: World) =
    world.Resources
    |> AMap.filter(fun _ resource -> resource.Status = Entity.Status.Alive)
    |> AMap.keys

  let inline private effectKindToCombatStatus(effect: Skill.ActiveEffect) =
    match effect.SourceEffect.Kind with
    | Skill.EffectKind.Stun -> Some CombatStatus.Stunned
    | Skill.EffectKind.Silence -> Some CombatStatus.Silenced
    | Skill.EffectKind.Taunt -> None
    | Skill.EffectKind.Buff
    | Skill.EffectKind.DamageOverTime
    | Skill.EffectKind.ResourceOverTime
    | Skill.EffectKind.Debuff -> None

  let private calculateCombatStatuses(world: World) =
    world.ActiveEffects
    |> AMap.map(fun _ effectList ->
      effectList |> IndexList.choose effectKindToCombatStatus)

  module private DerivedStatsCalculator =

    let calculateBase(baseStats: Entity.BaseStats) : Entity.DerivedStats = {
      AP = baseStats.Power * 2
      AC = baseStats.Power + int(float baseStats.Power * 1.25)
      DX = baseStats.Power
      MP = baseStats.Magic * 5
      MA = baseStats.Magic * 2
      MD = baseStats.Magic + int(float baseStats.Magic * 1.25)
      WT = baseStats.Sense * 5
      DA = baseStats.Sense * 2
      LK = baseStats.Sense + int(float baseStats.Sense * 0.5)
      HP = baseStats.Charm * 10
      DP = baseStats.Charm + int(float baseStats.Charm * 1.25)
      HV = baseStats.Charm * 2
      MS = 100
      HPRegen = 20
      MPRegen = 20
      ElementAttributes = HashMap.empty
      ElementResistances = HashMap.empty
    }

    let updateStat
      (stats: Entity.DerivedStats)
      (stat: Stat)
      (transformI: int -> int)
      (transformF: float -> float)
      =
      match stat with
      | AP -> { stats with AP = transformI stats.AP }
      | AC -> { stats with AC = transformI stats.AC }
      | DX -> { stats with DX = transformI stats.DX }
      | MP -> { stats with MP = transformI stats.MP }
      | MA -> { stats with MA = transformI stats.MA }
      | MD -> { stats with MD = transformI stats.MD }
      | WT -> { stats with WT = transformI stats.WT }
      | DA -> { stats with DA = transformI stats.DA }
      | LK -> { stats with LK = transformI stats.LK }
      | HP -> { stats with HP = transformI stats.HP }
      | DP -> { stats with DP = transformI stats.DP }
      | HV -> { stats with HV = transformI stats.HV }
      | MS -> { stats with MS = transformI stats.MS }
      | HPRegen -> {
          stats with
              HPRegen = transformI stats.HPRegen
        }
      | MPRegen -> {
          stats with
              MPRegen = transformI stats.MPRegen
        }
      | ElementResistance element ->
        let current =
          stats.ElementResistances
          |> HashMap.tryFind element
          |> Option.defaultValue 0.0

        let updated = transformF current

        {
          stats with
              ElementResistances =
                stats.ElementResistances.Add(element, updated)
        }
      | ElementAttribute element ->
        let current =
          stats.ElementAttributes
          |> HashMap.tryFind element
          |> Option.defaultValue 0.0

        let updated = transformF current

        {
          stats with
              ElementAttributes = stats.ElementAttributes.Add(element, updated)
        }

    let inline applySingleModifier
      (accStats: Entity.DerivedStats byref)
      (statModifier: StatModifier)
      =
      match statModifier with
      | Additive(stat, value) ->
        accStats <- updateStat accStats stat ((+)(int value)) ((+) value)
      | Multiplicative(stat, value) ->
        let multiplyI(current: int) = float current * value |> int
        let multiplyF(current: float) = current * value
        accStats <- updateStat accStats stat multiplyI multiplyF

    let applyModifiers
      (stats: Entity.DerivedStats)
      (effects: Skill.ActiveEffect IndexList voption)
      (equipmentBonuses: StatModifier IndexList)
      =
      let mutable result = stats

      for modifier in equipmentBonuses do
        applySingleModifier &result modifier

      match effects with
      | ValueNone -> ()
      | ValueSome effectList ->
        for effect in effectList do
          for modifier in effect.SourceEffect.Modifiers do
            match modifier with
            | Skill.EffectModifier.StaticMod m -> applySingleModifier &result m
            | Skill.EffectModifier.ResourceChange _
            | Skill.EffectModifier.AbilityDamageMod _
            | Skill.EffectModifier.DynamicMod _ -> ()

      result

    let inline collectEquipmentStats
      (equipmentStats: HashMap<Slot, StatModifier array>)
      : StatModifier IndexList =
      if equipmentStats.IsEmpty then
        IndexList.Empty
      else
        let mutable totalCount = 0

        for _, stats in equipmentStats do
          totalCount <- totalCount + stats.Length

        if totalCount = 0 then
          IndexList.Empty
        else
          let allStats = Array.zeroCreate totalCount
          let mutable idx = 0

          for _, stats in equipmentStats do
            for stat in stats do
              allStats[idx] <- stat
              idx <- idx + 1

          IndexList.ofArray allStats

    let getEquipmentStatBonusesForId
      (world: World, itemStore: ItemStore, entityId: Guid<EntityId>)
      =
      adaptive {
        let! equipmentStats =
          world.EquippedItems
          |> AMap.tryFind entityId
          |> AVal.map(
            Option.map(fun map ->
              map
              |> HashMap.chooseV(fun _ itemInstanceId ->
                world.ItemInstances
                |> Dictionary.tryFindV itemInstanceId
                |> ValueOption.bind(fun instance ->
                  itemStore.tryFind instance.ItemId)
                |> ValueOption.map(fun item ->
                  match item.Kind with
                  | Wearable props -> props.Stats
                  | _ -> Array.empty)))
          )

        let equipmentStats = equipmentStats |> Option.defaultValue HashMap.empty
        return collectEquipmentStats equipmentStats
      }

  let private calculateDerivedStats (itemStore: ItemStore) (world: World) =
    (world.BaseStats, world.ActiveEffects)
    ||> AMap.choose2V(fun _ baseStats effects ->
      baseStats |> ValueOption.map(fun stats -> struct (stats, effects)))
    |> AMap.mapA(fun entityId struct (baseStats, effects) -> adaptive {
      let! equipmentBonuses =
        DerivedStatsCalculator.getEquipmentStatBonusesForId(
          world,
          itemStore,
          entityId
        )

      let initialStats = DerivedStatsCalculator.calculateBase baseStats

      let finalStats =
        DerivedStatsCalculator.applyModifiers
          initialStats
          effects
          equipmentBonuses

      return finalStats
    })

  [<Struct>]
  type ResolvedInstance = {
    InstanceId: Guid<ItemInstanceId>
    GetUsesLeft: unit -> int voption
  }

  [<Struct>]
  type ResolvedItemStack = {
    Definition: ItemDefinition
    Count: int
    Instances: ResolvedInstance list
  }

  let private resolvedInventories (world: World) (itemStore: ItemStore) =
    world.EntityInventories
    |> AMap.map(fun _ itemInstanceIds ->
      let mutable result = HashMap.empty

      for instanceId in itemInstanceIds do
        world.ItemInstances
        |> Dictionary.tryFindV instanceId
        |> ValueOption.bind(fun inst ->
          itemStore.tryFind inst.ItemId
          |> ValueOption.map(fun def -> struct (inst, def)))
        |> ValueOption.iter(fun struct (inst, def) ->
          let getUsesLeft() =
            world.ItemInstances
            |> Dictionary.tryFindV instanceId
            |> ValueOption.bind _.UsesLeft

          let resolvedInst = {
            InstanceId = instanceId
            GetUsesLeft = getUsesLeft
          }

          let stack =
            match result |> HashMap.tryFindV inst.ItemId with
            | ValueSome existing -> {
                existing with
                    Count = existing.Count + 1
                    Instances = resolvedInst :: existing.Instances
              }
            | ValueNone ->
                {
                  Definition = def
                  Count = 1
                  Instances = [ resolvedInst ]
                }

          result <- result |> HashMap.add inst.ItemId stack)

      result)

  let private equippedItemDefs(world: World, itemStore: ItemStore) =
    world.EquippedItems
    |> AMap.map(fun _ itemIds ->
      itemIds
      |> HashMap.chooseV(fun _ itemId ->
        match world.ItemInstances |> Dictionary.tryFindV itemId with
        | ValueSome instance ->
          match itemStore.tryFind instance.ItemId with
          | ValueSome def -> ValueSome def
          | ValueNone -> ValueNone
        | ValueNone -> ValueNone))

  let private activeActionSets(world: World) =
    (world.ActiveActionSets, world.ActionSets)
    ||> AMap.choose2V(fun _ set sets ->
      let sets = defaultValueArg sets HashMap.empty

      set |> ValueOption.bind(fun set -> sets |> HashMap.tryFindV set))


  [<Struct>]
  type MovementSnapshot = {
    Positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>
    SpatialGrid: IReadOnlyDictionary<GridCell, Guid<EntityId>[]>
    Rotations: IReadOnlyDictionary<Guid<EntityId>, float32>
    ModelConfigIds: IReadOnlyDictionary<Guid<EntityId>, string>
  } with

    static member Empty = {
      Positions = Dictionary() :> IReadOnlyDictionary<_, _>
      SpatialGrid = Dictionary() :> IReadOnlyDictionary<_, _>
      Rotations = Dictionary() :> IReadOnlyDictionary<_, _>
      ModelConfigIds = Dictionary() :> IReadOnlyDictionary<_, _>
    }

  /// 3D movement snapshot for BlockMap-based scenarios
  [<Struct>]
  type Movement3DSnapshot = {
    Positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>
    SpatialGrid3D: IReadOnlyDictionary<GridCell3D, Guid<EntityId>[]>
    Rotations: IReadOnlyDictionary<Guid<EntityId>, float32>
    ModelConfigIds: IReadOnlyDictionary<Guid<EntityId>, string>
  } with

    static member Empty = {
      Positions = Dictionary()
      SpatialGrid3D = Dictionary()
      Rotations = Dictionary()
      ModelConfigIds = Dictionary()
    }


  [<Struct>]
  type EntityScenarioContext = {
    ScenarioId: Guid<ScenarioId>
    Scenario: Scenario
    MapKey: string
  }

  [<Struct>]
  type RegenerationContext = {
    Resources: Entity.Resource
    InCombatUntil: TimeSpan
    DerivedStats: Entity.DerivedStats
  }

  type ProjectionService =
    abstract LiveEntities: aset<Guid<EntityId>>
    abstract CombatStatuses: amap<Guid<EntityId>, IndexList<CombatStatus>>
    abstract DerivedStats: amap<Guid<EntityId>, Entity.DerivedStats>
    abstract EquippedItems: amap<Guid<EntityId>, HashMap<Slot, ItemDefinition>>

    abstract ResolvedInventories:
      amap<Guid<EntityId>, HashMap<int<ItemId>, ResolvedItemStack>>

    abstract EntityScenarios: amap<Guid<EntityId>, Guid<ScenarioId>>

    abstract ActionSets:
      amap<Guid<EntityId>, HashMap<Action.GameAction, SlotProcessing>>

    abstract EntityScenarioContexts: amap<Guid<EntityId>, EntityScenarioContext>
    abstract RegenerationContexts: amap<Guid<EntityId>, RegenerationContext>
    abstract AIControlledEntities: aset<Guid<EntityId>>

    abstract ComputeMovementSnapshot: Guid<ScenarioId> -> MovementSnapshot

    abstract ComputeMovement3DSnapshot: Guid<ScenarioId> -> Movement3DSnapshot

    abstract GetNearbyEntitiesSnapshot:
      MovementSnapshot * HashSet<Guid<EntityId>> * Vector2 * float32 ->
        IndexList<struct (Guid<EntityId> * Vector2)>

    abstract GetNearbyEntities3DSnapshot:
      Movement3DSnapshot * HashSet<Guid<EntityId>> * WorldPosition * float32 ->
        IndexList<struct (Guid<EntityId> * WorldPosition)>


  module PhysicsCache =

    type PhysicsCacheService =
      abstract GetMovementSnapshot: Guid<ScenarioId> -> MovementSnapshot
      abstract GetMovement3DSnapshot: Guid<ScenarioId> -> Movement3DSnapshot
      abstract RefreshAllCaches: unit -> unit

    let private calculateSnapshot
      (time: TimeSpan)
      (velocities: IReadOnlyDictionary<Guid<EntityId>, Vector2>)
      (positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>)
      (rotations: IReadOnlyDictionary<Guid<EntityId>, float32>)
      (modelConfigIds: HashMap<Guid<EntityId>, string>)
      (entityScenarios: HashMap<Guid<EntityId>, Guid<ScenarioId>>)
      (scenarioId: Guid<ScenarioId>)
      =
      let dt = float32 time.TotalSeconds

      let positionsBuilder = Dictionary<Guid<EntityId>, WorldPosition>()
      let rotationsBuilder = Dictionary<Guid<EntityId>, float32>()
      let modelConfigBuilder = Dictionary<Guid<EntityId>, string>()
      let gridBuilder = Dictionary<GridCell, ResizeArray<Guid<EntityId>>>()

      for KeyValue(id, startPos) in positions do
        match entityScenarios |> HashMap.tryFindV id with
        | ValueSome sId when sId = scenarioId ->
          // Calculate Position
          let currentPos =
            match velocities |> Dictionary.tryFindV id with
            | ValueSome v ->
                // Apply 2D velocity to X/Z plane, preserve Y
                {
                  WorldPosition.X = startPos.X + v.X * dt
                  Y = startPos.Y
                  Z = startPos.Z + v.Y * dt
                }
            | ValueNone -> startPos

          positionsBuilder[id] <- currentPos

          // Calculate Rotation (Derived from Velocity if moving, else keep existing)
          let rotation =
            match velocities |> Dictionary.tryFindV id with
            | ValueSome v when v <> Vector2.Zero ->
              float32(Math.Atan2(float v.X, float v.Y))
            | _ ->
              rotations
              |> Dictionary.tryFindV id
              |> ValueOption.defaultValue 0.0f

          rotationsBuilder[id] <- rotation

          // Model Config
          match modelConfigIds |> HashMap.tryFindV id with
          | ValueSome configId -> modelConfigBuilder[id] <- configId
          | ValueNone -> ()

          // Calculate Grid Cell
          let cell =
            Spatial.getGridCell
              Core.Constants.Collision.GridCellSize
              (WorldPosition.toVector2 currentPos)

          // Add to Grid (O(1) amortized with ResizeArray)
          match gridBuilder |> Dictionary.tryFindV cell with
          | ValueSome list -> list.Add id
          | ValueNone -> gridBuilder[cell] <- ResizeArray([| id |])
        | _ -> ()

      let spatialGrid = Dictionary<GridCell, Guid<EntityId>[]>()

      for kv in gridBuilder do
        spatialGrid[kv.Key] <- kv.Value.ToArray()

      {
        Positions = positionsBuilder
        SpatialGrid = spatialGrid
        Rotations = rotationsBuilder
        ModelConfigIds = modelConfigBuilder
      }

    let private calculate3DSnapshot
      (time: TimeSpan)
      (velocities: IReadOnlyDictionary<Guid<EntityId>, Vector2>)
      (positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>)
      (rotations: IReadOnlyDictionary<Guid<EntityId>, float32>)
      (modelConfigIds: HashMap<Guid<EntityId>, string>)
      (entityScenarios: HashMap<Guid<EntityId>, Guid<ScenarioId>>)
      (scenarioId: Guid<ScenarioId>)
      =
      let dt = float32 time.TotalSeconds

      let positionsBuilder = Dictionary<Guid<EntityId>, WorldPosition>()
      let rotationsBuilder = Dictionary<Guid<EntityId>, float32>()
      let modelConfigBuilder = Dictionary<Guid<EntityId>, string>()
      let gridBuilder = Dictionary<GridCell3D, ResizeArray<Guid<EntityId>>>()

      for KeyValue(id, startPos) in positions do
        match entityScenarios |> HashMap.tryFindV id with
        | ValueSome sId when sId = scenarioId ->
          let currentPos =
            match velocities |> Dictionary.tryFindV id with
            | ValueSome v -> {
                WorldPosition.X = startPos.X + v.X * dt
                Y = startPos.Y
                Z = startPos.Z + v.Y * dt
              }
            | ValueNone -> startPos

          positionsBuilder[id] <- currentPos

          let rotation =
            match velocities |> Dictionary.tryFindV id with
            | ValueSome v when v <> Vector2.Zero ->
              float32(Math.Atan2(float v.X, float v.Y))
            | _ ->
              rotations
              |> Dictionary.tryFindV id
              |> ValueOption.defaultValue 0.0f

          rotationsBuilder[id] <- rotation

          match modelConfigIds |> HashMap.tryFindV id with
          | ValueSome configId -> modelConfigBuilder[id] <- configId
          | ValueNone -> ()

          let cell: GridCell3D = {
            X = int(currentPos.X / BlockMap.CellSize)
            Y = int(currentPos.Y / BlockMap.CellSize)
            Z = int(currentPos.Z / BlockMap.CellSize)
          }

          match gridBuilder |> Dictionary.tryFindV cell with
          | ValueSome list -> list.Add id
          | ValueNone -> gridBuilder[cell] <- ResizeArray([| id |])
        | _ -> ()

      let spatialGrid = Dictionary<GridCell3D, Guid<EntityId>[]>()

      for kv in gridBuilder do
        spatialGrid[kv.Key] <- kv.Value.ToArray()

      {
        Positions = positionsBuilder
        SpatialGrid3D = spatialGrid
        Rotations = rotationsBuilder
        ModelConfigIds = modelConfigBuilder
      }

    let create(world: World) : PhysicsCacheService =
      let snapshotCache = Dictionary<Guid<ScenarioId>, MovementSnapshot>()
      let snapshot3DCache = Dictionary<Guid<ScenarioId>, Movement3DSnapshot>()

      { new PhysicsCacheService with
          member _.GetMovementSnapshot(scenarioId) =
            match snapshotCache |> Dictionary.tryFindV scenarioId with
            | ValueSome snapshot -> snapshot
            | ValueNone -> MovementSnapshot.Empty

          member _.GetMovement3DSnapshot(scenarioId) =
            match snapshot3DCache |> Dictionary.tryFindV scenarioId with
            | ValueSome snapshot -> snapshot
            | ValueNone -> Movement3DSnapshot.Empty

          member _.RefreshAllCaches() =
            let time = world.Time |> AVal.force |> _.Delta
            let velocities = world.Velocities
            let positions = world.Positions
            let rotations = world.Rotations
            let modelConfigIds = world.ModelConfigId |> AMap.force
            let entityScenarios = world.EntityScenario |> AMap.force
            let scenarios = world.Scenarios |> AMap.force

            for (sId, _) in scenarios do
              let snapshot =
                calculateSnapshot
                  time
                  velocities
                  positions
                  rotations
                  modelConfigIds
                  entityScenarios
                  sId

              snapshotCache[sId] <- snapshot

              let snapshot3D =
                calculate3DSnapshot
                  time
                  velocities
                  positions
                  rotations
                  modelConfigIds
                  entityScenarios
                  sId

              snapshot3DCache[sId] <- snapshot3D
      }

  let private entityScenarioContexts(world: World) =
    world.EntityScenario
    |> AMap.mapA(fun _entityId scenarioId -> adaptive {
      let! scenario = world.Scenarios |> AMap.tryFind scenarioId

      match scenario with
      | Some s ->
        return
          Some {
            ScenarioId = scenarioId
            Scenario = s
            MapKey = s.Map.Key
          }
      | None -> return None
    })
    |> AMap.choose(fun _ v -> v)

  let private regenerationContexts
    (world: World)
    (derivedStats: amap<Guid<EntityId>, Entity.DerivedStats>)
    =
    // Join Resources + DerivedStats (both required for regen)
    (world.Resources, derivedStats)
    ||> AMap.choose2V(fun _ resources stats ->
      match resources, stats with
      | ValueSome r, ValueSome s when r.Status = Entity.Status.Alive ->
        ValueSome struct (r, s)
      | _ -> ValueNone)
    // Add InCombatUntil (optional, defaults to Zero)
    |> AMap.mapA(fun entityId struct (resources, stats) -> adaptive {
      let! inCombatUntil = world.InCombatUntil |> AMap.tryFind entityId

      return {
        Resources = resources
        InCombatUntil = inCombatUntil |> Option.defaultValue TimeSpan.Zero
        DerivedStats = stats
      }
    })

  let create
    (
      itemStore: ItemStore,
      world: World,
      physicsCache: PhysicsCache.PhysicsCacheService
    ) =
    let derivedStats = calculateDerivedStats itemStore world


    { new ProjectionService with
        member _.LiveEntities = liveEntities world
        member _.CombatStatuses = calculateCombatStatuses world
        member _.DerivedStats = derivedStats
        member _.EquippedItems = equippedItemDefs(world, itemStore)
        member _.ResolvedInventories = resolvedInventories world itemStore
        member _.EntityScenarios = world.EntityScenario
        member _.ActionSets = activeActionSets world
        member _.EntityScenarioContexts = entityScenarioContexts world
        member _.RegenerationContexts = regenerationContexts world derivedStats
        member _.AIControlledEntities = world.AIControllers |> AMap.keys

        member _.ComputeMovementSnapshot(scenarioId) =
          physicsCache.GetMovementSnapshot(scenarioId)

        member _.ComputeMovement3DSnapshot(scenarioId) =
          physicsCache.GetMovement3DSnapshot(scenarioId)

        member _.GetNearbyEntitiesSnapshot
          (snapshot, liveEntities, center, radius)
          =
          let cells =
            Spatial.getCellsInRadius
              Constants.Collision.GridCellSize
              center
              radius

          let potentialTargets =
            cells
            |> IndexList.collect(fun cell ->
              match snapshot.SpatialGrid.TryGetValue cell with
              | true, list -> IndexList.ofArray list
              | false, _ -> IndexList.empty)

          potentialTargets
          |> IndexList.choose(fun entityId ->
            if not(liveEntities.Contains entityId) then
              None
            else
              match snapshot.Positions.TryGetValue entityId with
              | true, pos ->
                let pos2d = WorldPosition.toVector2 pos

                if Vector2.Distance(pos2d, center) <= radius then
                  Some struct (entityId, pos2d)
                else
                  None
              | _ -> None)

        member _.GetNearbyEntities3DSnapshot
          (snapshot, liveEntities, center, radius)
          =
          let cellSize = BlockMap.CellSize
          let cellRadius = int(radius / cellSize) + 1
          let centerCellX = int(center.X / cellSize)
          let centerCellY = int(center.Y / cellSize)
          let centerCellZ = int(center.Z / cellSize)
          let results = ResizeArray<struct (Guid<EntityId> * WorldPosition)>()
          let mutable cell = Unchecked.defaultof<GridCell3D>

          for dx = -cellRadius to cellRadius do
            for dy = -cellRadius to cellRadius do
              for dz = -cellRadius to cellRadius do
                cell <- {
                  X = centerCellX + dx
                  Y = centerCellY + dy
                  Z = centerCellZ + dz
                }

                match snapshot.SpatialGrid3D.TryGetValue cell with
                | true, entityIds ->
                  for entityId in entityIds do
                    if liveEntities.Contains entityId then
                      match snapshot.Positions.TryGetValue entityId with
                      | true, pos ->
                        if WorldPosition.distance pos center <= radius then
                          results.Add struct (entityId, pos)
                      | _ -> ()
                | _ -> ()

          IndexList.ofSeq results
    }

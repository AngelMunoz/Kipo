namespace Pomo.Core.Systems

open System
open System.Collections.Concurrent
open Microsoft.Xna.Framework
open FSharp.Control.Reactive
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.EventBus
open Pomo.Core.Domain.Units
open Pomo.Core.Domain
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.World
open Pomo.Core.Domain.RawInput
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.AI

module StateUpdate =
  let COMBAT_DURATION = TimeSpan.FromSeconds(5.0)

  /// <remarks>
  /// These functions must run within a transaction block as they mutate changeable values.
  /// </remarks>
  module Entity =
    let addEntity (world: MutableWorld) (entity: Entity.EntitySnapshot) =
      world.Positions[entity.Id] <- entity.Position
      world.Velocities[entity.Id] <- entity.Velocity
      world.EntityScenario[entity.Id] <- entity.ScenarioId
      // Remove from spawning entities if it was spawning
      world.SpawningEntities.Remove entity.Id |> ignore

    let inline removeEntity (world: MutableWorld) (entity: Guid<EntityId>) =
      world.Positions.Remove(entity) |> ignore
      world.Velocities.Remove(entity) |> ignore
      world.LiveProjectiles.Remove(entity) |> ignore
      world.SpawningEntities.Remove(entity) |> ignore
      world.MovementStates.Remove(entity) |> ignore
      world.RawInputStates.Remove(entity) |> ignore
      world.InputMaps.Remove(entity) |> ignore
      world.GameActionStates.Remove(entity) |> ignore
      world.Resources.Remove(entity) |> ignore
      world.Factions.Remove(entity) |> ignore
      world.ActionSets.Remove(entity) |> ignore
      world.ActiveActionSets.Remove(entity) |> ignore
      world.AbilityCooldowns.Remove(entity) |> ignore
      world.InCombatUntil.Remove(entity) |> ignore
      world.PendingSkillCast.Remove(entity) |> ignore
      world.BaseStats.Remove(entity) |> ignore
      world.DerivedStats.Remove(entity) |> ignore
      world.ActiveEffects.Remove(entity) |> ignore
      world.EntityInventories.Remove(entity) |> ignore
      world.EquippedItems.Remove(entity) |> ignore
      world.AIControllers.Remove(entity) |> ignore
      world.EntityScenario.Remove(entity) |> ignore

    let inline addSpawningEntity
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, scenarioId: Guid<ScenarioId>,
              spawnType: SystemCommunications.SpawnType, position: Vector2)
      =
      // Capture current time for animation
      let startTime = world.Time.Value.TotalGameTime

      world.SpawningEntities[entityId] <-
        struct (spawnType, position, startTime)

    let inline updatePosition
      (world: MutableWorld)
      struct (entity: Guid<EntityId>, position: Vector2)
      =
      if world.Positions.ContainsKey entity then
        world.Positions[entity] <- position

    let inline updateVelocity
      (world: MutableWorld)
      struct (entity: Guid<EntityId>, velocity: Vector2)
      =
      if world.Velocities.ContainsKey entity then
        world.Velocities[entity] <- velocity

    let inline updateRotation
      (world: MutableWorld)
      struct (entity: Guid<EntityId>, rotation: float32)
      =
      if world.Positions.ContainsKey entity then
        world.Rotations[entity] <- rotation

    let inline updateMovementState
      (world: MutableWorld)
      struct (entity: Guid<EntityId>, movementState: MovementState)
      =
      if world.Positions.ContainsKey entity then
        world.MovementStates[entity] <- movementState

    let createProjectile
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, projectile: Projectile.LiveProjectile,
              startPos: Vector2 voption)
      =
      // Determine starting position:
      // 1. If explicitly provided (e.g., chain projectile), use that
      // 2. For position-targeted projectiles (like falling boulders), spawn AT the target
      // 3. Otherwise, spawn at caster position
      let startingPos =
        match startPos with
        | ValueSome pos -> Some pos
        | ValueNone ->
          match projectile.Target with
          | Projectile.PositionTarget targetPos -> Some targetPos
          | Projectile.EntityTarget _ ->
            world.Positions.TryGetValue projectile.Caster

      match startingPos with
      | Some pos ->
        match world.EntityScenario.TryGetValue projectile.Caster with
        | Some scenarioId ->
          world.Positions[entityId] <- pos
          world.Velocities[entityId] <- Vector2.Zero
          world.LiveProjectiles[entityId] <- projectile
          // Use projectile's model if specified, otherwise default to "Projectile"
          let modelConfig =
            match projectile.Info.Visuals.ModelId with
            | ValueSome modelId -> modelId
            | ValueNone -> "Projectile"

          world.ModelConfigId[entityId] <- modelConfig
          world.EntityScenario[entityId] <- scenarioId
        | None -> ()
      | None -> ()

  module RawInput =
    let inline updateState
      (world: MutableWorld)
      struct (id: Guid<EntityId>, state: RawInputState)
      =
      if world.Positions.ContainsKey id then
        world.RawInputStates[id] <- state

  module InputMapping =
    let inline updateMap
      (world: MutableWorld)
      struct (id: Guid<EntityId>, map: InputMap)
      =
      if world.Positions.ContainsKey id then
        world.InputMaps[id] <- map

    let inline updateActionStates
      (world: MutableWorld)
      struct (id: Guid<EntityId>, states: HashMap<GameAction, InputActionState>)
      =
      if world.Positions.ContainsKey id then
        world.GameActionStates[id] <- states

  module Combat =
    open Pomo.Core.Domain.Core

    let inline updateResources
      (world: MutableWorld)
      struct (id: Guid<EntityId>, resources: Domain.Entity.Resource)
      =
      if world.Positions.ContainsKey id then
        world.Resources[id] <- resources

    let inline updateFactions
      (world: MutableWorld)
      struct (id: Guid<EntityId>, factions: Domain.Entity.Faction HashSet)
      =
      if world.Positions.ContainsKey id then
        world.Factions[id] <- factions

    let inline updateActionSets
      (world: MutableWorld)
      struct (id: Guid<EntityId>,
              actionSets: HashMap<int, HashMap<GameAction, SlotProcessing>>)
      =
      if world.Positions.ContainsKey id then
        world.ActionSets[id] <- actionSets

    let inline activeActionSetChanged
      (world: MutableWorld)
      struct (id: Guid<EntityId>, activeSet: int)
      =
      if world.Positions.ContainsKey id then
        world.ActiveActionSets[id] <- activeSet

    let inline updateCooldowns
      (world: MutableWorld)
      struct (id: Guid<EntityId>, cooldowns: HashMap<int<SkillId>, TimeSpan>)
      =
      if world.Positions.ContainsKey id then
        world.AbilityCooldowns[id] <- cooldowns

    let inline updateInCombatTimer
      (world: MutableWorld)
      (entityId: Guid<EntityId>)
      =
      if world.Positions.ContainsKey entityId then
        let newTimestamp = world.Time.Value.TotalGameTime + COMBAT_DURATION
        world.InCombatUntil[entityId] <- newTimestamp

    let inline setPendingSkillCast
      (world: MutableWorld)
      (entityId: Guid<EntityId>)
      (skillId: int<SkillId>)
      (target: SystemCommunications.SkillTarget)
      =
      if world.Positions.ContainsKey entityId then
        world.PendingSkillCast[entityId] <- struct (skillId, target)

    let inline clearPendingSkillCast
      (world: MutableWorld)
      (entityId: Guid<EntityId>)
      =
      world.PendingSkillCast.Remove entityId |> ignore

  module Attributes =
    let inline updateBaseStats
      (world: MutableWorld)
      struct (id: Guid<EntityId>, stats: Domain.Entity.BaseStats)
      =
      if world.Positions.ContainsKey id then
        world.BaseStats[id] <- stats

    let inline updateDerivedStats
      (world: MutableWorld)
      (id: Guid<EntityId>, stats: Domain.Entity.DerivedStats)
      =
      if world.Positions.ContainsKey id then
        world.DerivedStats[id] <- stats

    let inline applyEffect
      (world: MutableWorld)
      (id: Guid<EntityId>, effect: ActiveEffect)
      =
      if world.Positions.ContainsKey id then
        match world.ActiveEffects.TryGetValue(id) with
        | Some effects ->
          world.ActiveEffects[id] <- IndexList.add effect effects
        | None -> world.ActiveEffects[id] <- IndexList.single effect

    let inline expireEffect
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, effectId: Guid<EffectId>)
      =
      match world.ActiveEffects.TryGetValue(entityId) with
      | Some effects ->
        let newEffects =
          effects |> IndexList.filter(fun effect -> effect.Id <> effectId)

        if IndexList.isEmpty newEffects then
          world.ActiveEffects.Remove(entityId) |> ignore
        else
          world.ActiveEffects[entityId] <- newEffects
      | None -> ()

    let inline refreshEffect
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, effectId: Guid<EffectId>)
      =
      match world.ActiveEffects.TryGetValue(entityId) with
      | Some effects ->
        let newEffects =
          effects
          |> IndexList.map(fun effect ->
            if effect.Id = effectId then
              {
                effect with
                    StartTime = world.Time.Value.TotalGameTime
              }
            else
              effect)

        world.ActiveEffects[entityId] <- newEffects
      | None -> ()

    let inline changeEffectStack
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, effectId: Guid<EffectId>, newCount: int)
      =
      match world.ActiveEffects.TryGetValue(entityId) with
      | Some effects ->
        let newEffects =
          effects
          |> IndexList.map(fun effect ->
            if effect.Id = effectId then
              { effect with StackCount = newCount }
            else
              effect)

        world.ActiveEffects[entityId] <- newEffects
      | None -> ()

  module Inventory =
    let inline alterItemInstance
      (world: MutableWorld)
      (itemInstanceId: Guid<ItemInstanceId>)
      (itemInstance: Item.ItemInstance voption)
      =
      match itemInstance with
      | ValueNone ->
        match world.ItemInstances.TryRemove itemInstanceId with
        | true, _ -> ()
        | false, _ -> ()
      | ValueSome itemInstance ->
        world.ItemInstances[itemInstanceId] <- itemInstance

    let inline addItemToInventory
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, itemInstanceId: Guid<ItemInstanceId>)
      =
      if world.Positions.ContainsKey entityId then
        let currentInventory =
          world.EntityInventories.TryGetValue(entityId)
          |> Option.defaultValue HashSet.empty

        world.EntityInventories[entityId] <-
          HashSet.add itemInstanceId currentInventory

    let inline removeItemFromInventory
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, itemInstanceId: Guid<ItemInstanceId>)
      =
      if world.Positions.ContainsKey entityId then
        let currentInventory =
          world.EntityInventories.TryGetValue entityId
          |> Option.defaultValue HashSet.empty

        world.EntityInventories[entityId] <-
          HashSet.remove itemInstanceId currentInventory

    let inline equipItem
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, slot: Item.Slot,
              itemInstanceId: Guid<ItemInstanceId>)
      =
      if world.Positions.ContainsKey entityId then
        let currentEquipped =
          world.EquippedItems.TryGetValue entityId
          |> Option.defaultValue HashMap.empty

        world.EquippedItems[entityId] <-
          HashMap.add slot itemInstanceId currentEquipped

    let inline unequipItem
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, slot: Item.Slot,
              itemInstanceId: Guid<ItemInstanceId>)
      =
      if world.Positions.ContainsKey entityId then
        let currentEquipped =
          world.EquippedItems.TryGetValue entityId
          |> Option.defaultValue HashMap.empty

        world.EquippedItems[entityId] <- HashMap.remove slot currentEquipped

  module AI =
    let inline updateController
      (world: MutableWorld)
      (entityId: Guid<EntityId>, controller: AI.AIController)
      =
      if world.Positions.ContainsKey entityId then
        world.AIControllers[entityId] <- controller

  module Animation =
    let inline updateActiveAnimations
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>,
              anims: Animation.AnimationState IndexList)
      =
      if world.Positions.ContainsKey entityId then
        world.ActiveAnimations[entityId] <- anims

    let inline updatePose
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, pose: HashMap<string, Matrix>)
      =
      if world.Positions.ContainsKey entityId then
        world.Poses[entityId] <- pose

    let inline removeAnimationState
      (world: MutableWorld)
      (entityId: Guid<EntityId>)
      =
      world.ActiveAnimations.Remove entityId |> ignore
      world.Poses.Remove entityId |> ignore

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type StateUpdateSystem
    (game: Game, env: PomoEnvironment, mutableWorld: World.MutableWorld) =
    inherit GameComponent(game)

    let (Core core) = env.CoreServices
    let eventBus = core.EventBus
    let events = ConcurrentQueue<StateChangeEvent>()
    let mutable sub: IDisposable = null

    do
      base.UpdateOrder <- 1000

      sub <-
        eventBus.Observable
        |> Observable.choose(fun e ->
          match e with
          | GameEvent.State stateEvent -> Some stateEvent
          | _ -> None)
        |> Observable.subscribe(fun e -> events.Enqueue(e))

    override _.Dispose(disposing: bool) : unit =
      if disposing then
        sub.Dispose()

      base.Dispose disposing

    override this.Update(gameTime) =
      transact(fun () ->
        let mutable event = Unchecked.defaultof<StateChangeEvent>

        while events.TryDequeue(&event) do
          match event with
          | EntityLifecycle event ->
            match event with
            | Created created -> Entity.addEntity mutableWorld created
            | Removed removed -> Entity.removeEntity mutableWorld removed
            | Spawning spawning ->
              Entity.addSpawningEntity mutableWorld spawning
          | Input event ->
            match event with
            | RawStateChanged rawIChanged ->
              RawInput.updateState mutableWorld rawIChanged
            | MapChanged iMapChanged ->
              InputMapping.updateMap mutableWorld iMapChanged
            | GameActionStatesChanged gAChanged ->
              InputMapping.updateActionStates mutableWorld gAChanged
            | ActionSetsChanged aSChanged ->
              Combat.updateActionSets mutableWorld aSChanged
            | ActiveActionSetChanged aasChanged ->
              Combat.activeActionSetChanged mutableWorld aasChanged
          | Physics event ->
            match event with
            | PositionChanged posChanged ->
              Entity.updatePosition mutableWorld posChanged
            | VelocityChanged velChanged ->
              Entity.updateVelocity mutableWorld velChanged
            | RotationChanged rotChanged ->
              Entity.updateRotation mutableWorld rotChanged
            | MovementStateChanged mStateChanged ->
              Entity.updateMovementState mutableWorld mStateChanged
          | Combat event ->
            match event with
            | ResourcesChanged resChanged ->
              Combat.updateResources mutableWorld resChanged
            | FactionsChanged facChanged ->
              Combat.updateFactions mutableWorld facChanged
            | BaseStatsChanged statsChanged ->
              Attributes.updateBaseStats mutableWorld statsChanged
            | StatsChanged(entity, newStats) ->
              Attributes.updateDerivedStats mutableWorld (entity, newStats)
            | EffectApplied(entity, effect) ->
              Attributes.applyEffect mutableWorld (entity, effect)
            | CooldownsChanged cdChanged ->
              Combat.updateCooldowns mutableWorld cdChanged
            | EffectExpired effectExpired ->
              Attributes.expireEffect mutableWorld effectExpired
            | EffectRefreshed effectRefreshed ->
              Attributes.refreshEffect mutableWorld effectRefreshed
            | EffectStackChanged effectStackChanged ->
              Attributes.changeEffectStack mutableWorld effectStackChanged
            | InCombatTimerRefreshed entityId ->
              Combat.updateInCombatTimer mutableWorld entityId
            | PendingSkillCastSet(entityId, skillId, target) ->
              Combat.setPendingSkillCast mutableWorld entityId skillId target
            | PendingSkillCastCleared entityId ->
              Combat.clearPendingSkillCast mutableWorld entityId
          | Inventory event ->
            match event with
            | ItemInstanceCreated itemInstance ->
              Inventory.alterItemInstance
                mutableWorld
                itemInstance.InstanceId
                (ValueSome itemInstance)
            | ItemInstanceRemoved itemInstanceId ->
              Inventory.alterItemInstance mutableWorld itemInstanceId ValueNone
            | ItemAddedToInventory itemAdded ->
              Inventory.addItemToInventory mutableWorld itemAdded
            | ItemRemovedFromInventory itemRemoved ->
              Inventory.removeItemFromInventory mutableWorld itemRemoved
            | ItemEquipped itemEquipped ->
              Inventory.equipItem mutableWorld itemEquipped
            | ItemUnequipped itemUnequipped ->
              Inventory.unequipItem mutableWorld itemUnequipped
            | UpdateItemInstance itemInstance ->
              Inventory.alterItemInstance
                mutableWorld
                itemInstance.InstanceId
                (ValueSome itemInstance)
          | AI event ->
            match event with
            | ControllerUpdated(entityId, controller) ->
              AI.updateController mutableWorld (entityId, controller)
          | Visuals event ->
            match event with
            | ModelConfigChanged(entityId, configId) ->
              if mutableWorld.Positions.ContainsKey entityId then
                mutableWorld.ModelConfigId[entityId] <- configId
          | Animation event ->
            match event with
            | ActiveAnimationsChanged animsChanged ->
              Animation.updateActiveAnimations mutableWorld animsChanged
            | PoseChanged poseChanged ->
              Animation.updatePose mutableWorld poseChanged
            | AnimationStateRemoved entityId ->
              Animation.removeAnimationState mutableWorld entityId

          // Uncategorized
          | CreateProjectile projParams ->
            Entity.createProjectile mutableWorld projParams)

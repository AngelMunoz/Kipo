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

module StateUpdate =
  let COMBAT_DURATION = TimeSpan.FromSeconds(5.0)

  /// <remarks>
  /// These functions must run within a transaction block as they mutate changeable values.
  /// </remarks>
  module Entity =
    let inline addEntity (world: MutableWorld) (entity: Entity.EntitySnapshot) =
      world.Positions[entity.Id] <- entity.Position
      world.Velocities[entity.Id] <- entity.Velocity

    let inline removeEntity (world: MutableWorld) (entity: Guid<EntityId>) =
      world.Positions.Remove(entity) |> ignore
      world.Velocities.Remove(entity) |> ignore
      world.LiveProjectiles.Remove(entity) |> ignore

    let inline updatePosition
      (world: MutableWorld)
      struct (entity: Guid<EntityId>, position: Vector2)
      =
      world.Positions[entity] <- position

    let inline updateVelocity
      (world: MutableWorld)
      struct (entity: Guid<EntityId>, velocity: Vector2)
      =
      world.Velocities[entity] <- velocity

    let inline updateMovementState
      (world: MutableWorld)
      struct (entity: Guid<EntityId>, movementState: MovementState)
      =
      world.MovementStates[entity] <- movementState

    let createProjectile
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, projectile: Projectile.LiveProjectile,
              startPos: Vector2 voption)
      =
      let startingPos =
        match startPos with
        | ValueSome pos -> Some pos
        | ValueNone -> world.Positions.TryGetValue projectile.Caster

      // Get caster's position to use as the projectile's starting position.
      match startingPos with
      | Some pos ->
        // A projectile needs a position and velocity to exist in the world.
        world.Positions[entityId] <- pos
        world.Velocities[entityId] <- Vector2.Zero
        world.LiveProjectiles[entityId] <- projectile
      | None -> () // Caster/start has no position, so we can't create the projectile.

  module RawInput =
    let inline updateState
      (world: MutableWorld)
      struct (id: Guid<EntityId>, state: RawInputState)
      =
      world.RawInputStates[id] <- state

  module InputMapping =
    let inline updateMap
      (world: MutableWorld)
      struct (id: Guid<EntityId>, map: InputMap)
      =
      world.InputMaps[id] <- map

    let inline updateActionStates
      (world: MutableWorld)
      struct (id: Guid<EntityId>, states: HashMap<GameAction, InputActionState>)
      =
      world.GameActionStates[id] <- states

  module Combat =
    let inline updateResources
      (world: MutableWorld)
      struct (id: Guid<EntityId>, resources: Domain.Entity.Resource)
      =
      world.Resources[id] <- resources

    let inline updateFactions
      (world: MutableWorld)
      struct (id: Guid<EntityId>, factions: Domain.Entity.Faction HashSet)
      =
      world.Factions[id] <- factions

    let inline updateQuickSlots
      (world: MutableWorld)
      struct (id: Guid<EntityId>,
              quickSlots:
                HashMap<Domain.Action.GameAction, int<Domain.Units.SkillId>>)
      =
      world.QuickSlots[id] <- quickSlots

    let inline updateCooldowns
      (world: MutableWorld)
      struct (id: Guid<EntityId>, cooldowns: HashMap<int<SkillId>, TimeSpan>)
      =
      world.AbilityCooldowns[id] <- cooldowns

    let inline updateInCombatTimer
      (world: MutableWorld)
      (entityId: Guid<EntityId>)
      =
      let newTimestamp = world.Time.Value.TotalGameTime + COMBAT_DURATION
      world.InCombatUntil[entityId] <- newTimestamp

    let inline setPendingSkillCast
      (world: MutableWorld)
      (entityId: Guid<EntityId>)
      (skillId: int<SkillId>)
      (target: SystemCommunications.SkillTarget)
      =
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
      world.BaseStats[id] <- stats

    let inline updateDerivedStats
      (world: MutableWorld)
      (id: Guid<EntityId>, stats: Domain.Entity.DerivedStats)
      =
      world.DerivedStats[id] <- stats

    let inline applyEffect
      (world: MutableWorld)
      (id: Guid<EntityId>, effect: ActiveEffect)
      =
      match world.ActiveEffects.TryGetValue(id) with
      | Some effects -> world.ActiveEffects[id] <- IndexList.add effect effects
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
      | ValueNone -> world.ItemInstances.Remove itemInstanceId |> ignore
      | ValueSome itemInstance ->
        world.ItemInstances[itemInstanceId] <- itemInstance

    let inline addItemToInventory
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, itemInstanceId: Guid<ItemInstanceId>)
      =
      let currentInventory =
        world.EntityInventories.TryGetValue(entityId)
        |> Option.defaultValue HashSet.empty

      world.EntityInventories[entityId] <-
        HashSet.add itemInstanceId currentInventory

    let inline removeItemFromInventory
      (world: MutableWorld)
      struct (entityId: Guid<EntityId>, itemInstanceId: Guid<ItemInstanceId>)
      =
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
      let currentEquipped =
        world.EquippedItems.TryGetValue entityId
        |> Option.defaultValue HashMap.empty

      world.EquippedItems[entityId] <- HashMap.remove slot currentEquipped

  // The dedicated STATE WRITER system.
  // It receives the MutableWorld via constructor injection, ensuring no other system can access it.
  type StateUpdateSystem(game: Game, mutableWorld: World.MutableWorld) =
    inherit GameComponent(game)

    let eventBus = game.Services.GetService<EventBus>()
    let events = ConcurrentQueue<StateChangeEvent>()
    let mutable sub: IDisposable = null

    do
      base.UpdateOrder <- 1000

      sub <-
        eventBus.GetObservableFor<StateChangeEvent>()
        |> Observable.subscribe(fun e -> events.Enqueue(e))

    override _.Dispose(disposing: bool) : unit =
      if disposing then
        sub.Dispose()

      base.Dispose disposing

    override this.Update(gameTime) =
      // This is the one and only place where state is written,
      // wrapped in a single transaction for efficiency.
      transact(fun () ->
        let mutable event = Unchecked.defaultof<StateChangeEvent>

        while events.TryDequeue(&event) do
          match event with
          | EntityLifecycle event ->
            match event with
            | Created created -> Entity.addEntity mutableWorld created
            | Removed removed -> Entity.removeEntity mutableWorld removed
          | Input event ->
            match event with
            | RawStateChanged rawIChanged ->
              RawInput.updateState mutableWorld rawIChanged
            | MapChanged iMapChanged ->
              InputMapping.updateMap mutableWorld iMapChanged
            | GameActionStatesChanged gAChanged ->
              InputMapping.updateActionStates mutableWorld gAChanged
            | QuickSlotsChanged qsChanged ->
              Combat.updateQuickSlots mutableWorld qsChanged
          | Physics event ->
            match event with
            | PositionChanged posChanged ->
              Entity.updatePosition mutableWorld posChanged
            | VelocityChanged velChanged ->
              Entity.updateVelocity mutableWorld velChanged
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
          // Uncategorized
          | CreateProjectile projParams ->
            Entity.createProjectile mutableWorld projParams)

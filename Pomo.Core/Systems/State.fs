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

    let removeEntity (world: MutableWorld) (entity: Guid<EntityId>) =
      world.EntityExists.Remove(entity) |> ignore
      world.Positions.Remove(entity) |> ignore
      world.Velocities.Remove(entity) |> ignore
      world.Rotations.Remove(entity) |> ignore
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
      world.ActiveOrbitals.Remove(entity) |> ignore
      world.ActiveCharges.Remove(entity) |> ignore


    /// Apply an EntitySpawnBundle atomically - all components set in one pass
    let applyEntitySpawnBundle
      (world: MutableWorld)
      (bundle: EntitySpawnBundle)
      =
      let entityId = bundle.Snapshot.Id
      world.EntityExists.Add(entityId) |> ignore

      // Core entity data (always present)
      world.Positions[entityId] <- bundle.Snapshot.Position
      world.Velocities[entityId] <- bundle.Snapshot.Velocity
      world.EntityScenario[entityId] <- bundle.Snapshot.ScenarioId
      world.SpawningEntities.Remove entityId |> ignore

      // Optional components
      bundle.Resources
      |> ValueOption.iter(fun r -> world.Resources[entityId] <- r)

      bundle.Factions
      |> ValueOption.iter(fun f -> world.Factions[entityId] <- f)

      bundle.BaseStats
      |> ValueOption.iter(fun s -> world.BaseStats[entityId] <- s)

      bundle.ModelConfig
      |> ValueOption.iter(fun m -> world.ModelConfigId[entityId] <- m)

      bundle.InputMap
      |> ValueOption.iter(fun im -> world.InputMaps[entityId] <- im)

      bundle.ActionSets
      |> ValueOption.iter(fun aSets -> world.ActionSets[entityId] <- aSets)

      bundle.ActiveActionSet
      |> ValueOption.iter(fun aSet -> world.ActiveActionSets[entityId] <- aSet)

      // Inventory items - create instances and add to inventory
      bundle.InventoryItems
      |> ValueOption.iter(fun items ->
        let inventory =
          items
          |> Array.fold
            (fun acc item ->
              world.ItemInstances[item.InstanceId] <- item
              FSharp.Data.Adaptive.HashSet.add item.InstanceId acc)
            FSharp.Data.Adaptive.HashSet.empty

        world.EntityInventories[entityId] <- inventory)

      // Equipped slots
      bundle.EquippedSlots
      |> ValueOption.iter(fun slots ->
        let equipped =
          slots
          |> Array.fold
            (fun acc struct (slot, instanceId) ->
              HashMap.add slot instanceId acc)
            HashMap.empty

        world.EquippedItems[entityId] <- equipped)

      bundle.AIController
      |> ValueOption.iter(fun ai -> world.AIControllers[entityId] <- ai)

    let inline updatePosition
      (world: MutableWorld)
      struct (entity: Guid<EntityId>, position: Vector2)
      =
      if world.EntityExists.Contains entity then
        world.Positions[entity] <- position

    let inline updateVelocity
      (world: MutableWorld)
      struct (entity: Guid<EntityId>, velocity: Vector2)
      =
      if world.EntityExists.Contains entity then
        world.Velocities[entity] <- velocity

    let inline updateRotation
      (world: MutableWorld)
      struct (entity: Guid<EntityId>, rotation: float32)
      =
      if world.EntityExists.Contains entity then
        world.Rotations[entity] <- rotation

    let inline updateMovementState
      (world: MutableWorld)
      struct (entity: Guid<EntityId>, movementState: MovementState)
      =
      if world.EntityExists.Contains entity then
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
        | ValueSome pos -> ValueSome pos
        | ValueNone ->
          match projectile.Target with
          | Projectile.PositionTarget targetPos -> ValueSome targetPos
          | Projectile.EntityTarget _ ->
            match world.Positions.TryGetValue projectile.Caster with
            | true, pos -> ValueSome pos
            | false, _ -> ValueNone

      match startingPos with
      | ValueSome pos ->
        match world.EntityScenario.TryGetValue projectile.Caster with
        | Some scenarioId ->
          world.EntityExists.Add(entityId) |> ignore
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
      | ValueNone -> ()

  module RawInput =
    let inline updateState
      (world: MutableWorld)
      struct (id: Guid<EntityId>, state: RawInputState)
      =
      if world.Positions.ContainsKey id then
        world.RawInputStates[id] <- state

  module InputMapping =

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
      struct (entityId: Guid<EntityId>, slot: Item.Slot)
      =
      if world.EntityExists.Contains entityId then
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

  module Orbital =
    let inline updateActiveOrbital
      (world: MutableWorld)
      (entityId: Guid<EntityId>, orbital: Orbital.ActiveOrbital)
      =
      if world.Positions.ContainsKey entityId then
        world.ActiveOrbitals[entityId] <- orbital

    let inline removeActiveOrbital
      (world: MutableWorld)
      (entityId: Guid<EntityId>)
      =
      world.ActiveOrbitals.Remove entityId |> ignore

  module Charge =
    let inline updateActiveCharge
      (world: MutableWorld)
      (entityId: Guid<EntityId>, charge: ActiveCharge)
      =
      if world.Positions.ContainsKey entityId then
        world.ActiveCharges[entityId] <- charge

    let inline removeActiveCharge
      (world: MutableWorld)
      (entityId: Guid<EntityId>)
      =
      world.ActiveCharges.Remove entityId |> ignore

/// High-performance state write service using command queue pattern.
/// All state modifications go through this module to ensure consistent
/// frame timing and avoid GC pressure from adaptive collections.
module StateWrite =
  open System.Buffers
  open System.Collections.Generic
  open Pomo.Core.Environment
  open System.Diagnostics

  [<Struct>]
  type NonAdaptiveCommand =
    | UpdatePosition of posEntityId: Guid<EntityId> * position: Vector2
    | UpdateVelocity of velEntityId: Guid<EntityId> * velocity: Vector2
    | UpdateRotation of rotEntityId: Guid<EntityId> * rotation: float32

  let inline applyNonAdaptiveCommand
    (world: MutableWorld)
    (cmd: NonAdaptiveCommand)
    =
    match cmd with
    | UpdatePosition(id, pos) ->
      StateUpdate.Entity.updatePosition world struct (id, pos)
    | UpdateVelocity(id, vel) ->
      StateUpdate.Entity.updateVelocity world struct (id, vel)
    | UpdateRotation(id, rot) ->
      StateUpdate.Entity.updateRotation world struct (id, rot)

  [<Struct>]
  type AdaptiveCommand =
    | UpdateRawInputState of
      rawEntityId: Guid<EntityId> *
      rawState: RawInput.RawInputState
    | UpdateGameActionStates of
      gasEntityId: Guid<EntityId> *
      actionStates: HashMap<Action.GameAction, Action.InputActionState>
    | UpdateActiveActionSet of aasEntityId: Guid<EntityId> * actionSet: int
    | UpdateMovementState of
      msEntityId: Guid<EntityId> *
      movementState: Events.MovementState
    | UpdateResources of
      resEntityId: Guid<EntityId> *
      resources: Entity.Resource
    | UpdateCooldowns of
      cdEntityId: Guid<EntityId> *
      cooldowns: HashMap<int<SkillId>, TimeSpan>
    | UpdateInCombatTimer of ictEntityId: Guid<EntityId>
    | AddEntity of addEntityId: Guid<EntityId> * scenarioId: Guid<ScenarioId>
    | RemoveEntity of removeEntityId: Guid<EntityId>
    // Animation/Visuals
    | UpdateActiveAnimations of
      animEntityId: Guid<EntityId> *
      anims: IndexList<Animation.AnimationState>
    | UpdatePose of poseEntityId: Guid<EntityId> * pose: HashMap<string, Matrix>
    | RemoveAnimationState of removeAnimEntityId: Guid<EntityId>
    | UpdateModelConfig of id: Guid<EntityId> * configId: string
    // Effects
    | ApplyEffect of effectEntityId: Guid<EntityId> * effect: Skill.ActiveEffect
    | ExpireEffect of expireEntityId: Guid<EntityId> * effectId: Guid<EffectId>
    | RefreshEffect of
      refreshEntityId: Guid<EntityId> *
      refreshEffectId: Guid<EffectId>
    | ChangeEffectStack of
      stackEntityId: Guid<EntityId> *
      stackEffectId: Guid<EffectId> *
      newStack: int
    | SetPendingSkillCast of
      pendingEntityId: Guid<EntityId> *
      skillId: int<SkillId> *
      target: SystemCommunications.SkillTarget
    | ClearPendingSkillCast of clearEntityId: Guid<EntityId>
    // Inventory
    | CreateItemInstance of instance: Item.ItemInstance
    | UpdateItemInstance of instance: Item.ItemInstance
    | AddItemToInventory of
      invEntityId: Guid<EntityId> *
      instanceId: Guid<ItemInstanceId>
    | EquipItem of
      eqEntityId: Guid<EntityId> *
      slot: Item.Slot *
      eqInstanceId: Guid<ItemInstanceId>
    | UnequipItem of ueqEntityId: Guid<EntityId> * ueqSlot: Item.Slot
    // AI
    | UpdateAIController of
      aiEntityId: Guid<EntityId> *
      controller: AI.AIController
    | CreateProjectile of
      projEntityId: Guid<EntityId> *
      proj: Projectile.LiveProjectile *
      pos: Vector2 voption
    | ApplyEntitySpawnBundle of bundle: EntitySpawnBundle
    | UpdateActiveOrbital of
      orbEntityId: Guid<EntityId> *
      orbital: Orbital.ActiveOrbital
    | RemoveActiveOrbital of remOrbEntityId: Guid<EntityId>
    | UpdateActiveCharge of chgEntityId: Guid<EntityId> * charge: ActiveCharge
    | RemoveActiveCharge of remChgEntityId: Guid<EntityId>
    // Notifications
    | AddNotification of notification: WorldText
    | SetNotifications of notifications: WorldText[]

  let inline applyAdaptiveCommand (world: MutableWorld) (cmd: AdaptiveCommand) =
    match cmd with
    | UpdateRawInputState(id, rawState) ->
      if world.EntityExists.Contains id then
        StateUpdate.RawInput.updateState world struct (id, rawState)
    | UpdateGameActionStates(id, actionStates) ->
      if world.EntityExists.Contains id then
        StateUpdate.InputMapping.updateActionStates
          world
          struct (id, actionStates)
    | UpdateActiveActionSet(id, actionSet) ->
      if world.EntityExists.Contains id then
        StateUpdate.Combat.activeActionSetChanged world struct (id, actionSet)
    | UpdateMovementState(id, mState) ->
      StateUpdate.Entity.updateMovementState world struct (id, mState)
    | UpdateResources(id, res) ->
      StateUpdate.Combat.updateResources world struct (id, res)
    | UpdateCooldowns(id, cds) ->
      StateUpdate.Combat.updateCooldowns world struct (id, cds)
    | UpdateInCombatTimer id -> StateUpdate.Combat.updateInCombatTimer world id
    | RemoveAnimationState id ->
      world.ActiveAnimations.Remove id |> ignore
      world.Poses.Remove id |> ignore
    | AddEntity(id, sid) ->
      world.EntityExists.Add(id) |> ignore
      world.EntityScenario[id] <- sid
    | RemoveEntity id -> StateUpdate.Entity.removeEntity world id
    // Animation/Visuals
    | UpdateActiveAnimations(id, anims) -> world.ActiveAnimations[id] <- anims
    | UpdatePose(id, pose) -> world.Poses[id] <- pose
    | UpdateModelConfig(id, configId) -> world.ModelConfigId[id] <- configId
    // Effects
    | ApplyEffect(id, effect) ->
      StateUpdate.Attributes.applyEffect world (id, effect)
    | ExpireEffect(id, effectId) ->
      StateUpdate.Attributes.expireEffect world struct (id, effectId)
    | RefreshEffect(id, effectId) ->
      StateUpdate.Attributes.refreshEffect world struct (id, effectId)
    | ChangeEffectStack(id, effectId, stack) ->
      StateUpdate.Attributes.changeEffectStack
        world
        struct (id, effectId, stack)
    // Pending skill cast
    | SetPendingSkillCast(id, skillId, target) ->
      StateUpdate.Combat.setPendingSkillCast world id skillId target
    | ClearPendingSkillCast id ->
      StateUpdate.Combat.clearPendingSkillCast world id
    // Inventory
    | CreateItemInstance instance ->
      world.ItemInstances[instance.InstanceId] <- instance
    | UpdateItemInstance instance ->
      world.ItemInstances[instance.InstanceId] <- instance
    | AddItemToInventory(entityId, instanceId) ->
      StateUpdate.Inventory.addItemToInventory
        world
        struct (entityId, instanceId)
    | EquipItem(entityId, slot, instanceId) ->
      StateUpdate.Inventory.equipItem world struct (entityId, slot, instanceId)
    | UnequipItem(entityId, slot) ->
      StateUpdate.Inventory.unequipItem world struct (entityId, slot)

    | UpdateAIController(entityId, controller) ->
      StateUpdate.AI.updateController world (entityId, controller)

    | CreateProjectile(entityId, proj, pos) ->
      StateUpdate.Entity.createProjectile world (entityId, proj, pos)
    | ApplyEntitySpawnBundle bundle ->
      StateUpdate.Entity.applyEntitySpawnBundle world bundle
    | UpdateActiveOrbital(entityId, orbital) ->
      StateUpdate.Orbital.updateActiveOrbital world (entityId, orbital)
    | RemoveActiveOrbital entityId ->
      StateUpdate.Orbital.removeActiveOrbital world entityId
    | UpdateActiveCharge(entityId, charge) ->
      StateUpdate.Charge.updateActiveCharge world (entityId, charge)
    | RemoveActiveCharge entityId ->
      StateUpdate.Charge.removeActiveCharge world entityId
    // Notifications
    | AddNotification notification -> world.Notifications.Add notification
    | SetNotifications notifications ->
      world.Notifications.Clear()

      for n in notifications do
        world.Notifications.Add n

  type CommandBuffer<'T>
    (initialCapacity: int, [<InlineIfLambda>] apply: MutableWorld -> 'T -> unit)
    =
    let pool = ArrayPool<'T>.Shared
    let mutable commands = pool.Rent initialCapacity
    let mutable count = 0
    let mutable lowUsageFrames = 0

    member _.Enqueue(cmd: 'T) =
      if count >= commands.Length then
        let newSize = commands.Length * 2
        let next = pool.Rent newSize
        Array.Copy(commands, next, commands.Length)
        pool.Return commands
        commands <- next
        lowUsageFrames <- 0
#if DEBUG
        Console.WriteLine $"CommandBuffer resized to {newSize}"
#endif

      commands[count] <- cmd
      count <- count + 1

    member _.Flush(world: MutableWorld) =
      for i = 0 to count - 1 do
        let cmd = commands[i]
        apply world cmd

      if count < commands.Length / 4 && commands.Length > initialCapacity then
        lowUsageFrames <- lowUsageFrames + 1

        if lowUsageFrames > 60 then
          let smaller = pool.Rent(max initialCapacity (commands.Length / 2))
          pool.Return commands
          commands <- smaller
          lowUsageFrames <- 0
#if DEBUG
          Console.WriteLine $"CommandBuffer resized to {commands.Length}"
#endif
      else
        lowUsageFrames <- 0

      count <- 0

    member _.Dispose() =
      pool.Return commands
      count <- 0

  let create(mutableWorld: MutableWorld) : IStateWriteService =
    let nonAdaptiveBuffer =
      CommandBuffer<NonAdaptiveCommand>(1024, applyNonAdaptiveCommand)

    let adaptiveBuffer =
      CommandBuffer<AdaptiveCommand>(1024, applyAdaptiveCommand)

    { new IStateWriteService with

        member _.UpdatePosition(id, pos) =
          nonAdaptiveBuffer.Enqueue(NonAdaptiveCommand.UpdatePosition(id, pos))

        member _.UpdateVelocity(id, vel) =
          nonAdaptiveBuffer.Enqueue(NonAdaptiveCommand.UpdateVelocity(id, vel))

        member _.UpdateRotation(id, rot) =
          nonAdaptiveBuffer.Enqueue(NonAdaptiveCommand.UpdateRotation(id, rot))


        member _.RemoveEntity(id) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.RemoveEntity(id))

        member _.ApplyEntitySpawnBundle(bundle) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.ApplyEntitySpawnBundle bundle)

        member _.CreateProjectile(entityId, proj, pos) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.CreateProjectile(entityId, proj, pos)
          )


        member _.UpdateMovementState(entityId, movementState) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.UpdateMovementState(entityId, movementState)
          )


        member _.UpdateRawInputState(entityId, rawState) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.UpdateRawInputState(entityId, rawState)
          )

        member _.UpdateGameActionStates(entityId, actionStates) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.UpdateGameActionStates(entityId, actionStates)
          )

        member _.UpdateActiveActionSet(entityId, actionSet) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.UpdateActiveActionSet(entityId, actionSet)
          )


        member _.UpdateResources(entityId, resources) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.UpdateResources(entityId, resources)
          )

        member _.UpdateCooldowns(id, cds) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.UpdateCooldowns(id, cds))

        member _.UpdateInCombatTimer(id) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.UpdateInCombatTimer id)

        member _.SetPendingSkillCast(id, skillId, target) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.SetPendingSkillCast(id, skillId, target)
          )

        member _.ClearPendingSkillCast(id) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.ClearPendingSkillCast id)


        member _.ApplyEffect(id, effect) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.ApplyEffect(id, effect))

        member _.ExpireEffect(id, effectId) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.ExpireEffect(id, effectId))

        member _.RefreshEffect(id, effectId) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.RefreshEffect(id, effectId))

        member _.ChangeEffectStack(id, effectId, stack) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.ChangeEffectStack(id, effectId, stack)
          )


        member _.CreateItemInstance(instance) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.CreateItemInstance instance)

        member _.UpdateItemInstance(instance) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.UpdateItemInstance instance)

        member _.AddItemToInventory(entityId, instanceId) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.AddItemToInventory(entityId, instanceId)
          )

        member _.EquipItem(entityId, slot, instanceId) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.EquipItem(entityId, slot, instanceId)
          )

        member _.UnequipItem(entityId, slot) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.UnequipItem(entityId, slot))


        member _.UpdateActiveAnimations(id, anims) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.UpdateActiveAnimations(id, anims)
          )

        member _.UpdatePose(id, pose) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.UpdatePose(id, pose))

        member _.RemoveAnimationState(id) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.RemoveAnimationState id)

        member _.UpdateModelConfig(id, configId) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.UpdateModelConfig(id, configId)
          )


        member _.UpdateAIController(entityId, controller) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.UpdateAIController(entityId, controller)
          )

        member _.UpdateActiveOrbital(entityId, orbital) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.UpdateActiveOrbital(entityId, orbital)
          )

        member _.RemoveActiveOrbital(entityId) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.RemoveActiveOrbital entityId)

        member _.UpdateActiveCharge(entityId, charge) =
          adaptiveBuffer.Enqueue(
            AdaptiveCommand.UpdateActiveCharge(entityId, charge)
          )

        member _.RemoveActiveCharge(entityId) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.RemoveActiveCharge entityId)

        member _.AddNotification(notification) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.AddNotification notification)

        member _.SetNotifications(notifications) =
          adaptiveBuffer.Enqueue(AdaptiveCommand.SetNotifications notifications)

        member _.FlushWrites() =
          nonAdaptiveBuffer.Flush mutableWorld
          transact(fun () -> adaptiveBuffer.Flush mutableWorld)

        member _.Dispose() =
          nonAdaptiveBuffer.Dispose()
          adaptiveBuffer.Dispose()
    }

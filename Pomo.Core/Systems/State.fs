namespace Pomo.Core.Domains


open System
open Microsoft.Xna.Framework

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.EventBus
open Pomo.Core.Domain.RawInput

module StateUpdate =

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
      struct (entity: Guid<EntityId>, projectile: Projectile.LiveProjectile)
      =
      // Get caster's position to use as the projectile's starting position.
      match world.Positions.TryGetValue(projectile.Caster) with
      | Some casterPos ->
        // A projectile needs a position and velocity to exist in the world.
        world.Positions[entity] <- casterPos
        world.Velocities[entity] <- Vector2.Zero
        world.LiveProjectiles[entity] <- projectile
      | None -> () // Caster has no position, so we can't create the projectile.

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

    let dealDamage
      (world: MutableWorld)
      (eventBus: EventBus)
      (targetId: Guid<EntityId>)
      (amount: int)
      =
      match world.Resources.TryGetValue(targetId) with
      | Some currentResources ->
        if currentResources.Status <> Entity.Status.Dead then
          let newHp = currentResources.HP - amount
          let newResources = { currentResources with HP = newHp }
          updateResources world (targetId, newResources)

          if newHp <= 0 then
            let deadResources = {
              newResources with
                  Status = Entity.Status.Dead
            }

            updateResources world (targetId, deadResources)
            eventBus.Publish(EntityDied targetId)
      | None -> ()

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
      (id: Guid<EntityId>, effect: Domain.Skill.Effect)
      =
      match world.ActiveEffects.TryGetValue(id) with
      | Some effects -> world.ActiveEffects[id] <- IndexList.add effect effects
      | None -> world.ActiveEffects[id] <- IndexList.single effect

  // The dedicated STATE WRITER system.
  // It receives the MutableWorld via constructor injection, ensuring no other system can access it.
  type StateUpdateSystem(game: Game, mutableWorld: World.MutableWorld) =
    inherit GameComponent(game)

    let eventBus = game.Services.GetService<EventBus>()

    do base.UpdateOrder <- 1000

    override this.Update(gameTime) =
      // This is the one and only place where state is written,
      // wrapped in a single transaction for efficiency.
      transact(fun () ->
        let mutable event = Unchecked.defaultof<WorldEvent>

        while eventBus.TryDequeue(&event) do
          match event with
          | PositionChanged change -> Entity.updatePosition mutableWorld change
          | EntityRemoved removed -> Entity.removeEntity mutableWorld removed
          | EntityCreated created -> Entity.addEntity mutableWorld created
          | VelocityChanged change -> Entity.updateVelocity mutableWorld change
          | RawInputStateChanged change ->
            RawInput.updateState mutableWorld change
          | InputMapChanged change ->
            InputMapping.updateMap mutableWorld change
          | GameActionStatesChanged change ->
            InputMapping.updateActionStates mutableWorld change
          | ResourcesChanged change ->
            Combat.updateResources mutableWorld change
          | FactionsChanged change -> Combat.updateFactions mutableWorld change
          | QuickSlotsChanged change ->
            Combat.updateQuickSlots mutableWorld change
          | BaseStatsChanged change ->
            Attributes.updateBaseStats mutableWorld change
          | StatsChanged(entity, newStats) ->
            Attributes.updateDerivedStats mutableWorld (entity, newStats)
          | EffectApplied(entity, effect) ->
            Attributes.applyEffect mutableWorld (entity, effect)
          | AbilityIntent _ -> () // To be handled by CombatSystem
          | DamageDealt(targetId, amount) ->
            Combat.dealDamage mutableWorld eventBus targetId amount
          | EntityDied targetId -> Entity.removeEntity mutableWorld targetId
          | SlotActivated(slot, casterId) -> ()
          | AttackIntent(attacker, target) ->
            // Treat as a basic melee attack (Skill 1)
            eventBus.Publish(
              AbilityIntent(attacker, (UMX.tag 1), ValueSome target)
            )
          | SetMovementTarget(mover, targetPosition) ->
            Entity.updateMovementState
              mutableWorld
              (mover, MovingTo targetPosition)
          | TargetSelected(selector, targetPosition) -> () // Handled by TargetingSystem
          | MovementStateChanged mStateChanged ->
            Entity.updateMovementState mutableWorld mStateChanged
          | ProjectileImpacted _ -> () // Handled by CombatSystem
          | CreateProjectile projectile ->
            Entity.createProjectile mutableWorld projectile
          | CooldownsChanged cdChanged -> Combat.updateCooldowns mutableWorld cdChanged
          | ShowNotification(message, position) -> failwith "Not Implemented")

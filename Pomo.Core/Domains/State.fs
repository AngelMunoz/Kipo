namespace Pomo.Core.Domains


open System
open Microsoft.Xna.Framework

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Units
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
            InputMapping.updateActionStates mutableWorld change)

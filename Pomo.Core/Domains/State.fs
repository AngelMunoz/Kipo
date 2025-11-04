namespace Pomo.Core.Domains


open System
open Microsoft.Xna.Framework

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.EventBus

module StateUpdate =

  /// <remarks>
  /// These functions must run within a transaction block as they mutate changeable values.
  /// </remarks>
  module Entity =
    let addEntity (world: MutableWorld) (entity: Entity.EntitySnapshot) =
      world.Positions[entity.Id] <- entity.Position
      world.Velocities[entity.Id] <- entity.Velocity

    let removeEntity (world: MutableWorld) (entity: Guid<EntityId>) =
      world.Positions.Remove(entity) |> ignore
      world.Velocities.Remove(entity) |> ignore

    let updatePosition
      (world: MutableWorld)
      struct (entity: Guid<EntityId>, position: Vector2)
      =
      world.Positions[entity] <- position

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
          | VelocityChanged struct (id, vel) ->
            mutableWorld.Velocities[id] <- vel)

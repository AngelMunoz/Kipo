namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Events
open Pomo.Core.Systems.Systems
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Core.Constants

module Collision =
  open Pomo.Core.Domain
  open Pomo.Core.Domain.World
  open System.Collections.Generic

  // Helper to get entities in nearby cells
  let private neighborOffsets = [|
    struct (-1, -1)
    0, -1
    1, -1
    -1, 0
    0, 0
    1, 0
    -1, 1
    0, 1
    1, 1
  |]

  let getNearbyEntities
    (grid: IReadOnlyDictionary<GridCell, Guid<EntityId>[]>)
    (cell: GridCell)
    =
    neighborOffsets
    |> Seq.collect(fun struct (dx, dy) ->
      let neighborCell = { X = cell.X + dx; Y = cell.Y + dy }

      match grid |> Dictionary.tryFindV neighborCell with
      | ValueSome entities -> entities
      | ValueNone -> Array.empty)


  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type CollisionSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let stateWrite = env.CoreServices.StateWrite

    override val Kind = SystemKind.Collision with get

    override this.Update _ =
      let scenarios = core.World.Scenarios |> AMap.force
      let entityScenarios = core.World.EntityScenario |> AMap.force

      for scenarioId, scenario in scenarios do
        let snapshot = gameplay.Projections.ComputeMovementSnapshot(scenarioId)
        let grid = snapshot.SpatialGrid
        let positions = snapshot.Positions
        let getNearbyTo = getNearbyEntities grid

        // Check for entity-entity collisions
        for KeyValue(entityId, pos) in positions do
          let cell = getGridCell BlockMap.CellSize (WorldPosition.toVector2 pos)

          let nearbyEntities = getNearbyTo cell

          for otherId in nearbyEntities do
            if entityId <> otherId then
              match positions |> Dictionary.tryFindV otherId with
              | ValueSome otherPos ->
                let pos2d = WorldPosition.toVector2 pos
                let otherPos2d = WorldPosition.toVector2 otherPos
                let distance = Vector2.Distance(pos2d, otherPos2d)
                // Simple radius check
                if distance < Core.Constants.Entity.CollisionDistance then
                  core.EventBus.Publish(
                    GameEvent.Collision(
                      SystemCommunications.EntityCollision
                        struct (entityId, otherId)
                    )
                  )
              | ValueNone -> ()

        ()

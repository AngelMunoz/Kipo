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
  open Pomo.Core.Domain.BlockMap
  open System.Collections.Generic
  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  let private neighborOffsets3D: struct (int * int * int)[] = [|
    struct (-1, 0, -1)
    struct (0, 0, -1)
    struct (1, 0, -1)
    struct (-1, 0, 0)
    struct (0, 0, 0)
    struct (1, 0, 0)
    struct (-1, 0, 1)
    struct (0, 0, 1)
    struct (1, 0, 1)
  |]

  let private getNearbyEntities3D
    (grid: IReadOnlyDictionary<GridCell3D, Guid<EntityId>[]>)
    (cell: GridCell3D)
    =
    neighborOffsets3D
    |> Seq.collect(fun struct (dx, dy, dz) ->
      let neighborCell: GridCell3D = {
        X = cell.X + dx
        Y = cell.Y + dy
        Z = cell.Z + dz
      }

      match grid |> Dictionary.tryFindV neighborCell with
      | ValueSome entities -> entities
      | ValueNone -> Array.empty)

  type CollisionSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices

    override val Kind = SystemKind.Collision with get

    override this.Update gameTime =
      let scenarios = core.World.Scenarios |> AMap.force

      for scenarioId, _ in scenarios do
        let snapshot =
          gameplay.Projections.ComputeMovement3DSnapshot(scenarioId)

        let grid = snapshot.SpatialGrid3D
        let positions = snapshot.Positions

        // Entity-entity collision detection only
        // Block collision is handled in the snapshot calculation
        for KeyValue(entityId, pos) in positions do
          let cell: GridCell3D = {
            X = int(pos.X / BlockMap.CellSize)
            Y = int(pos.Y / BlockMap.CellSize)
            Z = int(pos.Z / BlockMap.CellSize)
          }

          let nearbyEntities = getNearbyEntities3D grid cell

          for otherId in nearbyEntities do
            if entityId <> otherId then
              positions
              |> Dictionary.tryFindV otherId
              |> ValueOption.iter(fun otherPos ->
                let dx = pos.X - otherPos.X
                let dz = pos.Z - otherPos.Z
                let distanceXZ = sqrt(dx * dx + dz * dz)

                if distanceXZ < Core.Constants.Entity.CollisionDistance then
                  core.EventBus.Publish(
                    GameEvent.Collision(
                      SystemCommunications.EntityCollision
                        struct (entityId, otherId)
                    )
                  ))

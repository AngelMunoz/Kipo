namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.Entity
open Pomo.Core.Stores
open Pomo.Core.Domain.Events
open Pomo.Core.Systems.Systems

module Collision =
  open Pomo.Core.Domain

  // Helper to get entities in nearby cells
  let getNearbyEntities
    (grid: HashMap<GridCell, IndexList<Guid<EntityId>>>)
    (cell: GridCell)
    =
    let neighborOffsets: IndexList<struct (int * int)> =
      IndexList.ofList [
        -1, -1
        0, -1
        1, -1
        -1, 0
        0, 0
        1, 0
        -1, 1
        0, 1
        1, 1
      ]

    neighborOffsets
    |> IndexList.collect(fun struct (dx, dy) ->
      let neighborCell = { X = cell.X + dx; Y = cell.Y + dy }

      match grid |> HashMap.tryFindV neighborCell with
      | ValueSome entities -> entities
      | ValueNone -> IndexList.empty)


  type CollisionSystem(game: Game, mapKey: string) as this =
    inherit GameSystem(game)

    let mapStore = game.Services.GetService<MapStore>()
    let map = mapStore.tryFind mapKey

    let spatialGrid = this.Projections.SpatialGrid

    override val Kind = SystemKind.Collision with get

    override this.Update _ =
      let grid = spatialGrid |> AMap.force
      let positions = this.Projections.UpdatedPositions |> AMap.force
      let liveEntities = this.Projections.LiveEntities |> ASet.force
      let getNearbyTo = getNearbyEntities grid

      // Check for collisions
      for entityId in liveEntities do
        match positions |> HashMap.tryFindV entityId with
        | ValueSome pos ->
          let cell = getGridCell Core.Constants.Collision.GridCellSize pos
          let nearbyEntities = getNearbyTo cell

          for otherId in nearbyEntities do
            if entityId <> otherId then
              match positions |> HashMap.tryFindV otherId with
              | ValueSome otherPos ->
                let distance = Vector2.Distance(pos, otherPos)
                // Simple radius check
                if distance < Core.Constants.Entity.CollisionDistance then
                  this.EventBus.Publish(
                    SystemCommunications.EntityCollision
                      struct (entityId, otherId)
                  )
              | ValueNone -> ()
        | ValueNone -> ()

      // Check for map object collisions
      match map with
      | ValueSome mapDef ->
        for entityId in liveEntities do
          match positions |> HashMap.tryFindV entityId with
          | ValueSome pos ->
            for group in mapDef.ObjectGroups do
              for obj in group.Objects do
                let isCollidable =
                  match obj.Type with
                  | ValueSome MapObjectType.Wall -> true
                  | _ -> false

                if isCollidable then
                  let entityPoly = getEntityPolygon pos
                  let objPoly = getMapObjectPolygon obj

                  match Spatial.intersectsMTV entityPoly objPoly with
                  | ValueSome mtv ->

                    this.EventBus.Publish(
                      SystemCommunications.MapObjectCollision
                        struct (entityId, obj, mtv)
                    )
                  | ValueNone -> ()
          | ValueNone -> ()
      | ValueNone -> ()

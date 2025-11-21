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

  // Helper to get entities in nearby cells
  let getNearbyEntities
    (grid: HashMap<GridCell, IndexList<Guid<EntityId>>>)
    (cell: GridCell)
    =
    let neighborOffsets =
      IndexList.ofList [
        struct (-1, -1)
        (0, -1)
        (1, -1)
        (-1, 0)
        (0, 0)
        (1, 0)
        (-1, 1)
        (0, 1)
        (1, 1)
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
          let cell = getGridCell 64.0f pos
          let nearbyEntities = getNearbyTo cell

          for otherId in nearbyEntities do
            if entityId <> otherId then
              match positions |> HashMap.tryFindV otherId with
              | ValueSome otherPos ->
                let distance = Vector2.Distance(pos, otherPos)
                // Simple radius check (assuming 32.0f radius for now)
                if distance < 64.0f then
                  this.EventBus.Publish(
                    StateChangeEvent.Collision(
                      CollisionEvents.EntityCollision struct (entityId, otherId)
                    )
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
                let entityPoly = getEntityPolygon pos
                let objPoly = getMapObjectPolygon obj

                if intersects entityPoly objPoly then
                  // Debug log
                  // Console.WriteLine($"Collision detected with {obj.Name} (ID: {obj.Id})")
                  this.EventBus.Publish(
                    StateChangeEvent.Collision(
                      CollisionEvents.MapObjectCollision struct (entityId, obj)
                    )
                  )
          | ValueNone -> ()
      | ValueNone -> ()

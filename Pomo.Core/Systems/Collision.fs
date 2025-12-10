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
    (grid: HashMap<GridCell, IndexList<Guid<EntityId>>>)
    (cell: GridCell)
    =
    neighborOffsets
    |> Seq.collect(fun struct (dx, dy) ->
      let neighborCell = { X = cell.X + dx; Y = cell.Y + dy }

      match grid |> HashMap.tryFindV neighborCell with
      | ValueSome entities -> entities
      | ValueNone -> IndexList.empty)


  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type CollisionSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices
    let (Gameplay gameplay) = env.GameplayServices

    let mutable mapObjectCache =
      HashMap.empty<
        string,
        HashMap<int<ObjectId>, struct (IndexList<Vector2> * IndexList<Vector2>)>
       >

    let getMapObjects(map: MapDefinition) =
      match mapObjectCache.TryFindV map.Key with
      | ValueSome objects -> objects
      | ValueNone ->
        let objects =
          map.ObjectGroups
          |> IndexList.collect(fun g -> g.Objects)
          |> IndexList.collect(fun obj ->
            match Spatial.getMapObjectPolygon obj with
            | ValueSome poly ->
              let axes = Spatial.getAxes poly
              IndexList.single(obj.Id, struct (poly, axes))
            | ValueNone -> IndexList.empty)
          |> HashMap.ofSeq

        mapObjectCache <- mapObjectCache.Add(map.Key, objects)
        objects



    override val Kind = SystemKind.Collision with get

    override this.Update _ =
      let scenarios = core.World.Scenarios |> AMap.force
      let entityScenarios = core.World.EntityScenario |> AMap.force

      for (scenarioId, scenario) in scenarios do
        let snapshot = gameplay.Projections.ComputeMovementSnapshot(scenarioId)
        let grid = snapshot.SpatialGrid
        let positions = snapshot.Positions
        let getNearbyTo = getNearbyEntities grid

        // Check for collisions
        for (entityId, pos) in positions do
          let cell = getGridCell Core.Constants.Collision.GridCellSize pos
          let nearbyEntities = getNearbyTo cell

          for otherId in nearbyEntities do
            if entityId <> otherId then
              match positions |> HashMap.tryFindV otherId with
              | ValueSome otherPos ->
                let distance = Vector2.Distance(pos, otherPos)
                // Simple radius check
                if distance < Core.Constants.Entity.CollisionDistance then
                  core.EventBus.Publish(
                    SystemCommunications.EntityCollision
                      struct (entityId, otherId)
                  )
              | ValueNone -> ()

        // Check for map object collisions per scenario
        let mapDef = scenario.Map
        let mapObjects = getMapObjects mapDef

        // Filter entities in this scenario
        // Optimization: We could maintain a reverse index of Scenario -> Entities,
        // but for now iterating liveEntities is okay if count is low.
        for (entityId, pos) in positions do
          let entityPoly = getEntityPolygon pos
          let entityAxes = getAxes entityPoly

          for group in mapDef.ObjectGroups do
            for obj in group.Objects do
              let isCollidable =
                match obj.Type with
                | ValueSome MapObjectType.Wall -> true
                | _ -> false

              let isTrigger = obj.PortalData.IsValueSome

              if isCollidable || isTrigger then
                match mapObjects.TryFindV obj.Id with
                | ValueSome struct (objPoly, objAxes) ->
                  // For triggers, we just need intersection, not MTV
                  // But SAT gives us both.
                  match
                    Spatial.intersectsMTVWithAxes
                      entityPoly
                      entityAxes
                      objPoly
                      objAxes
                  with
                  | ValueSome mtv ->
                    if isTrigger then
                      match obj.PortalData with
                      | ValueSome portalData ->
                        core.EventBus.Publish(
                          {
                            EntityId = entityId
                            TargetMap = portalData.TargetMap
                            TargetSpawn = portalData.TargetSpawn
                          }
                          : SystemCommunications.PortalTravel
                        )
                      | ValueNone -> ()
                    else
                      // It's a wall/collidable
                      core.EventBus.Publish(
                        SystemCommunications.MapObjectCollision
                          struct (entityId, obj, mtv)
                      )
                  | ValueNone -> ()
                | ValueNone -> ()

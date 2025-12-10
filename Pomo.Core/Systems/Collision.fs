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

  // Composite key for caching: (groupId, objectId) to handle duplicate IDs across groups
  [<Struct>]
  type CacheKey = {
    GroupId: int
    ObjectId: int<ObjectId>
  }

  type CollisionSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices
    let (Gameplay gameplay) = env.GameplayServices

    // Cache for polygon colliders (for SAT)
    let mutable polygonCache =
      HashMap.empty<
        string,
        HashMap<CacheKey, struct (IndexList<Vector2> * IndexList<Vector2>)>
       >

    // Cache for polyline colliders (for segment-based collision)
    let mutable polylineCache =
      HashMap.empty<string, HashMap<CacheKey, IndexList<Vector2>>>

    let getPolygonObjects(map: MapDefinition) =
      match polygonCache.TryFindV map.Key with
      | ValueSome objects -> objects
      | ValueNone ->
        let objects =
          map.ObjectGroups
          |> IndexList.collect(fun g ->
            g.Objects
            |> IndexList.collect(fun obj ->
              // Cache all objects with polygon shapes
              // The collision loop filters by isCollidable || isTrigger
              match Spatial.getMapObjectPolygon obj with
              | ValueSome poly ->
                let axes = Spatial.getAxes poly
                let key = { GroupId = g.Id; ObjectId = obj.Id }
                IndexList.single(key, struct (poly, axes))
              | ValueNone -> IndexList.empty))
          |> HashMap.ofSeq

        polygonCache <- polygonCache.Add(map.Key, objects)
        objects

    let getPolylineObjects(map: MapDefinition) =
      match polylineCache.TryFindV map.Key with
      | ValueSome objects -> objects
      | ValueNone ->
        let objects =
          map.ObjectGroups
          |> IndexList.collect(fun g ->
            g.Objects
            |> IndexList.collect(fun obj ->
              // Cache all objects with polyline shapes
              // The collision loop filters by isCollidable || isTrigger
              match Spatial.getMapObjectPolyline obj with
              | ValueSome chain ->
                let key = { GroupId = g.Id; ObjectId = obj.Id }
                IndexList.single(key, chain)
              | ValueNone -> IndexList.empty))
          |> HashMap.ofSeq

        polylineCache <- polylineCache.Add(map.Key, objects)
        objects

    // Entity collision radius for circle-based polyline collision
    // For a square entity, we use the circumscribed circle (touching corners)
    // so the circle fully contains the square: radius = halfDiagonal = halfSize * sqrt(2)
    let entityHalfSize = Core.Constants.Entity.Size.X / 2.0f
    let entityRadius = entityHalfSize * sqrt(2.0f)

    override val Kind = SystemKind.Collision with get

    override this.Update _ =
      let scenarios = core.World.Scenarios |> AMap.force
      let entityScenarios = core.World.EntityScenario |> AMap.force

      for (scenarioId, scenario) in scenarios do
        let snapshot = gameplay.Projections.ComputeMovementSnapshot(scenarioId)
        let grid = snapshot.SpatialGrid
        let positions = snapshot.Positions
        let getNearbyTo = getNearbyEntities grid

        // Check for entity-entity collisions
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
        let polygonObjects = getPolygonObjects mapDef
        let polylineObjects = getPolylineObjects mapDef

        for (entityId, pos) in positions do
          let entityPoly = getEntityPolygon pos
          let entityAxes = getAxes entityPoly

          // Accumulate all MTVs for this entity across all collisions
          let mutable accumulatedMTV = Vector2.Zero
          let mutable hasWallCollision = false

          for group in mapDef.ObjectGroups do
            for obj in group.Objects do
              let isCollidable =
                match obj.Type with
                | ValueSome MapObjectType.Wall -> true
                | _ -> false

              let isTrigger = obj.PortalData.IsValueSome

              if isCollidable || isTrigger then
                let cacheKey = {
                  GroupId = group.Id
                  ObjectId = obj.Id
                }

                // Try polygon collision first (for closed shapes)
                match polygonObjects.TryFindV cacheKey with
                | ValueSome struct (objPoly, objAxes) ->
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
                      // Accumulate MTV for combined correction
                      accumulatedMTV <- accumulatedMTV + mtv
                      hasWallCollision <- true

                      // Emit collision event for velocity sliding
                      core.EventBus.Publish(
                        SystemCommunications.MapObjectCollision
                          struct (entityId, obj, mtv)
                      )
                  | ValueNone -> ()
                | ValueNone ->
                  // Try polyline collision (for open chains)
                  match polylineObjects.TryFindV cacheKey with
                  | ValueSome chain ->
                    match Spatial.circleChainMTV pos entityRadius chain with
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
                        // Accumulate MTV for combined correction
                        accumulatedMTV <- accumulatedMTV + mtv
                        hasWallCollision <- true

                        // Emit collision event for velocity sliding
                        core.EventBus.Publish(
                          SystemCommunications.MapObjectCollision
                            struct (entityId, obj, mtv)
                        )
                    | ValueNone -> ()
                  | ValueNone -> ()

          // Apply single combined position correction after checking all objects
          if hasWallCollision then
            let correctedPos = pos + accumulatedMTV

            core.EventBus.Publish(
              Physics(PositionChanged struct (entityId, correctedPos))
            )

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
    // Reduced to inscribed circle (8.0f) to avoid large visual overlap and tight corners
    let entityHalfSize = Core.Constants.Entity.Size.X / 2.0f
    let entityRadius = entityHalfSize // * sqrt(2.0f) was too big

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

        // Get dependencies for CCD
        let velocities = core.World.Velocities |> AMap.force
        let time = core.World.Time |> AVal.force
        let dt = float32 time.Delta.TotalSeconds

        for (entityId, targetPos) in positions do
          // 1. Reconstruct Start Position
          let startPos =
            match velocities |> HashMap.tryFindV entityId with
            | ValueSome v -> targetPos - (v * dt)
            | ValueNone -> targetPos

          // 2. Determine Steps
          let displacement = targetPos - startPos
          let dist = displacement.Length()
          // Step size reduced for tighter precision with smaller radius
          let stepSize = 3.0f

          let numSteps =
            if dist > 0.0f then
              max 1 (int(MathF.Ceiling(dist / stepSize)))
            else
              1 // Always run at least one check to resolve static overlaps

          let stepMove =
            if numSteps > 1 then
              displacement / float32 numSteps
            else
              displacement

          // State for the stepping loop
          let mutable currentPos = startPos
          let mutable totalMTV = Vector2.Zero
          let mutable globalHasCollision = false
          let mutable lastCollidedObj = ValueNone

          // 3. Step Loop
          for step = 1 to numSteps do
            // Advance position
            if numSteps > 1 || dist > 0.0f then
              currentPos <- currentPos + stepMove
            else
              // Static case: just use targetPos (which equals startPos)
              currentPos <- targetPos

            // Run Iterative Solver at this step
            let mutable iteration = 0
            let maxIterations = 4
            let mutable doneIterating = false

            while not doneIterating && iteration < maxIterations do
              iteration <- iteration + 1
              let mutable iterationMTV = Vector2.Zero
              let mutable iterationHasCollision = false

              let entityPoly = getEntityPolygon currentPos
              let entityAxes = getAxes entityPoly

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

                    // Try polygon collision
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
                          | ValueSome p ->
                            core.EventBus.Publish(
                              {
                                EntityId = entityId
                                TargetMap = p.TargetMap
                                TargetSpawn = p.TargetSpawn
                              }
                              : SystemCommunications.PortalTravel
                            )
                          | ValueNone -> ()
                        else
                          iterationMTV <- iterationMTV + mtv
                          iterationHasCollision <- true
                          // Only update lastCollidedObj if it's a real wall collision
                          lastCollidedObj <- ValueSome obj
                      | ValueNone -> ()
                    | ValueNone ->
                      // Try polyline collision
                      match polylineObjects.TryFindV cacheKey with
                      | ValueSome chain ->
                        match
                          Spatial.circleChainMTV currentPos entityRadius chain
                        with
                        | ValueSome mtv ->
                          if isTrigger then
                            match obj.PortalData with
                            | ValueSome p ->
                              core.EventBus.Publish(
                                {
                                  EntityId = entityId
                                  TargetMap = p.TargetMap
                                  TargetSpawn = p.TargetSpawn
                                }
                                : SystemCommunications.PortalTravel
                              )
                            | ValueNone -> ()
                          else
                            iterationMTV <- iterationMTV + mtv
                            iterationHasCollision <- true
                            lastCollidedObj <- ValueSome obj
                        | ValueNone -> ()
                      | ValueNone -> ()

              if iterationHasCollision then
                currentPos <- currentPos + iterationMTV
                totalMTV <- totalMTV + iterationMTV
                globalHasCollision <- true
              else
                doneIterating <- true

          // 4. Publish Results
          if globalHasCollision then
            // Note: totalMTV here is the sum of all corrections across all steps.
            // But technically PositionChanged just needs the final valid position.
            // The MTV in MapObjectCollision is used for sliding.
            // Ideally it should be the normal of the wall we hit.
            // Summing them might produce a large vector if we slid along a wall for many steps.
            // However, for velocity cancellation, a large opposing vector is fine/good.

            core.EventBus.Publish(
              Physics(PositionChanged struct (entityId, currentPos))
            )

            match lastCollidedObj with
            | ValueSome obj ->
              core.EventBus.Publish(
                SystemCommunications.MapObjectCollision
                  struct (entityId, obj, totalMTV)
              )
            | ValueNone -> ()

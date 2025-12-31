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
open Pomo.Core.Domain.Projectile
open Pomo.Core.Domain.Particles
open Pomo.Core.Stores
open Pomo.Core.Domain.Events
open Pomo.Core.Systems.Systems
open Pomo.Core.Domain.Core

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

  // Composite key for caching: (groupId, objectId) to handle duplicate IDs across groups
  [<Struct>]
  type CacheKey = {
    GroupId: int
    ObjectId: int<ObjectId>
  }

  /// Checks collision against polygon or polyline shapes. Returns MTV if collision found.
  let inline checkShapeCollision
    (polygonObjects:
      HashMap<CacheKey, struct (IndexList<Vector2> * IndexList<Vector2>)>)
    (polylineObjects: HashMap<CacheKey, IndexList<Vector2>>)
    (entityPoly: IndexList<Vector2>)
    (entityAxes: IndexList<Vector2>)
    (entityPos: Vector2)
    (entityRadius: float32)
    (cacheKey: CacheKey)
    : Vector2 voption =
    // Try polygon collision first
    match polygonObjects.TryFindV cacheKey with
    | ValueSome struct (objPoly, objAxes) ->
      Spatial.intersectsMTVWithAxes entityPoly entityAxes objPoly objAxes
    | ValueNone ->
      // Try polyline collision
      match polylineObjects.TryFindV cacheKey with
      | ValueSome chain -> Spatial.circleChainMTV entityPos entityRadius chain
      | ValueNone -> ValueNone

  /// Result of processing a collision hit.
  [<Struct>]
  type CollisionResult =
    | NoHit
    | TriggerHit of portalData: Map.PortalData
    | WallHit of mtv: Vector2 * collidedObj: Map.MapObject

  /// Processes a shape collision hit and determines the result type.
  let inline processHitResult
    (findObj: CacheKey -> Map.MapObject option)
    (cacheKey: CacheKey)
    (mtv: Vector2)
    : CollisionResult =
    match findObj cacheKey with
    | Some obj ->
      match obj.PortalData with
      | ValueSome portalData -> TriggerHit portalData
      | ValueNone -> WallHit(mtv, obj)
    | None -> NoHit

  /// Context containing all cached map collision data for a scenario.
  /// Created once per scenario per Update call to avoid repeated parameter threading.
  [<Struct>]
  type MapCollisionContext = {
    MapDef: Map.MapDefinition
    ObjectGrid: HashMap<GridCell, IndexList<CacheKey>>
    PolygonObjects:
      HashMap<CacheKey, struct (IndexList<Vector2> * IndexList<Vector2>)>
    PolylineObjects: HashMap<CacheKey, IndexList<Vector2>>
  }

  type CollisionSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let stateWrite = env.CoreServices.StateWrite

    let spawnTerrainImpactEffect (vfxId: string) (pos: WorldPosition) =
      stores.ParticleStore.tryFind vfxId
      |> ValueOption.iter(fun configs ->
        let struct (billboardEmitters, meshEmitters) =
          splitEmittersByRenderMode configs

        let effect = {
          Id = System.Guid.NewGuid().ToString()
          Emitters = billboardEmitters
          MeshEmitters = meshEmitters
          Position = ref(Vector3(pos.X, pos.Y, pos.Z))
          Rotation = ref Quaternion.Identity
          Scale = ref Vector3.One
          IsAlive = ref true
          Owner = ValueNone
          Overrides = EffectOverrides.empty
        }

        core.World.VisualEffects.Add effect)

    // Cache for map object spatial grid
    // MapKey -> (Grid -> List of CacheKey)
    let mutable mapObjectGridCache =
      HashMap.empty<string, HashMap<GridCell, IndexList<CacheKey>>>

    // Cache for polygon colliders (for SAT)
    let mutable polygonCache =
      HashMap.empty<
        string,
        HashMap<CacheKey, struct (IndexList<Vector2> * IndexList<Vector2>)>
       >

    // Cache for polyline colliders (for segment-based collision)
    let mutable polylineCache =
      HashMap.empty<string, HashMap<CacheKey, IndexList<Vector2>>>

    // Reusable HashSet for deduplicating nearby objects (avoids allocation per frame)
    let nearbyObjectsSet = System.Collections.Generic.HashSet<CacheKey>()

    let getMapObjectGrid(map: MapDefinition) =
      match mapObjectGridCache.TryFindV map.Key with
      | ValueSome grid -> grid
      | ValueNone ->
        // Build the spatial grid for static map objects
        let cellSize = Core.Constants.Collision.GridCellSize

        let grid =
          map.ObjectGroups
          |> IndexList.fold
            (fun grid group ->
              group.Objects
              |> IndexList.fold
                (fun grid obj ->
                  let isCollidable =
                    match obj.Type with
                    | ValueSome MapObjectType.Wall -> true
                    | _ -> false

                  let isTrigger = obj.PortalData.IsValueSome

                  if isCollidable || isTrigger then
                    let struct (min, max) = Spatial.getMapObjectAABB obj

                    // Get all cells this object touches
                    let minCell = getGridCell cellSize min
                    let maxCell = getGridCell cellSize max

                    let key = {
                      GroupId = group.Id
                      ObjectId = obj.Id
                    }

                    // Add to every cell in range
                    let cells = [
                      for x in minCell.X .. maxCell.X do
                        for y in minCell.Y .. maxCell.Y do
                          yield { X = x; Y = y }
                    ]

                    cells
                    |> List.fold
                      (fun g c ->
                        match g |> HashMap.tryFindV c with
                        | ValueSome list ->
                          g |> HashMap.add c (list |> IndexList.add key)
                        | ValueNone ->
                          g |> HashMap.add c (IndexList.single key))
                      grid
                  else
                    grid)
                grid)
            HashMap.empty

        mapObjectGridCache <- mapObjectGridCache.Add(map.Key, grid)
        grid

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
      let liveProjectiles = core.World.LiveProjectiles |> AMap.force

      for scenarioId, scenario in scenarios do
        let snapshot = gameplay.Projections.ComputeMovementSnapshot(scenarioId)
        let grid = snapshot.SpatialGrid
        let positions = snapshot.Positions
        let getNearbyTo = getNearbyEntities grid

        // Create map collision context only for TileMap scenarios
        let mapCtxOpt =
          scenario.Map
          |> ValueOption.map(fun map -> {
            MapDef = map
            ObjectGrid = getMapObjectGrid map
            PolygonObjects = getPolygonObjects map
            PolylineObjects = getPolylineObjects map
          })

        // Check for entity-entity collisions
        for KeyValue(entityId, pos) in positions do
          let cell =
            getGridCell
              Core.Constants.Collision.GridCellSize
              (WorldPosition.toVector2 pos)

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

        // Check for map object collisions per scenario (TileMap only)
        mapCtxOpt
        |> ValueOption.iter(fun mapCtx ->
          // Get dependencies for CCD
          let velocities = core.World.Velocities
          let time = core.World.Time |> AVal.force
          let dt = float32 time.Delta.TotalSeconds

          // Cache object lookup for this frame/map
          let findObj(key: CacheKey) =
            mapCtx.MapDef.ObjectGroups
            |> IndexList.tryFind(fun _ g -> g.Id = key.GroupId)
            |> Option.bind(fun g ->
              g.Objects |> IndexList.tryFind(fun _ o -> o.Id = key.ObjectId))

          for KeyValue(entityId, targetPos) in positions do
            let projectileOpt = liveProjectiles |> HashMap.tryFindV entityId

            let shouldCheckWalls =
              match projectileOpt with
              | ValueSome proj -> proj.Info.Collision = BlockedByTerrain
              | ValueNone -> true

            if shouldCheckWalls then
              let startPos =
                match velocities.TryFindV entityId with
                | ValueSome v -> {
                    X = targetPos.X - v.X * dt
                    Y = targetPos.Y
                    Z = targetPos.Z - v.Y * dt
                  }
                | ValueNone -> targetPos

              let displacement =
                WorldPosition.toVector2 targetPos
                - WorldPosition.toVector2 startPos

              let dist = displacement.Length()
              let stepSize = 3.0f

              let numSteps =
                if dist > 0.0f then
                  max 1 (int(MathF.Ceiling(dist / stepSize)))
                else
                  1

              let stepMove =
                if numSteps > 1 then
                  displacement / float32 numSteps
                else
                  displacement

              let mutable currentPos = startPos
              let mutable totalMTV = Vector2.Zero
              let mutable globalHasCollision = false
              let mutable lastCollidedObj = ValueNone

              for step = 1 to numSteps do
                if numSteps > 1 || dist > 0.0f then
                  currentPos <- {
                    X = currentPos.X + stepMove.X
                    Y = currentPos.Y
                    Z = currentPos.Z + stepMove.Y
                  }
                else
                  currentPos <- targetPos

                let mutable iteration = 0
                let maxIterations = 4
                let mutable doneIterating = false

                while not doneIterating && iteration < maxIterations do
                  iteration <- iteration + 1
                  let mutable iterationMTV = Vector2.Zero
                  let mutable iterationHasCollision = false

                  let currentPos2d = WorldPosition.toVector2 currentPos
                  let entityPoly = getEntityPolygon currentPos2d
                  let entityAxes = getAxes entityPoly

                  let cell =
                    getGridCell
                      Core.Constants.Collision.GridCellSize
                      currentPos2d

                  nearbyObjectsSet.Clear()

                  for struct (dx, dy) in neighborOffsets do
                    let neighbor = { X = cell.X + dx; Y = cell.Y + dy }

                    match mapCtx.ObjectGrid |> HashMap.tryFindV neighbor with
                    | ValueSome keys ->
                      for key in keys do
                        nearbyObjectsSet.Add(key) |> ignore
                    | ValueNone -> ()

                  for cacheKey in nearbyObjectsSet do
                    let hitMTV =
                      checkShapeCollision
                        mapCtx.PolygonObjects
                        mapCtx.PolylineObjects
                        entityPoly
                        entityAxes
                        currentPos2d
                        entityRadius
                        cacheKey

                    match hitMTV with
                    | ValueSome mtv ->
                      match processHitResult findObj cacheKey mtv with
                      | TriggerHit portalData ->
                        core.EventBus.Publish(
                          GameEvent.Intent(
                            IntentEvent.Portal {
                              EntityId = entityId
                              TargetMap = portalData.TargetMap
                              TargetSpawn = portalData.TargetSpawn
                            }
                          )
                        )
                      | WallHit(wallMtv, obj) ->
                        iterationMTV <- iterationMTV + wallMtv
                        iterationHasCollision <- true
                        lastCollidedObj <- ValueSome obj
                      | NoHit -> ()
                    | ValueNone -> ()

                  if iterationHasCollision then
                    currentPos <- {
                      X = currentPos.X + iterationMTV.X
                      Y = currentPos.Y
                      Z = currentPos.Z + iterationMTV.Y
                    }

                    totalMTV <- totalMTV + iterationMTV
                    globalHasCollision <- true
                  else
                    doneIterating <- true

              if globalHasCollision then
                match projectileOpt with
                | ValueSome proj ->
                  let impact: SystemCommunications.ProjectileImpacted = {
                    ProjectileId = entityId
                    CasterId = proj.Caster
                    ImpactPosition = WorldPosition.toVector2 currentPos
                    TargetEntity = ValueNone
                    SkillId = proj.SkillId
                    RemainingJumps = ValueNone
                  }

                  match proj.Info.TerrainImpactVisuals with
                  | ValueSome visuals ->
                    match visuals.VfxId with
                    | ValueSome vfxId ->
                      spawnTerrainImpactEffect vfxId currentPos
                    | ValueNone -> ()
                  | ValueNone -> ()

                  core.EventBus.Publish(
                    GameEvent.Lifecycle(
                      LifecycleEvent.ProjectileImpacted impact
                    )
                  )

                  stateWrite.RemoveEntity(entityId)
                | ValueNone ->
                  stateWrite.UpdatePosition(entityId, currentPos)

                  match lastCollidedObj with
                  | ValueSome obj ->
                    core.EventBus.Publish(
                      GameEvent.Collision(
                        SystemCommunications.MapObjectCollision
                          struct (entityId, obj, totalMTV)
                      )
                    )
                  | ValueNone -> ())

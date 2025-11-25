namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Entity

module Spatial =

  [<Struct>]
  type GridCell = { X: int; Y: int }

  [<Struct>]
  type Cone = {
    Origin: Vector2
    Direction: Vector2
    AngleDegrees: float32
    Length: float32
  }

  [<Struct>]
  type LineSegment = {
    Start: Vector2
    End: Vector2
    Width: float32
  }

  [<Struct>]
  type Circle = { Center: Vector2; Radius: float32 }

  let isPointInCircle (circle: Circle) (point: Vector2) =
    Vector2.DistanceSquared(circle.Center, point)
    <= circle.Radius * circle.Radius

  let getGridCell (cellSize: float32) (position: Vector2) : GridCell = {
    X = int(position.X / cellSize)
    Y = int(position.Y / cellSize)
  }

  let getCellsInRadius (cellSize: float32) (center: Vector2) (radius: float32) =
    let minX = int((center.X - radius) / cellSize)
    let maxX = int((center.X + radius) / cellSize)
    let minY = int((center.Y - radius) / cellSize)
    let maxY = int((center.Y + radius) / cellSize)

    IndexList.ofArray [|
      for x in minX..maxX do
        for y in minY..maxY do
          { X = x; Y = y }
    |]

  // SAT Helpers
  let getAxes(points: IndexList<Vector2>) =
    points
    |> IndexList.pairwise
    |> IndexList.map(fun (p1, p2) ->
      let edge = p2 - p1
      Vector2(-edge.Y, edge.X) |> Vector2.Normalize)
    |> fun axes ->
        // Add last edge (closing the loop)
        match IndexList.tryLast points, IndexList.tryFirst points with
        | Some pLast, Some pFirst ->
          let edge = pFirst - pLast
          let lastAxis = Vector2(-edge.Y, edge.X) |> Vector2.Normalize
          IndexList.add lastAxis axes
        | _ -> axes

  let project (axis: Vector2) (points: IndexList<Vector2>) =
    let dots = points |> IndexList.map(fun p -> Vector2.Dot(p, axis))
    // IndexList doesn't have min/max directly, so convert to seq or fold
    let min = dots |> Seq.min
    let max = dots |> Seq.max
    struct (min, max)

  let overlap struct (minA, maxA) struct (minB, maxB) =
    not(maxA < minB || maxB < minA)

  let intersects (polyA: IndexList<Vector2>) (polyB: IndexList<Vector2>) =
    let axesA = getAxes polyA
    let axesB = getAxes polyB
    let allAxes = IndexList.append axesA axesB

    allAxes
    |> IndexList.forall(fun _ axis ->
      let projA = project axis polyA
      let projB = project axis polyB
      overlap projA projB)

  let intersectsMTVWithAxes
    (polyA: IndexList<Vector2>)
    (axesA: IndexList<Vector2>)
    (polyB: IndexList<Vector2>)
    (axesB: IndexList<Vector2>)
    : Vector2 voption =
    let allAxes = IndexList.append axesA axesB

    let mutable minOverlap = Single.MaxValue
    let mutable mtvAxis = Vector2.Zero
    let mutable separated = false

    for axis in allAxes do
      if not separated then
        let struct (minA, maxA) = project axis polyA
        let struct (minB, maxB) = project axis polyB

        if not(overlap (minA, maxA) (minB, maxB)) then
          separated <- true
        else
          let o = Math.Min(maxA, maxB) - Math.Max(minA, minB)

          if o < minOverlap then
            minOverlap <- o
            mtvAxis <- axis

    if separated then
      ValueNone
    else
      // Calculate centers for direction
      let centerA =
        polyA |> Seq.fold (+) Vector2.Zero |> (fun s -> s / float32 polyA.Count)

      let centerB =
        polyB |> Seq.fold (+) Vector2.Zero |> (fun s -> s / float32 polyB.Count)

      let direction = centerA - centerB

      if Vector2.Dot(direction, mtvAxis) < 0.0f then
        mtvAxis <- -mtvAxis

      ValueSome(mtvAxis * minOverlap)

  let intersectsMTV
    (polyA: IndexList<Vector2>)
    (polyB: IndexList<Vector2>)
    : Vector2 voption =
    let axesA = getAxes polyA
    let axesB = getAxes polyB
    intersectsMTVWithAxes polyA axesA polyB axesB

  let getEntityPolygon(pos: Vector2) =
    let halfSize = Core.Constants.Entity.Size.X / 2.0f // Assuming square

    IndexList.ofList [
      Vector2(pos.X - halfSize, pos.Y - halfSize)
      Vector2(pos.X + halfSize, pos.Y - halfSize)
      Vector2(pos.X + halfSize, pos.Y + halfSize)
      Vector2(pos.X - halfSize, pos.Y + halfSize)
    ]

  let rotate (v: Vector2) (radians: float32) =
    let cos = MathF.Cos radians
    let sin = MathF.Sin radians
    Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos)

  let getMapObjectPolygon(obj: Map.MapObject) =
    let radians = MathHelper.ToRadians obj.Rotation
    let pos = Vector2(obj.X, obj.Y)

    match obj.Points with
    | ValueSome points ->
      points |> IndexList.map(fun p -> rotate p radians + pos)
    | ValueNone ->
      // Rectangle
      let w = obj.Width
      let h = obj.Height

      let corners =
        IndexList.ofList [
          Vector2.Zero
          Vector2(w, 0.0f)
          Vector2(w, h)
          Vector2(0.0f, h)
        ]

      corners |> IndexList.map(fun p -> rotate p radians + pos)

  let isPointInCone (cone: Cone) (point: Vector2) =
    let distanceSquared = Vector2.DistanceSquared(cone.Origin, point)

    if distanceSquared > cone.Length * cone.Length then
      false
    else
      let offset = point - cone.Origin
      // Handle the case where point is exactly at origin to avoid normalizing zero vector
      if offset = Vector2.Zero then
        true // Point at origin is always in the cone
      else
        let toPoint = Vector2.Normalize(offset)
        let angleRadians = MathHelper.ToRadians(cone.AngleDegrees / 2.0f)
        let dot = Vector2.Dot(cone.Direction, toPoint)
        let cosAngle = MathF.Cos angleRadians
        dot >= cosAngle

  let isPointInLine (line: LineSegment) (point: Vector2) =
    let lineVec = line.End - line.Start
    let lineLenSq = lineVec.LengthSquared()

    if lineLenSq = 0.0f then
      Vector2.DistanceSquared(line.Start, point)
      <= line.Width / 2.0f * (line.Width / 2.0f)
    else
      let t = Vector2.Dot(point - line.Start, lineVec) / lineLenSq
      let tClamped = Math.Clamp(t, 0.0f, 1.0f)
      let projection = line.Start + lineVec * tClamped
      let distSq = Vector2.DistanceSquared(point, projection)
      distSq <= line.Width / 2.0f * (line.Width / 2.0f)

  module Isometric =
    open Pomo.Core.Domain.Map

    /// Converts from screen/world coordinates to isometric grid coordinates
    let screenToGrid
      (mapDef: MapDefinition)
      (screenX: float32, screenY: float32)
      =
      let tileW = float32 mapDef.TileWidth
      let tileH = float32 mapDef.TileHeight
      let originX = float32 mapDef.Width * tileW / 2.0f

      // Invert the isometric transformation:
      // screenX = originX + (gridX - gridY) * tileW / 2
      // screenY = (gridX + gridY) * tileH / 2
      //
      // Solving for gridX and gridY:
      // From second equation: gridX + gridY = 2 * screenY / tileH
      // From first equation: gridX - gridY = 2 * (screenX - originX) / tileW
      //
      // Adding: 2 * gridX = 2 * screenY / tileH + 2 * (screenX - originX) / tileW
      // So: gridX = screenY / tileH + (screenX - originX) / tileW
      //
      // Subtracting: 2 * gridY = 2 * screenY / tileH - 2 * (screenX - originX) / tileW
      // So: gridY = screenY / tileH - (screenX - originX) / tileW

      let gridX = screenY / tileH + (screenX - originX) / tileW
      let gridY = screenY / tileH - (screenX - originX) / tileW

      Vector2(gridX, gridY)

    /// Converts from isometric grid coordinates to screen/world coordinates
    let gridToScreen (mapDef: MapDefinition) (gridX: float32, gridY: float32) =
      let tileW = float32 mapDef.TileWidth
      let tileH = float32 mapDef.TileHeight
      let originX = float32 mapDef.Width * tileW / 2.0f

      // screenX = originX + (gridX - gridY) * tileW / 2
      // screenY = (gridX + gridY) * tileH / 2

      let screenX = originX + (gridX - gridY) * tileW / 2.0f
      let screenY = (gridX + gridY) * tileH / 2.0f

      Vector2(screenX, screenY)

    /// Checks if a point in screen coordinates is within an isometric line AOE
    /// lineStart and lineEnd are in screen coordinates
    /// Performs the check in isometric grid space to account for the projection
    let isPointInIsometricLine
      (mapDef: MapDefinition)
      (line: LineSegment)
      (point: Vector2)
      =
      // Convert all coordinates to isometric grid space
      let startGrid = screenToGrid mapDef (line.Start.X, line.Start.Y)
      let endGrid = screenToGrid mapDef (line.End.X, line.End.Y)
      let pointGrid = screenToGrid mapDef (point.X, point.Y)

      // Apply the same line algorithm but in isometric grid space
      let lineVec = endGrid - startGrid
      let lineLenSq = lineVec.LengthSquared()

      if lineLenSq = 0.0f then
        Vector2.DistanceSquared(startGrid, pointGrid)
        <= line.Width / 2.0f * (line.Width / 2.0f)
      else
        let t = Vector2.Dot(pointGrid - startGrid, lineVec) / lineLenSq
        let tClamped = System.Math.Clamp(t, 0.0f, 1.0f)
        let projection = startGrid + lineVec * tClamped
        let distSq = Vector2.DistanceSquared(pointGrid, projection)
        distSq <= line.Width / 2.0f * (line.Width / 2.0f)

    /// Checks if a point in screen coordinates is within an isometric cone AOE
    /// origin, direction, and point are in screen coordinates
    /// Performs the check in isometric grid space
    let isPointInIsometricCone
      (mapDef: MapDefinition)
      (cone: Cone)
      (point: Vector2)
      =
      // Convert coordinates to isometric grid space
      let originGrid = screenToGrid mapDef (cone.Origin.X, cone.Origin.Y)
      let pointGrid = screenToGrid mapDef (point.X, point.Y)

      // The direction vector also needs to be converted to grid space
      // To do this, we'll convert the destination point (origin + direction) to grid space,
      // then calculate the difference
      let originPlusDir = cone.Origin + cone.Direction

      let originPlusDirGrid =
        screenToGrid mapDef (originPlusDir.X, originPlusDir.Y)

      let directionGrid = Vector2.Normalize(originPlusDirGrid - originGrid)

      let distanceSquared = Vector2.DistanceSquared(originGrid, pointGrid)

      if distanceSquared > cone.Length * cone.Length then
        false
      else
        let offset = pointGrid - originGrid
        // Handle the case where point is exactly at origin to avoid normalizing zero vector
        if offset = Vector2.Zero then
          true // Point at origin is always in the cone
        else
          let toPoint = Vector2.Normalize(offset)

          let angleRadians =
            Microsoft.Xna.Framework.MathHelper.ToRadians(
              cone.AngleDegrees / 2.0f
            )

          let dot = Vector2.Dot(directionGrid, toPoint)
          let cosAngle = System.MathF.Cos angleRadians
          dot >= cosAngle

    /// Checks if a point in screen coordinates is within an isometric circle AOE
    let isPointInIsometricCircle
      (mapDef: MapDefinition)
      (circle: Circle)
      (point: Vector2)
      =
      // Convert to grid space for comparison
      let centerGrid = screenToGrid mapDef (circle.Center.X, circle.Center.Y)
      let pointGrid = screenToGrid mapDef (point.X, point.Y)

      // Apply anisotropic scaling to account for isometric distortion
      // In isometric projection, distances along the X and Y axes are compressed
      let dx = pointGrid.X - centerGrid.X
      let dy = pointGrid.Y - centerGrid.Y

      // The effective distance in isometric space needs to account for the projection
      // Use a weighted distance that accounts for the isometric distortion
      let effectiveDistance = sqrt((dx * dx) + (dy * dy))

      effectiveDistance <= circle.Radius

  module Search =
    open Pomo.Core.Domain.Map

    type SearchContext = {
      GetNearbyEntities:
        Vector2 -> float32 -> IndexList<struct (Guid<EntityId> * Vector2)>
    }

    [<Struct>]
    type CircleSearchRequest = {
      CasterId: Guid<EntityId>
      Circle: Circle
      MaxTargets: int
    }

    [<Struct>]
    type ConeSearchRequest = {
      CasterId: Guid<EntityId>
      Cone: Cone
      MaxTargets: int
    }

    [<Struct>]
    type LineSearchRequest = {
      CasterId: Guid<EntityId>
      Line: LineSegment
      MaxTargets: int
    }

    let findTargetsInCircle
      (ctx: SearchContext)
      (request: CircleSearchRequest)
      =
      let nearby =
        ctx.GetNearbyEntities request.Circle.Center request.Circle.Radius

      let targets =
        nearby
        |> IndexList.filter(fun struct (id, pos) ->
          id <> request.CasterId && isPointInCircle request.Circle pos)
        |> IndexList.sortBy(fun struct (_, pos) ->
          Vector2.DistanceSquared(request.Circle.Center, pos))
        |> IndexList.map(fun struct (id, _) -> id)

      if request.MaxTargets >= IndexList.count targets then
        targets
      else
        targets |> IndexList.take request.MaxTargets

    let findTargetsInCone (ctx: SearchContext) (request: ConeSearchRequest) =
      let nearby = ctx.GetNearbyEntities request.Cone.Origin request.Cone.Length

      let targets =
        nearby
        |> IndexList.filter(fun struct (id, pos) ->
          id <> request.CasterId && isPointInCone request.Cone pos)
        |> IndexList.sortBy(fun struct (_, pos) ->
          Vector2.DistanceSquared(request.Cone.Origin, pos))
        |> IndexList.map(fun struct (id, _) -> id)

      if request.MaxTargets >= IndexList.count targets then
        targets
      else
        targets |> IndexList.take request.MaxTargets

    let findTargetsInLine (ctx: SearchContext) (request: LineSearchRequest) =
      // Radius for broad phase is half the length + half the width, roughly, or just length from start
      let length = Vector2.Distance(request.Line.Start, request.Line.End)

      let nearby =
        ctx.GetNearbyEntities request.Line.Start (length + request.Line.Width)

      let targets =
        nearby
        |> IndexList.filter(fun struct (id, pos) ->
          id <> request.CasterId && isPointInLine request.Line pos)
        |> IndexList.sortBy(fun struct (_, pos) ->
          Vector2.DistanceSquared(request.Line.Start, pos))
        |> IndexList.map(fun struct (id, _) -> id)

      if request.MaxTargets >= IndexList.count targets then
        targets
      else
        targets |> IndexList.take request.MaxTargets

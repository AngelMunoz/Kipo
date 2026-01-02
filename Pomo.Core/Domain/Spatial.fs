namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Entity

module Spatial =

  [<Struct>]
  type GridCell3D = { X: int; Y: int; Z: int } // Y is height

  module GridCell3D =
    let inline fromVector3 (v: Vector3) (cellSize: float32) = {
      X = int(v.X / cellSize)
      Y = int(v.Y / cellSize)
      Z = int(v.Z / cellSize)
    }

    let inline toWorldPosition (c: GridCell3D) (cellSize: float32) =
      Vector3(
        float32 c.X * cellSize,
        float32 c.Y * cellSize,
        float32 c.Z * cellSize
      )

  [<Struct>]
  type Sphere = {
    Center: WorldPosition
    Radius: float32
  }

  [<Struct>]
  type Cone3D = {
    Origin: WorldPosition
    Direction: Vector3
    AngleDegrees: float32
    Length: float32
  }

  [<Struct>]
  type Cylinder = {
    Base: WorldPosition
    Height: float32
    Radius: float32
  }

  let inline isPointInSphere (sphere: Sphere) (point: WorldPosition) : bool =
    let dx = point.X - sphere.Center.X
    let dy = point.Y - sphere.Center.Y
    let dz = point.Z - sphere.Center.Z
    dx * dx + dy * dy + dz * dz <= sphere.Radius * sphere.Radius

  let inline isPointInCone3D (cone: Cone3D) (point: WorldPosition) : bool =
    let dx = point.X - cone.Origin.X
    let dy = point.Y - cone.Origin.Y
    let dz = point.Z - cone.Origin.Z
    let distSq = dx * dx + dy * dy + dz * dz

    // Outside length
    if distSq > cone.Length * cone.Length then
      false
    // Point at origin is always in cone
    elif distSq < 0.0001f then
      true
    else
      // Check angle
      let toPoint = Vector3(dx, dy, dz) |> Vector3.Normalize
      let dir = cone.Direction |> Vector3.Normalize
      let dot = Vector3.Dot(dir, toPoint)
      let halfAngleRad = MathHelper.ToRadians(cone.AngleDegrees / 2.0f)
      dot >= MathF.Cos halfAngleRad

  let inline isPointInCylinder (cyl: Cylinder) (point: WorldPosition) : bool =
    // Cylinder extends from Base.Y to Base.Y + Height
    // Check height bounds
    if point.Y < cyl.Base.Y || point.Y > cyl.Base.Y + cyl.Height then
      false
    else
      // Check XZ distance from cylinder axis
      let dx = point.X - cyl.Base.X
      let dz = point.Z - cyl.Base.Z
      dx * dx + dz * dz <= cyl.Radius * cyl.Radius

  type SearchContext3D = {
    /// Get entities near a world position within radius
    GetNearbyEntities:
      WorldPosition
        -> float32
        -> IndexList<struct (Guid<EntityId> * WorldPosition)>
  }

  [<Struct>]
  type SphereSearchRequest = {
    CasterId: Guid<EntityId>
    Sphere: Sphere
    MaxTargets: int
  }

  [<Struct>]
  type Cone3DSearchRequest = {
    CasterId: Guid<EntityId>
    Cone: Cone3D
    MaxTargets: int
  }

  [<Struct>]
  type CylinderSearchRequest = {
    CasterId: Guid<EntityId>
    Cylinder: Cylinder
    MaxTargets: int
  }

  module Search3D =

    let findTargetsInSphere
      (ctx: SearchContext3D)
      (request: SphereSearchRequest)
      : IndexList<Guid<EntityId>> =
      let nearby =
        ctx.GetNearbyEntities request.Sphere.Center request.Sphere.Radius

      // Single pass: filter and collect (id, distSq) pairs
      let candidates = ResizeArray<struct (Guid<EntityId> * float32)>()

      for struct (id, pos) in nearby do
        if id <> request.CasterId && isPointInSphere request.Sphere pos then
          let dx = pos.X - request.Sphere.Center.X
          let dy = pos.Y - request.Sphere.Center.Y
          let dz = pos.Z - request.Sphere.Center.Z
          candidates.Add(struct (id, dx * dx + dy * dy + dz * dz))

      // Sort by distance
      candidates.Sort(fun struct (_, d1) struct (_, d2) -> d1.CompareTo(d2))

      // Take up to MaxTargets and convert to IndexList
      let count = min request.MaxTargets candidates.Count
      let result = Array.zeroCreate<Guid<EntityId>> count

      for i in 0 .. count - 1 do
        let struct (id, _) = candidates.[i]
        result.[i] <- id

      IndexList.ofArray result

    let findTargetsInCone3D
      (ctx: SearchContext3D)
      (request: Cone3DSearchRequest)
      : IndexList<Guid<EntityId>> =
      let nearby = ctx.GetNearbyEntities request.Cone.Origin request.Cone.Length

      let candidates = ResizeArray<struct (Guid<EntityId> * float32)>()

      for struct (id, pos) in nearby do
        if id <> request.CasterId && isPointInCone3D request.Cone pos then
          let dx = pos.X - request.Cone.Origin.X
          let dy = pos.Y - request.Cone.Origin.Y
          let dz = pos.Z - request.Cone.Origin.Z
          candidates.Add struct (id, dx * dx + dy * dy + dz * dz)

      candidates.Sort(fun struct (_, d1) struct (_, d2) -> d1.CompareTo(d2))

      let count = min request.MaxTargets candidates.Count
      let result = Array.zeroCreate<Guid<EntityId>> count

      for i in 0 .. count - 1 do
        let struct (id, _) = candidates.[i]
        result.[i] <- id

      IndexList.ofArray result

    let findTargetsInCylinder
      (ctx: SearchContext3D)
      (request: CylinderSearchRequest)
      : IndexList<Guid<EntityId>> =
      // Broad phase: search using max of radius and height/2 from center
      let broadRadius =
        max request.Cylinder.Radius (request.Cylinder.Height / 2.0f)

      let centerY = request.Cylinder.Base.Y + request.Cylinder.Height / 2.0f

      let searchCenter: WorldPosition = {
        X = request.Cylinder.Base.X
        Y = centerY
        Z = request.Cylinder.Base.Z
      }

      let nearby = ctx.GetNearbyEntities searchCenter broadRadius

      let candidates = ResizeArray<struct (Guid<EntityId> * float32)>()

      for struct (id, pos) in nearby do
        if id <> request.CasterId && isPointInCylinder request.Cylinder pos then
          let dx = pos.X - request.Cylinder.Base.X
          let dz = pos.Z - request.Cylinder.Base.Z
          candidates.Add struct (id, dx * dx + dz * dz) // XZ distance from axis

      candidates.Sort(fun struct (_, d1) struct (_, d2) -> d1.CompareTo(d2))

      let count = min request.MaxTargets candidates.Count
      let result = Array.zeroCreate<Guid<EntityId>> count

      for i in 0 .. count - 1 do
        let struct (id, _) = candidates.[i]
        result.[i] <- id

      IndexList.ofArray result


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

  /// Calculates the closest point on a line segment to a given point
  let closestPointOnSegment (p: Vector2) (a: Vector2) (b: Vector2) : Vector2 =
    let ab = b - a
    let lenSq = ab.LengthSquared()

    if lenSq = 0.0f then
      a // Degenerate segment (a == b)
    else
      let t = Vector2.Dot(p - a, ab) / lenSq
      let tClamped = Math.Clamp(t, 0.0f, 1.0f)
      a + ab * tClamped

  /// Calculates the distance from a point to a line segment
  let distanceToSegment (p: Vector2) (a: Vector2) (b: Vector2) : float32 =
    let closest = closestPointOnSegment p a b
    Vector2.Distance(p, closest)

  /// Checks if a circle intersects a line segment, returning the MTV if so
  /// The MTV is the minimum translation vector to push the circle out of the segment
  let circleSegmentMTV
    (center: Vector2)
    (radius: float32)
    (a: Vector2)
    (b: Vector2)
    : Vector2 voption =
    let closest = closestPointOnSegment center a b
    let dist = Vector2.Distance(center, closest)

    if dist < radius then
      // Circle intersects the segment
      if dist = 0.0f then
        // Center is exactly on the segment, push perpendicular to segment
        let segDir = b - a

        if segDir.LengthSquared() = 0.0f then
          // Degenerate segment, push in any direction
          ValueSome(Vector2.UnitX * radius)
        else
          let perpendicular = Vector2.Normalize(Vector2(-segDir.Y, segDir.X))
          ValueSome(perpendicular * radius)
      else
        // Normal case: push away from closest point
        let pushDir = Vector2.Normalize(center - closest)
        let penetration = radius - dist
        ValueSome(pushDir * penetration)
    else
      ValueNone

  /// Checks a circle against an open chain of segments (polyline), returning combined MTV
  /// This iterates through each segment and accumulates all MTVs to handle corners properly
  /// Also checks vertices (polyline joints) to prevent slipping through sharp corners
  let circleChainMTV
    (center: Vector2)
    (radius: float32)
    (chain: IndexList<Vector2>)
    : Vector2 voption =
    if chain.Count < 2 then
      ValueNone
    else
      let mutable accumulatedMTV = Vector2.Zero
      let mutable hasCollision = false

      // Check each segment in the chain and accumulate all MTVs
      for i in 0 .. chain.Count - 2 do
        let a = chain.[i]
        let b = chain.[i + 1]

        match circleSegmentMTV center radius a b with
        | ValueSome mtv ->
          hasCollision <- true
          accumulatedMTV <- accumulatedMTV + mtv
        | ValueNone -> ()

      // Also check interior vertices (joints) explicitly
      // This prevents slipping through acute angle corners
      for i in 1 .. chain.Count - 2 do
        let vertex = chain.[i]
        let dist = Vector2.Distance(center, vertex)

        if dist < radius then
          hasCollision <- true

          if dist > 0.0f then
            let pushDir = Vector2.Normalize(center - vertex)
            let penetration = radius - dist
            accumulatedMTV <- accumulatedMTV + pushDir * penetration
          else
            // Entity center exactly on vertex, push in any direction
            accumulatedMTV <- accumulatedMTV + Vector2.UnitX * radius

      if hasCollision then ValueSome accumulatedMTV else ValueNone

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

  module Search =
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

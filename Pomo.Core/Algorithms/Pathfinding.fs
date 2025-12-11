namespace Pomo.Core.Algorithms

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.Navigation
open Pomo.Core.Domain.Map


module Pathfinding =

  module Grid =
    let worldToGrid (cellSize: float32) (pos: Vector2) : GridCell = {
      X = int(pos.X / cellSize)
      Y = int(pos.Y / cellSize)
    }

    let gridToWorld (cellSize: float32) (cell: GridCell) : Vector2 =
      Vector2(
        float32 cell.X * cellSize + cellSize * 0.5f,
        float32 cell.Y * cellSize + cellSize * 0.5f
      )

    let isValid (grid: NavGrid) (cell: GridCell) =
      cell.X >= 0 && cell.X < grid.Width && cell.Y >= 0 && cell.Y < grid.Height

    let isWalkable (grid: NavGrid) (cell: GridCell) =
      isValid grid cell && not grid.IsBlocked[cell.X, cell.Y]

    let getNeighbors (grid: NavGrid) (cell: GridCell) =
      let x, y = cell.X, cell.Y
      let neighbors = ResizeArray<GridCell>(8)

      let isWalkable x y = isWalkable grid { X = x; Y = y }

      let n = isWalkable x (y - 1)
      let s = isWalkable x (y + 1)
      let w = isWalkable (x - 1) y
      let e = isWalkable (x + 1) y

      if n then
        neighbors.Add({ X = x; Y = y - 1 })

      if s then
        neighbors.Add({ X = x; Y = y + 1 })

      if w then
        neighbors.Add({ X = x - 1; Y = y })

      if e then
        neighbors.Add({ X = x + 1; Y = y })

      // Diagonals
      // We allow diagonal movement even if orthogonal neighbors are blocked to traverse tight diagonal gaps.
      // The fine grid resolution (8.0f) and exact collision check at the target node provide sufficient safety.
      if isWalkable (x - 1) (y - 1) then
        neighbors.Add({ X = x - 1; Y = y - 1 })

      if isWalkable (x + 1) (y - 1) then
        neighbors.Add({ X = x + 1; Y = y - 1 })

      if isWalkable (x - 1) (y + 1) then
        neighbors.Add({ X = x - 1; Y = y + 1 })

      if isWalkable (x + 1) (y + 1) then
        neighbors.Add({ X = x + 1; Y = y + 1 })

      neighbors.ToArray()

    /// Generates a NavGrid from a MapDefinition.
    /// This version uses more accurate collision detection for entities of specific size
    let generate
      (map: MapDefinition)
      (cellSize: float32)
      (entitySize: Vector2)
      : NavGrid =
      let width =
        int(ceil(float32 map.Width * float32 map.TileWidth / cellSize))

      let height =
        int(ceil(float32 map.Height * float32 map.TileHeight / cellSize))

      let isBlocked = Array2D.create width height false
      let cost = Array2D.create width height 1.0f

      // Calculate the entity's collision box size for more accurate collision
      // Use the diagonal (circumscribed circle) to ensure corners don't clip walls
      // Add a small buffer to prevent "sliding" where physics detects collision but pathfinding didn't
      let entityRadius = (entitySize.Length() / 2.0f) + 2.0f

      // 1. Rasterize Walls
      for group in map.ObjectGroups do
        for obj in group.Objects do
          // Only consider Wall types as blocking for now
          let isWall =
            match obj.Type with
            | ValueSome MapObjectType.Wall -> true
            | _ -> false

          if isWall then
            let radians = MathHelper.ToRadians obj.Rotation
            let cos = MathF.Cos radians
            let sin = MathF.Sin radians

            let rotate(v: Vector2) =
              Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos)

            let pos = Vector2(obj.X, obj.Y)

            let rasterizePolygon (points: IndexList<Vector2>) (isClosed: bool) =
              let worldPoints =
                points |> Seq.map(fun p -> (rotate p) + pos) |> Seq.toArray

              // Calculate bounding box for search optimization
              let minPX =
                worldPoints |> Array.minBy(fun p -> p.X) |> (fun p -> p.X)

              let minPY =
                worldPoints |> Array.minBy(fun p -> p.Y) |> (fun p -> p.Y)

              let maxPX =
                worldPoints |> Array.maxBy(fun p -> p.X) |> (fun p -> p.X)

              let maxPY =
                worldPoints |> Array.maxBy(fun p -> p.Y) |> (fun p -> p.Y)

              // Expand search area by entity radius (approx) to catch all potential collisions
              let searchMinX = minPX - entityRadius
              let searchMinY = minPY - entityRadius
              let searchMaxX = maxPX + entityRadius
              let searchMaxY = maxPY + entityRadius

              let minX = max 0 (int(floor(searchMinX / cellSize)))
              let minY = max 0 (int(floor(searchMinY / cellSize)))
              let maxX = min (width - 1) (int(ceil(searchMaxX / cellSize)))
              let maxY = min (height - 1) (int(ceil(searchMaxY / cellSize)))

              // Helper: Segment Intersection
              let segmentsIntersect
                (a: Vector2)
                (b: Vector2)
                (c: Vector2)
                (d: Vector2)
                =
                let denominator =
                  ((b.X - a.X) * (d.Y - c.Y)) - ((b.Y - a.Y) * (d.X - c.X))

                if denominator = 0.0f then
                  false
                else
                  let numerator1 =
                    ((a.Y - c.Y) * (d.X - c.X)) - ((a.X - c.X) * (d.Y - c.Y))

                  let numerator2 =
                    ((a.Y - c.Y) * (b.X - a.X)) - ((a.X - c.X) * (b.Y - a.Y))

                  let r = numerator1 / denominator
                  let s = numerator2 / denominator
                  (r >= 0.0f && r <= 1.0f) && (s >= 0.0f && s <= 1.0f)

              // Helper: Point in Polygon (Ray Casting)
              let isPointInPolygon (p: Vector2) (poly: Vector2[]) =
                let mutable inside = false
                let count = poly.Length
                let mutable j = count - 1

                for i in 0 .. count - 1 do
                  let pi = poly.[i]
                  let pj = poly.[j]

                  if
                    ((pi.Y > p.Y) <> (pj.Y > p.Y))
                    && (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y)
                              + pi.X)
                  then
                    inside <- not inside

                  j <- i

                inside

              // Entity half dimensions
              let halfW = entitySize.X / 2.0f
              let halfH = entitySize.Y / 2.0f

              for x in minX..maxX do
                for y in minY..maxY do
                  if not isBlocked[x, y] then
                    let cellCenter = gridToWorld cellSize { X = x; Y = y }

                    // Define Entity AABB at this cell
                    let entMinX = cellCenter.X - halfW
                    let entMaxX = cellCenter.X + halfW
                    let entMinY = cellCenter.Y - halfH
                    let entMaxY = cellCenter.Y + halfH

                    // 1. Check if Cell Center is inside Polygon (Fastest check for large polygons)
                    if isClosed && isPointInPolygon cellCenter worldPoints then
                      isBlocked[x, y] <- true
                    else
                      // 2. Check if any Polygon Vertex is inside Entity AABB (Handles small polygons inside entity)
                      let mutable vertexInside = false
                      let count = worldPoints.Length
                      let mutable i = 0

                      while i < count && not vertexInside do
                        let p = worldPoints.[i]

                        if
                          p.X >= entMinX
                          && p.X <= entMaxX
                          && p.Y >= entMinY
                          && p.Y <= entMaxY
                        then
                          vertexInside <- true

                        i <- i + 1

                      if vertexInside then
                        isBlocked[x, y] <- true
                      else
                        // 3. Check Edge Intersection (Handles overlapping edges)
                        let mutable edgeIntersect = false
                        let mutable i = 0
                        let edgeCount = if isClosed then count else count - 1
                        // Entity Edges
                        let entCorners = [|
                          Vector2(entMinX, entMinY)
                          Vector2(entMaxX, entMinY)
                          Vector2(entMaxX, entMaxY)
                          Vector2(entMinX, entMaxY)
                        |]

                        while i < edgeCount && not edgeIntersect do
                          let p1 = worldPoints.[i]
                          let p2 = worldPoints.[(i + 1) % count]

                          // Check against all 4 entity edges
                          for j in 0..3 do
                            let e1 = entCorners.[j]
                            let e2 = entCorners.[(j + 1) % 4]

                            if segmentsIntersect p1 p2 e1 e2 then
                              edgeIntersect <- true

                          i <- i + 1

                        if edgeIntersect then
                          isBlocked[x, y] <- true

            // Determine the shape to rasterize based on CollisionShape
            match obj.CollisionShape with
            | ValueSome(Pomo.Core.Domain.Map.CollisionShape.Circle radius) ->
              // Handle Circle
              let localCenter = Vector2(radius, radius)
              let worldCenter = (rotate localCenter) + pos

              let searchRadius = radius + entityRadius

              let minX =
                max 0 (int(floor((worldCenter.X - searchRadius) / cellSize)))

              let minY =
                max 0 (int(floor((worldCenter.Y - searchRadius) / cellSize)))

              let maxX =
                min
                  (width - 1)
                  (int(ceil((worldCenter.X + searchRadius) / cellSize)))

              let maxY =
                min
                  (height - 1)
                  (int(ceil((worldCenter.Y + searchRadius) / cellSize)))

              for x in minX..maxX do
                for y in minY..maxY do
                  if not isBlocked[x, y] then
                    let cellCenter = gridToWorld cellSize { X = x; Y = y }
                    let relative = cellCenter - pos

                    let local =
                      Vector2(
                        relative.X * cos + relative.Y * sin,
                        -relative.X * sin + relative.Y * cos
                      )

                    let rx = radius + entityRadius
                    let distSq = Vector2.DistanceSquared(local, localCenter)

                    if distSq <= rx * rx then
                      isBlocked[x, y] <- true

            | ValueSome(Pomo.Core.Domain.Map.CollisionShape.EllipseShape(ew, eh)) ->
              // Handle Ellipse
              // Ellipse defined by bounding box at (X,Y) with ew, eh
              // We approximate collision by checking if a point is within the ellipse + entityRadius
              let localCenter = Vector2(ew / 2.0f, eh / 2.0f)
              let worldCenter = (rotate localCenter) + pos

              let radiusX = ew / 2.0f
              let radiusY = eh / 2.0f

              // Bounding box for the ellipse (approximate with rotation)
              // A safe bound is the max dimension / 2.0f + entityRadius (circumscribed radius)
              let maxDim = max ew eh
              let searchRadius = maxDim / 2.0f + entityRadius

              let minX =
                max 0 (int(floor((worldCenter.X - searchRadius) / cellSize)))

              let minY =
                max 0 (int(floor((worldCenter.Y - searchRadius) / cellSize)))

              let maxX =
                min
                  (width - 1)
                  (int(ceil((worldCenter.X + searchRadius) / cellSize)))

              let maxY =
                min
                  (height - 1)
                  (int(ceil((worldCenter.Y + searchRadius) / cellSize)))

              for x in minX..maxX do
                for y in minY..maxY do
                  if not isBlocked[x, y] then
                    let cellCenter = gridToWorld cellSize { X = x; Y = y }

                    // Transform cell center back to local ellipse space
                    let relative = cellCenter - pos
                    // Inverse rotate
                    let local =
                      Vector2(
                        relative.X * cos + relative.Y * sin,
                        -relative.X * sin + relative.Y * cos
                      )

                    // Check if inside ellipse (expanded by entityRadius)
                    // (dx/rx)^2 + (dy/ry)^2 <= 1
                    // We add entityRadius to radii to approximate expansion
                    let rx = radiusX + entityRadius
                    let ry = radiusY + entityRadius

                    let dx = local.X - radiusX // relative to center (radiusX, radiusY)
                    let dy = local.Y - radiusY

                    if
                      (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry) <= 1.0f
                    then
                      isBlocked[x, y] <- true

            | ValueSome(Pomo.Core.Domain.Map.CollisionShape.RectangleShape(w, h)) ->
              let points =
                IndexList.ofList [
                  Vector2.Zero
                  Vector2(w, 0.0f)
                  Vector2(w, h)
                  Vector2(0.0f, h)
                ]

              rasterizePolygon points true

            | ValueSome(Pomo.Core.Domain.Map.CollisionShape.ClosedPolygon points) ->
              rasterizePolygon points true

            | ValueSome(Pomo.Core.Domain.Map.CollisionShape.OpenPolyline points) ->
              rasterizePolygon points false

            | ValueNone -> ()


      {
        Width = width
        Height = height
        CellSize = cellSize
        IsBlocked = isBlocked
        Cost = cost
      }

  module AStar =

    let private heuristic (a: GridCell) (b: GridCell) : float32 =
      // Euclidean distance for heuristic
      let dx = float32(a.X - b.X)
      let dy = float32(a.Y - b.Y)
      MathF.Sqrt(dx * dx + dy * dy)

    let private reconstructPath
      (grid: NavGrid)
      (endNode: NavNode)
      (closedSet: Dictionary<GridCell, NavNode>)
      : Vector2 list =
      let rec buildPath node acc =
        let worldPos = Grid.gridToWorld grid.CellSize node.Position
        let newAcc = worldPos :: acc

        match node.Parent with
        | ValueSome p ->
          match closedSet.TryGetValue p with
          | true, parentNode -> buildPath parentNode newAcc
          | false, _ -> newAcc
        | ValueNone -> newAcc

      buildPath endNode []

    let private hasLineOfSight
      (grid: NavGrid)
      (startPos: Vector2)
      (endPos: Vector2)
      =
      let dist = Vector2.Distance(startPos, endPos)

      if dist < grid.CellSize then
        true
      else
        let steps = ceil(dist / (grid.CellSize / 2.0f))
        let stepVec = (endPos - startPos) / steps
        let mutable current = startPos
        let mutable clear = true
        let mutable i = 0.0f

        while i < steps && clear do
          current <- current + stepVec
          let cell = Grid.worldToGrid grid.CellSize current

          if not(Grid.isWalkable grid cell) then
            clear <- false

          i <- i + 1.0f

        clear

    let private smoothPath (grid: NavGrid) (path: Vector2 list) =
      match path with
      | [] -> []
      | [ x ] -> [ x ]
      | start :: rest ->
        let rec optimize current remaining acc =
          match remaining with
          | [] -> List.rev(current :: acc)
          | [ next ] -> List.rev(next :: current :: acc)
          | next :: after :: others ->
            if hasLineOfSight grid current after then
              optimize current (after :: others) acc
            else
              optimize next (after :: others) (current :: acc)

        optimize start rest []

    let findPath
      (grid: NavGrid)
      (startPos: Vector2)
      (endPos: Vector2)
      : Vector2 list voption =
      let startCell = Grid.worldToGrid grid.CellSize startPos
      let endCell = Grid.worldToGrid grid.CellSize endPos

      if not(Grid.isWalkable grid endCell) then
        ValueNone
      elif startCell = endCell then
        ValueSome [ endPos ]
      else
        let openSet = PriorityQueue<NavNode, float32>()
        let closedSet = Dictionary<GridCell, NavNode>()
        let gScores = Dictionary<GridCell, float32>()

        let hStart = heuristic startCell endCell

        let startNode = {
          Position = startCell
          Parent = ValueNone
          Cost = { G = 0f; H = hStart; F = hStart }
        }

        openSet.Enqueue(startNode, hStart)
        gScores[startCell] <- 0f

        let mutable result = ValueNone

        while openSet.Count > 0 && result.IsValueNone do
          let current = openSet.Dequeue()

          if current.Position = endCell then
            result <- ValueSome current
          else if not(closedSet.ContainsKey current.Position) then
            closedSet[current.Position] <- current

            let neighbors = Grid.getNeighbors grid current.Position

            for neighborCell in neighbors do
              if not(closedSet.ContainsKey neighborCell) then
                // Calculate distance as 1.0 for orthogonal, 1.414 for diagonal
                let dx = abs(neighborCell.X - current.Position.X)
                let dy = abs(neighborCell.Y - current.Position.Y)
                let d = if dx = 1 && dy = 1 then 1.414f else 1.0f

                let newG =
                  current.Cost.G + d * grid.Cost[neighborCell.X, neighborCell.Y]

                let bestG =
                  match gScores.TryGetValue neighborCell with
                  | true, v -> v
                  | false, _ -> Single.MaxValue

                if newG < bestG then
                  gScores[neighborCell] <- newG
                  let newH = heuristic neighborCell endCell
                  let newF = newG + newH

                  let neighborNode = {
                    Position = neighborCell
                    Parent = ValueSome current.Position
                    Cost = { G = newG; H = newH; F = newF }
                  }

                  openSet.Enqueue(neighborNode, newF)

        match result with
        | ValueSome endNode ->
          let path = reconstructPath grid endNode closedSet

          // Remove the start node from the path to avoid snapping back to the center of the current tile
          let pathWithoutStart =
            match path with
            | _ :: rest when not(List.isEmpty rest) -> rest
            | _ -> path

          let pathWithExactEnd =
            match pathWithoutStart with
            | [] -> [ endPos ]
            | _ ->
              let pathPrefix =
                pathWithoutStart |> List.take(pathWithoutStart.Length - 1)

              pathPrefix @ [ endPos ]

          // Apply path smoothing
          let smoothedPath = smoothPath grid pathWithExactEnd
          ValueSome smoothedPath
        | ValueNone -> ValueNone

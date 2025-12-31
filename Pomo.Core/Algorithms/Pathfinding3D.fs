namespace Pomo.Core.Algorithms

open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.BlockMap

/// 3D A* pathfinding on BlockMap grids
module Pathfinding3D =

  [<Struct>]
  type NavGrid3D = {
    BlockMap: BlockMapDefinition
    CellSize: float32
  }

  let inline private toCell
    (grid: NavGrid3D)
    (pos: WorldPosition)
    : GridCell3D =
    {
      X = int(pos.X / grid.CellSize)
      Y = int(pos.Y / grid.CellSize)
      Z = int(pos.Z / grid.CellSize)
    }

  let inline private toWorldPosition
    (grid: NavGrid3D)
    (cell: GridCell3D)
    : WorldPosition =
    {
      X = (float32 cell.X + 0.5f) * grid.CellSize
      Y = float32 cell.Y * grid.CellSize
      Z = (float32 cell.Z + 0.5f) * grid.CellSize
    }

  /// Check if cell is walkable: empty or NoCollision, with solid floor below
  let isWalkable (grid: NavGrid3D) (cell: GridCell3D) : bool =
    let map = grid.BlockMap

    // Bounds check
    if
      cell.X < 0
      || cell.X >= map.Width
      || cell.Y < 0
      || cell.Y >= map.Height
      || cell.Z < 0
      || cell.Z >= map.Depth
    then
      false
    else
      // Check current cell is empty or NoCollision
      let currentCellFree =
        match map.Blocks.TryGetValue cell with
        | true, block ->
          match map.Palette.TryGetValue block.BlockTypeId with
          | true, blockType -> blockType.CollisionType = NoCollision
          | false, _ -> true
        | false, _ -> true

      // Check cell below is solid (floor)
      let hasFloor =
        if cell.Y = 0 then
          true // Ground level always has floor
        else
          let belowCell = { cell with Y = cell.Y - 1 }

          match map.Blocks.TryGetValue belowCell with
          | true, block ->
            match map.Palette.TryGetValue block.BlockTypeId with
            | true, blockType ->
              match blockType.CollisionType with
              | Box
              | Mesh -> true
              | NoCollision -> false
            | false, _ -> false
          | false, _ -> false

      currentCellFree && hasFloor

  /// Get walkable neighbors (6-connected: up/down/left/right/forward/backward in XZ, plus Y transitions)
  let getNeighbors (grid: NavGrid3D) (cell: GridCell3D) : GridCell3D[] =
    let results = ResizeArray<GridCell3D>(8)

    // XZ neighbors (same level)
    let directions = [|
      struct (1, 0)
      struct (-1, 0)
      struct (0, 1)
      struct (0, -1)
    |]

    for struct (dx, dz) in directions do
      let neighbor = {
        X = cell.X + dx
        Y = cell.Y
        Z = cell.Z + dz
      }

      if isWalkable grid neighbor then
        results.Add neighbor
      else
        // Check if can step up
        let upNeighbor = { neighbor with Y = neighbor.Y + 1 }

        if isWalkable grid upNeighbor then
          results.Add upNeighbor
        // Check if can step down
        let downNeighbor = { neighbor with Y = neighbor.Y - 1 }

        if downNeighbor.Y >= 0 && isWalkable grid downNeighbor then
          results.Add downNeighbor

    results.ToArray()

  /// 3D distance heuristic
  let inline private heuristic (a: GridCell3D) (b: GridCell3D) : float32 =
    let dx = float32(abs(a.X - b.X))
    let dy = float32(abs(a.Y - b.Y))
    let dz = float32(abs(a.Z - b.Z))
    sqrt(dx * dx + dy * dy + dz * dz)

  /// A* pathfinding in 3D
  let findPath
    (grid: NavGrid3D)
    (startPos: WorldPosition)
    (endPos: WorldPosition)
    : WorldPosition list voption =

    let startCell = toCell grid startPos
    let endCell = toCell grid endPos

    if not(isWalkable grid startCell) || not(isWalkable grid endCell) then
      ValueNone
    else
      let openSet = SortedSet<struct (float32 * GridCell3D)>()
      let cameFrom = Dictionary<GridCell3D, GridCell3D>()
      let gScore = Dictionary<GridCell3D, float32>()

      gScore[startCell] <- 0.0f
      openSet.Add(struct (heuristic startCell endCell, startCell)) |> ignore

      let mutable found = false
      let mutable current = startCell

      while not found && openSet.Count > 0 do
        let struct (_, cell) = openSet.Min
        openSet.Remove(openSet.Min) |> ignore
        current <- cell

        if current = endCell then
          found <- true
        else
          let neighbors = getNeighbors grid current

          for neighbor in neighbors do
            let tentativeG = gScore[current] + heuristic current neighbor

            let currentG =
              match gScore.TryGetValue neighbor with
              | true, g -> g
              | false, _ -> infinityf

            if tentativeG < currentG then
              cameFrom[neighbor] <- current
              gScore[neighbor] <- tentativeG
              let fScore = tentativeG + heuristic neighbor endCell
              openSet.Add(struct (fScore, neighbor)) |> ignore

      if found then
        // Reconstruct path
        let path = ResizeArray<WorldPosition>()
        let mutable node = endCell

        while cameFrom.ContainsKey node do
          path.Add(toWorldPosition grid node)
          node <- cameFrom[node]

        path.Reverse()

        if path.Count > 0 then
          ValueSome(List.ofSeq path)
        else
          ValueNone
      else
        ValueNone

  /// Check line of sight between two positions (simple grid raycast)
  let hasLineOfSight
    (grid: NavGrid3D)
    (startPos: WorldPosition)
    (endPos: WorldPosition)
    : bool =
    let startCell = toCell grid startPos
    let endCell = toCell grid endPos
    let dx = endCell.X - startCell.X
    let dy = endCell.Y - startCell.Y
    let dz = endCell.Z - startCell.Z
    let steps = max (max (abs dx) (abs dy)) (abs dz)

    if steps = 0 then
      true
    else
      let mutable blocked = false

      for i = 1 to steps do
        if not blocked then
          let t = float32 i / float32 steps

          let cell: GridCell3D = {
            X = startCell.X + int(float32 dx * t)
            Y = startCell.Y + int(float32 dy * t)
            Z = startCell.Z + int(float32 dz * t)
          }

          if not(isWalkable grid cell) then
            blocked <- true

      not blocked

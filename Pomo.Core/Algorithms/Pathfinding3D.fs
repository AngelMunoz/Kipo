namespace Pomo.Core.Algorithms

open System.Collections.Generic
open FSharp.UMX
open Pomo.Core
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

  module Grid =

    let inline worldToCell (grid: NavGrid3D) (pos: WorldPosition) : GridCell3D = {
      X = int(pos.X / grid.CellSize)
      Y = int(pos.Y / grid.CellSize)
      Z = int(pos.Z / grid.CellSize)
    }

    let inline cellToWorld
      (grid: NavGrid3D)
      (cell: GridCell3D)
      : WorldPosition =
      {
        X = (float32 cell.X + 0.5f) * grid.CellSize
        Y = float32 cell.Y * grid.CellSize
        Z = (float32 cell.Z + 0.5f) * grid.CellSize
      }

    let inline private isInBounds (map: BlockMapDefinition) (cell: GridCell3D) =
      cell.X >= 0
      && cell.X < map.Width
      && cell.Y >= 0
      && cell.Y < map.Height
      && cell.Z >= 0
      && cell.Z < map.Depth

    let isWalkableWithHeight
      (grid: NavGrid3D)
      (entityHeight: float32)
      (cell: GridCell3D)
      : bool =
      isInBounds grid.BlockMap cell
      && Spatial3D.canStandInCellWithConfig
        ValueNone
        grid.BlockMap
        cell
        entityHeight

    let isWalkable (grid: NavGrid3D) (cell: GridCell3D) : bool =
      isWalkableWithHeight grid grid.CellSize cell

    /// XZ movement directions (same Y level)
    /// Cardinals + Diagonals for smooth corner navigation
    let private cardinalDirections = [|
      struct (1, 0)
      struct (-1, 0)
      struct (0, 1)
      struct (0, -1)
    |]

    let private diagonalDirections = [|
      struct (1, 1)
      struct (1, -1)
      struct (-1, 1)
      struct (-1, -1)
    |]

    /// Get walkable neighbors into provided ResizeArray (avoids allocation)
    /// Includes diagonals with corner-cutting prevention
    let getNeighborsIntoWithHeight
      (grid: NavGrid3D)
      (entityHeight: float32)
      (cell: GridCell3D)
      (results: ResizeArray<GridCell3D>)
      =
      results.Clear()

      // Cardinal directions - always check
      for struct (dx, dz) in cardinalDirections do
        let neighbor = {
          X = cell.X + dx
          Y = cell.Y
          Z = cell.Z + dz
        }

        if isWalkableWithHeight grid entityHeight neighbor then
          results.Add neighbor

      // Diagonal directions - only if adjacent cardinals are walkable
      // This prevents corner-cutting through walls
      for struct (dx, dz) in diagonalDirections do
        let neighbor = {
          X = cell.X + dx
          Y = cell.Y
          Z = cell.Z + dz
        }

        // Check adjacent cardinal cells to prevent cutting corners
        let adjX = { cell with X = cell.X + dx }
        let adjZ = { cell with Z = cell.Z + dz }

        let canMoveDiagonally =
          isWalkableWithHeight grid entityHeight adjX
          && isWalkableWithHeight grid entityHeight adjZ

        if
          canMoveDiagonally && isWalkableWithHeight grid entityHeight neighbor
        then
          results.Add neighbor

    let getNeighborsInto
      (grid: NavGrid3D)
      (cell: GridCell3D)
      (results: ResizeArray<GridCell3D>)
      =
      getNeighborsIntoWithHeight grid grid.CellSize cell results

  module AStar =

    /// 3D Euclidean distance heuristic
    let inline private heuristic (a: GridCell3D) (b: GridCell3D) : float32 =
      let dx = float32(abs(a.X - b.X))
      let dy = float32(abs(a.Y - b.Y))
      let dz = float32(abs(a.Z - b.Z))
      sqrt(dx * dx + dy * dy + dz * dz)

    let hasLineOfSightWithHeight
      (grid: NavGrid3D)
      (entityHeight: float32)
      (startPos: WorldPosition)
      (endPos: WorldPosition)
      : bool =
      Spatial3D.canTraverseWithConfig
        ValueNone
        grid.BlockMap
        startPos
        endPos
        entityHeight
        grid.CellSize

    let hasLineOfSight
      (grid: NavGrid3D)
      (startPos: WorldPosition)
      (endPos: WorldPosition)
      : bool =
      hasLineOfSightWithHeight grid grid.CellSize startPos endPos

    /// Smooth path by removing intermediate waypoints when there's line of sight
    let private smoothPath
      (grid: NavGrid3D)
      (entityHeight: float32)
      (path: WorldPosition[])
      =
      if path.Length <= 2 then
        path
      else
        let result = ResizeArray<WorldPosition>()
        result.Add path[0]
        let mutable current = 0

        while current < path.Length - 1 do
          let mutable farthest = current + 1

          for i = current + 2 to path.Length - 1 do
            if
              hasLineOfSightWithHeight grid entityHeight path[current] path[i]
            then
              farthest <- i

          result.Add path[farthest]
          current <- farthest

        result.ToArray()

    /// A* pathfinding in 3D
    let findPathWithHeight
      (grid: NavGrid3D)
      (entityHeight: float32)
      (startPos: WorldPosition)
      (endPos: WorldPosition)
      : WorldPosition[] voption =

      let startCell = Grid.worldToCell grid startPos
      let endCell = Grid.worldToCell grid endPos

      if
        not(Grid.isWalkableWithHeight grid entityHeight startCell)
        || not(Grid.isWalkableWithHeight grid entityHeight endCell)
      then
        ValueNone
      else
        let openSet = SortedSet<struct (float32 * GridCell3D)>()
        let cameFrom = Dictionary<GridCell3D, GridCell3D>()
        let gScore = Dictionary<GridCell3D, float32>()
        let neighbors = ResizeArray<GridCell3D>(8)

        gScore[startCell] <- 0.0f
        openSet.Add struct (heuristic startCell endCell, startCell) |> ignore

        let mutable found = false
        let mutable current = startCell

        while not found && openSet.Count > 0 do
          let struct (_, cell) = openSet.Min
          openSet.Remove openSet.Min |> ignore
          current <- cell

          if current = endCell then
            found <- true
          else
            Grid.getNeighborsIntoWithHeight grid entityHeight current neighbors

            for neighbor in neighbors do
              let tentativeG =
                (gScore
                 |> Dictionary.tryFindV current
                 |> ValueOption.defaultValue infinityf)
                + heuristic current neighbor

              let currentG =
                gScore
                |> Dictionary.tryFindV neighbor
                |> ValueOption.defaultValue infinityf

              if tentativeG < currentG then
                cameFrom[neighbor] <- current
                gScore[neighbor] <- tentativeG
                let fScore = tentativeG + heuristic neighbor endCell
                openSet.Add struct (fScore, neighbor) |> ignore

        if found then
          // Reconstruct path
          let path = ResizeArray<WorldPosition>()
          let mutable node = endCell

          while cameFrom.ContainsKey node do
            path.Add(Grid.cellToWorld grid node)
            node <- cameFrom[node]

          path.Reverse()

          if path.Count > 0 then
            let rawPath = path.ToArray()
            let smoothed = smoothPath grid entityHeight rawPath
            ValueSome smoothed
          else
            ValueNone
        else
          ValueNone

    let findPath
      (grid: NavGrid3D)
      (startPos: WorldPosition)
      (endPos: WorldPosition)
      : WorldPosition[] voption =
      findPathWithHeight grid grid.CellSize startPos endPos

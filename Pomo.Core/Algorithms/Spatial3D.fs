namespace Pomo.Core.Algorithms

open System
open System.Collections.Generic
open Pomo.Core
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Core.Constants

module Spatial3D =

  [<Struct>]
  type QueryConfig = {
    IsBlockingCell: BlockMapDefinition -> GridCell3D -> bool
    TryGetSupportYAtXZ: BlockMapDefinition -> WorldPosition -> float32 voption
  }

  let inline private getCellAtXZ(pos: WorldPosition) : struct (int * int) =
    struct (int(pos.X / BlockMap.CellSize), int(pos.Z / BlockMap.CellSize))

  let inline private inXZBounds (map: BlockMapDefinition) (cx: int) (cz: int) =
    cx >= 0 && cx < map.Width && cz >= 0 && cz < map.Depth

  let inline private tryGetBlockType
    (map: BlockMapDefinition)
    (cell: GridCell3D)
    : BlockType voption =
    map.Blocks
    |> Dictionary.tryFindV cell
    |> ValueOption.bind(fun block ->
      map.Palette |> Dictionary.tryFindV block.BlockTypeId)

  let inline private defaultIsBlockingCell
    (map: BlockMapDefinition)
    (cell: GridCell3D)
    : bool =
    match tryGetBlockType map cell with
    | ValueSome blockType ->
      match blockType.CollisionType with
      | NoCollision -> false
      | Box
      | Mesh -> true
    | ValueNone -> false

  let private defaultTryGetSupportYAtXZ
    (map: BlockMapDefinition)
    (pos: WorldPosition)
    : float32 voption =
    let struct (cx, cz) = getCellAtXZ pos

    if not(inXZBounds map cx cz) then
      ValueNone
    else
      let mutable found = ValueNone
      let mutable y = map.Height - 1

      while y >= 0 && found.IsNone do
        let cell: GridCell3D = { X = cx; Y = y; Z = cz }

        if defaultIsBlockingCell map cell then
          found <- ValueSome(float32(y + 1) * BlockMap.CellSize)

        y <- y - 1

      found |> ValueOption.defaultValue 0.0f |> ValueSome

  let DefaultConfig: QueryConfig = {
    IsBlockingCell = defaultIsBlockingCell
    TryGetSupportYAtXZ = defaultTryGetSupportYAtXZ
  }

  let tryGetSurfaceHeightWithConfig
    (config: QueryConfig voption)
    (map: BlockMapDefinition)
    (pos: WorldPosition)
    : float32 voption =
    let cfg = defaultValueArg config DefaultConfig
    cfg.TryGetSupportYAtXZ map pos

  let canOccupyWithConfig
    (config: QueryConfig voption)
    (map: BlockMapDefinition)
    (pos: WorldPosition)
    (entityHeight: float32)
    : bool =
    let cfg = defaultValueArg config DefaultConfig
    let radius = Entity.CollisionRadius

    let checkPoint(p: WorldPosition) =
      let struct (cx, cz) = getCellAtXZ p

      if not(inXZBounds map cx cz) then
        false
      else
        let entityCellY = int(p.Y / BlockMap.CellSize)

        let heightInCells =
          entityHeight
          |> fun h -> if h > 0.0f then h else BlockMap.CellSize
          |> fun h -> int(MathF.Ceiling(h / BlockMap.CellSize))
          |> max 1

        let mutable blocked = false
        let mutable y = entityCellY
        let yEnd = entityCellY + heightInCells - 1

        while not blocked && y <= yEnd do
          if y < 0 || y >= map.Height then
            blocked <- true
          else
            let cell: GridCell3D = { X = cx; Y = y; Z = cz }

            if cfg.IsBlockingCell map cell then
              blocked <- true

          y <- y + 1

        not blocked

    // Check center and 4 points around the radius for "fat" collision
    checkPoint pos
    && checkPoint { pos with X = pos.X + radius }
    && checkPoint { pos with X = pos.X - radius }
    && checkPoint { pos with Z = pos.Z + radius }
    && checkPoint { pos with Z = pos.Z - radius }

  let tryProjectToGroundWithConfig
    (config: QueryConfig voption)
    (map: BlockMapDefinition)
    (pos: WorldPosition)
    : WorldPosition voption =
    let cfg = defaultValueArg config DefaultConfig

    let struct (cx, cz) = getCellAtXZ pos

    if not(inXZBounds map cx cz) then
      ValueNone
    else
      let startY = int(pos.Y / BlockMap.CellSize) - 1

      if startY < 0 then
        ValueSome { pos with Y = 0.0f }
      else
        let mutable found = ValueNone
        let mutable y = min (map.Height - 1) startY

        while y >= 0 && found.IsNone do
          let cell: GridCell3D = { X = cx; Y = y; Z = cz }

          if cfg.IsBlockingCell map cell then
            found <- ValueSome(float32(y + 1) * BlockMap.CellSize)

          y <- y - 1

        let groundedY = found |> ValueOption.defaultValue 0.0f
        ValueSome { pos with Y = groundedY }

  let canStandWithConfig
    (config: QueryConfig voption)
    (map: BlockMapDefinition)
    (pos: WorldPosition)
    (entityHeight: float32)
    : bool =
    match tryProjectToGroundWithConfig config map pos with
    | ValueSome grounded -> canOccupyWithConfig config map grounded entityHeight
    | ValueNone -> false

  let canStandInCellWithConfig
    (config: QueryConfig voption)
    (map: BlockMapDefinition)
    (cell: GridCell3D)
    (entityHeight: float32)
    : bool =
    let cfg = defaultValueArg config DefaultConfig

    if
      cell.X < 0
      || cell.X >= map.Width
      || cell.Z < 0
      || cell.Z >= map.Depth
      || cell.Y < 0
      || cell.Y >= map.Height
    then
      false
    else
      let hasSupport =
        if cell.Y = 0 then
          true
        else
          let below = { cell with Y = cell.Y - 1 }
          cfg.IsBlockingCell map below

      if not hasSupport then
        false
      else
        let pos: WorldPosition = {
          X = (float32 cell.X + 0.5f) * BlockMap.CellSize
          Y = float32 cell.Y * BlockMap.CellSize
          Z = (float32 cell.Z + 0.5f) * BlockMap.CellSize
        }

        canOccupyWithConfig config map pos entityHeight

  let canTraverseWithConfig
    (config: QueryConfig voption)
    (map: BlockMapDefinition)
    (startPos: WorldPosition)
    (targetPos: WorldPosition)
    (entityHeight: float32)
    (maxStepHeight: float32)
    : bool =
    let cfg = defaultValueArg config DefaultConfig

    let dx = targetPos.X - startPos.X
    let dz = targetPos.Z - startPos.Z

    let maxDelta = max (abs dx) (abs dz)

    if maxDelta <= 0.0001f then
      canStandWithConfig config map startPos entityHeight
    else
      let sampleStep = BlockMap.CellSize * 0.5f

      let steps = int(MathF.Ceiling(maxDelta / sampleStep)) |> max 1

      let mutable ok = true
      let mutable i = 0
      let mutable prevY = startPos.Y

      while ok && i <= steps do
        let t = float32 i / float32 steps
        let x = startPos.X + dx * t
        let z = startPos.Z + dz * t

        let probe: WorldPosition = { X = x; Y = prevY; Z = z }

        let y =
          match tryProjectToGroundWithConfig config map probe with
          | ValueSome grounded -> grounded.Y
          | ValueNone -> 0.0f

        if abs(y - prevY) > maxStepHeight then
          ok <- false
        else
          let p: WorldPosition = { X = x; Y = y; Z = z }

          if not(canOccupyWithConfig config map p entityHeight) then
            ok <- false

        prevY <- y
        i <- i + 1

      ok

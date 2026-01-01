namespace Pomo.Core.Algorithms

open Microsoft.Xna.Framework
open Pomo.Core
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Algorithms

module BlockCollision =

  let inline private getCellAtXZ(pos: WorldPosition) : struct (int * int) =
    struct (int(pos.X / BlockMap.CellSize), int(pos.Z / BlockMap.CellSize))

  let getSurfaceHeight
    (map: BlockMapDefinition)
    (pos: WorldPosition)
    : float32 voption =
    let struct (cx, cz) = getCellAtXZ pos

    if cx < 0 || cx >= map.Width || cz < 0 || cz >= map.Depth then
      ValueNone
    else
      let mutable found = ValueNone
      let mutable y = map.Height - 1

      while y >= 0 && found.IsNone do
        let cell: GridCell3D = { X = cx; Y = y; Z = cz }

        match map.Blocks |> Dictionary.tryFindV cell with
        | ValueSome block ->
          match map.Palette |> Dictionary.tryFindV block.BlockTypeId with
          | ValueSome blockType ->
            match blockType.CollisionType with
            | Box
            | Mesh -> found <- ValueSome(float32(y + 1) * BlockMap.CellSize)
            | NoCollision -> ()
          | ValueNone -> ()
        | ValueNone -> ()

        y <- y - 1

      found

  let isBlocked
    (map: BlockMapDefinition)
    (pos: WorldPosition)
    (entityHeight: float32)
    : bool =
    let struct (cx, cz) = getCellAtXZ pos

    if cx < 0 || cx >= map.Width || cz < 0 || cz >= map.Depth then
      false
    else
      let entityCellY = int(pos.Y / BlockMap.CellSize)
      let cell: GridCell3D = { X = cx; Y = entityCellY; Z = cz }

      match map.Blocks |> Dictionary.tryFindV cell with
      | ValueSome block ->
        match map.Palette |> Dictionary.tryFindV block.BlockTypeId with
        | ValueSome blockType ->
          match blockType.CollisionType with
          | Box -> true
          | Mesh -> false
          | NoCollision -> false
        | ValueNone -> false
      | ValueNone -> false

  let applyCollision
    (map: BlockMapDefinition)
    (currentPos: WorldPosition)
    (velocity: Vector2)
    (dt: float32)
    (entityHeight: float32)
    : WorldPosition =
    let newX = currentPos.X + velocity.X * dt
    let newZ = currentPos.Z + velocity.Y * dt
    let proposedPos: WorldPosition = { X = newX; Y = currentPos.Y; Z = newZ }

    if isBlocked map proposedPos entityHeight then
      currentPos
    else
      match getSurfaceHeight map proposedPos with
      | ValueSome surfaceY -> { proposedPos with Y = surfaceY }
      | ValueNone -> proposedPos

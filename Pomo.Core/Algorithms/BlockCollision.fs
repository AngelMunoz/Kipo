namespace Pomo.Core.Algorithms

open Microsoft.Xna.Framework
open Pomo.Core
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Core.Constants

module BlockCollision =

  let getSurfaceHeight
    (map: BlockMapDefinition)
    (pos: WorldPosition)
    : float32 voption =
    Spatial3D.tryGetSurfaceHeightWithConfig ValueNone map pos

  let isBlocked
    (map: BlockMapDefinition)
    (pos: WorldPosition)
    (entityHeight: float32)
    : bool =
    not(Spatial3D.canOccupyWithConfig ValueNone map pos entityHeight)

  let applyCollision
    (map: BlockMapDefinition)
    (currentPos: WorldPosition)
    (velocity: Vector2)
    (dt: float32)
    (entityHeight: float32)
    : WorldPosition =
    let newX = currentPos.X + velocity.X * dt
    let newZ = currentPos.Z + velocity.Y * dt

    let tryMove(pos: WorldPosition) =
      match Spatial3D.tryProjectToGroundWithConfig ValueNone map pos with
      | ValueSome grounded when
        Spatial3D.canOccupyWithConfig ValueNone map grounded entityHeight
        ->
        ValueSome grounded
      | _ -> ValueNone

    // 1. Try full movement (diagonal)
    match tryMove { currentPos with X = newX; Z = newZ } with
    | ValueSome grounded -> grounded
    | ValueNone ->
      // 2. Try X-only movement
      match tryMove { currentPos with X = newX } with
      | ValueSome groundedX ->
        // 3. From X-only, can we also apply some Z? (Sliding with friction/partial blocked)
        // For now, just keep X-only to keep it simple and avoid oscillations
        groundedX
      | ValueNone ->
        // 4. Try Z-only movement
        match tryMove { currentPos with Z = newZ } with
        | ValueSome groundedZ -> groundedZ
        | ValueNone -> currentPos

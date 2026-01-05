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
    let proposedPos: WorldPosition = { X = newX; Y = currentPos.Y; Z = newZ }

    match Spatial3D.tryProjectToGroundWithConfig ValueNone map proposedPos with
    | ValueSome grounded when
      Spatial3D.canOccupyWithConfig ValueNone map grounded entityHeight
      ->
      grounded
    | _ -> currentPos

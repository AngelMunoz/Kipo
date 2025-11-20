namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units

module Spatial =

  [<Struct>]
  type GridCell = { X: int; Y: int }

  type SpatialGrid = HashMap<GridCell, IndexList<Guid<EntityId>>>

  let getGridCell (cellSize: float32) (position: Vector2) : GridCell = {
    X = int(position.X / cellSize)
    Y = int(position.Y / cellSize)
  }

namespace Pomo.Core.Domain

open Microsoft.Xna.Framework
open Pomo.Core.Domain.Spatial

module Navigation =

    [<Struct>]
    type NodeCost = {
        G: float32 // Cost from start
        H: float32 // Heuristic to end
        F: float32 // Total cost
    }

    [<Struct>]
    type NavNode = {
        Position: GridCell
        Parent: GridCell voption
        Cost: NodeCost
    }

    type NavGrid = {
        Width: int
        Height: int
        CellSize: float32
        // True if the cell is blocked (wall/obstacle)
        IsBlocked: bool[,]
        // Optional: Movement cost multiplier (1.0f default, 2.0f for water, etc.)
        Cost: float32[,]
    }

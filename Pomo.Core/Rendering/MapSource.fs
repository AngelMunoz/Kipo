namespace Pomo.Core.Rendering

open Microsoft.Xna.Framework
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Core

/// Represents the terrain/world source for rendering
type MapSource =
  | TileMap of MapDefinition
  | BlockMap3D of BlockMapDefinition

module MapSource =
  /// Gets the appropriate pixels-per-unit based on the map source type
  let inline getPixelsPerUnit(source: MapSource) =
    match source with
    | TileMap map -> Vector2(float32 map.TileWidth, float32 map.TileHeight)
    | BlockMap3D _ -> Constants.BlockMap3DPixelsPerUnit

  /// Extracts the TileMap if present
  let inline tryGetTileMap(source: MapSource) =
    match source with
    | TileMap map -> ValueSome map
    | BlockMap3D _ -> ValueNone

  /// Extracts the BlockMap if present
  let inline tryGetBlockMap(source: MapSource) =
    match source with
    | BlockMap3D blockMap -> ValueSome blockMap
    | TileMap _ -> ValueNone

  /// Returns true if this is a 3D BlockMap source
  let inline isBlockMap3D(source: MapSource) =
    match source with
    | BlockMap3D _ -> true
    | TileMap _ -> false

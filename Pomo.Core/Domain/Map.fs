namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units

module Map =

  [<Struct>]
  type Orientation =
    | Orthogonal
    | Isometric
    | Staggered
    | Hexagonal

  [<Struct>]
  type StaggerAxis =
    | X
    | Y

  [<Struct>]
  type StaggerIndex =
    | Odd
    | Even

  [<Struct>]
  type RenderOrder =
    | RightDown
    | RightUp
    | LeftDown
    | LeftUp

  [<Struct>]
  type TileDefinition = {
    Id: int<TileId>
    ImageSource: string
    Width: int
    Height: int
    Properties: HashMap<string, string>
  }

  [<Struct>]
  type MapTile = { TileId: int<TileId>; X: int; Y: int }

  [<Struct>]
  type MapLayer = {
    Id: int<LayerId>
    Name: string
    Width: int
    Height: int
    Tiles: MapTile voption[,] // 2D array of potential tiles
    Opacity: float32
    Visible: bool
    Properties: HashMap<string, string>
  }

  /// Collision shape types for map objects
  [<Struct>]
  type CollisionShape =
    | ClosedPolygon of closedPoly: IndexList<Vector2>
    | OpenPolyline of openPoly: IndexList<Vector2>
    | RectangleShape of width: float32 * height: float32
    | EllipseShape of width: float32 * height: float32

  [<Struct>]
  type MapObjectType =
    | Wall
    | Zone
    | Spawn
    | Teleport

  [<Struct>]
  type PortalData = {
    TargetMap: string
    TargetSpawn: string
  }

  [<Struct>]
  type MapObject = {
    Id: int<ObjectId>
    Name: string
    Type: MapObjectType voption
    X: float32
    Y: float32
    Width: float32
    Height: float32
    Rotation: float32
    Gid: int voption // For tile objects
    Properties: HashMap<string, string>
    CollisionShape: CollisionShape voption
    PortalData: PortalData voption
  }

  [<Struct>]
  type ObjectGroup = {
    Id: int
    Name: string
    Objects: IndexList<MapObject>
    Opacity: float32
    Visible: bool
  }

  [<Struct>]
  type Tileset = {
    FirstGid: int
    Name: string
    TileWidth: int
    TileHeight: int
    TileCount: int
    Columns: int
    Tiles: HashMap<int, TileDefinition> // Local ID to Definition
  }

  type MapDefinition = {
    Key: string
    Version: string
    TiledVersion: string
    Orientation: Orientation
    RenderOrder: RenderOrder
    Width: int
    Height: int
    TileWidth: int
    TileHeight: int
    Infinite: bool
    StaggerAxis: StaggerAxis voption
    StaggerIndex: StaggerIndex voption
    Tilesets: IndexList<Tileset>
    Layers: IndexList<MapLayer>
    ObjectGroups: IndexList<ObjectGroup>
    BackgroundColor: Color voption
    Properties: HashMap<string, string>
  }

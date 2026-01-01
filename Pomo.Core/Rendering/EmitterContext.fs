namespace Pomo.Core.Rendering

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Projectile
open Pomo.Core.Domain.Animation
open Pomo.Core.Stores
open Pomo.Core.Graphics

open Pomo.Core.Domain.Core

/// Shared rendering core - used by all emitters
///
/// Note: This type intentionally carries a small amount of render-space knowledge
/// so emitters can avoid isometric-specific math when rendering BlockMap3D.
[<Struct>]
type RenderSpace =
  | Isometric
  | True3D

type RenderCore = {
  PixelsPerUnit: Vector2
  Space: RenderSpace
  ToRenderPos: WorldPosition -> Vector3
  ToRenderParticlePos: Vector3 -> Vector3
}

/// Entity-specific rendering data
type EntityRenderData = {
  ModelStore: ModelStore
  GetLoadedModelByAsset: string -> LoadedModel voption
  EntityPoses: IReadOnlyDictionary<Guid<EntityId>, Dictionary<string, Matrix>>
  LiveProjectiles: HashMap<Guid<EntityId>, LiveProjectile>
  SquishFactor: float32
  ModelScale: float32
}

/// Particle-specific rendering data (billboard + mesh)
type ParticleRenderData = {
  GetTexture: string -> Texture2D voption
  GetLoadedModelByAsset: string -> LoadedModel voption
  EntityPositions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>
  SquishFactor: float32
  ModelScale: float32
  FallbackTexture: Texture2D
}

/// Terrain-specific rendering data
type TerrainRenderData = {
  GetTileTexture: int -> Texture2D voption
  /// Pre-computed tile render indices per layer (for correct staggered overlap order)
  LayerRenderIndices: IReadOnlyDictionary<int, int[]>
}

/// Render command for text (after culling and transform)
[<Struct>]
type TextCommand = {
  Text: string
  ScreenPosition: Vector2
  Alpha: float32
  Color: Color
  Scale: float32
}

module RenderCore =
  open Pomo.Core.Graphics
  open Pomo.Core.Domain.BlockMap

  /// Creates the shared RenderCore for TileMap (2D isometric) rendering
  let createForTileMap(pixelsPerUnit: Vector2) : RenderCore =
    let toRenderPos(pos: WorldPosition) =
      RenderMath.LogicRender.toRender pos pixelsPerUnit

    let toRenderParticlePos(particlePos: Vector3) =
      RenderMath.ParticleSpace.toRender particlePos pixelsPerUnit

    {
      PixelsPerUnit = pixelsPerUnit
      Space = Isometric
      ToRenderPos = toRenderPos
      ToRenderParticlePos = toRenderParticlePos
    }

  /// Creates the shared RenderCore for BlockMap3D (true 3D) rendering
  let createForBlockMap3D
    (pixelsPerUnit: Vector2)
    (mapWidth: int)
    (mapDepth: int)
    : RenderCore =
    let ppu = pixelsPerUnit.X // Uniform scale for 3D

    let centerOffset =
      RenderMath.BlockMap3D.calcCenterOffset mapWidth mapDepth ppu

    let toRenderPos(pos: WorldPosition) =
      RenderMath.BlockMap3D.toRender pos ppu centerOffset

    let toRenderParticlePos(particlePos: Vector3) =
      RenderMath.BlockMap3D.toRender
        {
          X = particlePos.X
          Y = particlePos.Y
          Z = particlePos.Z
        }
        ppu
        centerOffset

    {
      PixelsPerUnit = pixelsPerUnit
      Space = True3D
      ToRenderPos = toRenderPos
      ToRenderParticlePos = toRenderParticlePos
    }

  /// Creates the shared RenderCore based on MapSource
  let createFromMapSource(mapSource: MapSource) : RenderCore =
    let ppu = MapSource.getPixelsPerUnit mapSource

    match mapSource with
    | MapSource.TileMap _ -> createForTileMap ppu
    | MapSource.BlockMap3D blockMap ->
      createForBlockMap3D ppu blockMap.Width blockMap.Depth

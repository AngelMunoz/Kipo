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

/// Shared rendering core - used by all emitters
type RenderCore = {
  PixelsPerUnit: Vector2
  ToRenderPos: Vector2 -> float32 -> Vector3
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
  EntityPositions: IReadOnlyDictionary<Guid<EntityId>, Vector2>
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

  /// Creates the shared RenderCore from map pixel settings
  let create(pixelsPerUnit: Vector2) : RenderCore =
    let toRenderPos (logicPos: Vector2) (altitude: float32) =
      RenderMath.LogicRender.toRender logicPos altitude pixelsPerUnit

    {
      PixelsPerUnit = pixelsPerUnit
      ToRenderPos = toRenderPos
    }

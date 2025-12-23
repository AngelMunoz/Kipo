namespace Pomo.Core.Rendering

open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.Units
open Pomo.Core.Graphics

module TerrainEmitter =

  /// Cleans asset path from Tiled format to ContentManager format
  let private cleanAssetPath(p: string) =
    p.Replace("../", "").Replace(".png", "").Replace(".jpg", "")

  /// Pre-loads tile textures from map tilesets into a cache
  let loadTileTextures
    (content: ContentManager)
    (map: MapDefinition)
    : Dictionary<int, Texture2D> =
    let cache = Dictionary<int, Texture2D>()

    for tileset in map.Tilesets do
      for localId, tileDef in tileset.Tiles do
        let globalId = tileset.FirstGid + localId
        let assetPath = cleanAssetPath tileDef.ImageSource

        try
          let texture = content.Load<Texture2D> assetPath
          cache[globalId] <- texture
        with _ ->
          ()

    cache

  /// Computes the optimized tile render indices for a single layer
  /// For staggered maps, this orders tiles so that evens then odds (or vice versa)
  /// are rendered per row, ensuring correct visual overlap.
  let private computeLayerIndices
    (map: MapDefinition)
    (layer: MapLayer)
    : int[] =
    let w = layer.Width
    let h = layer.Height

    let isStaggeredX =
      match map.Orientation, map.StaggerAxis with
      | Staggered, ValueSome X -> true
      | _ -> false

    if isStaggeredX then
      // Precompute which phase each column belongs to
      let isPass2 =
        match map.StaggerIndex with
        | ValueSome Odd -> fun x -> x % 2 = 1
        | ValueSome Even -> fun x -> x % 2 = 0
        | _ -> fun _ -> false

      // Single allocation, stable sort by (y, phase, x)
      Array.init (w * h) id
      |> Array.sortBy(fun i ->
        let x = i % w
        let y = i / w
        // Sort key: y first, then pass (0 or 1), then x
        struct (y, (if isPass2 x then 1 else 0), x))
    else
      Array.init (w * h) id

  /// Pre-computes render indices for all layers in the map (call once at load time)
  let computeLayerRenderIndices
    (map: MapDefinition)
    : IReadOnlyDictionary<int, int[]> =
    let dict = Dictionary<int, int[]>()

    for layer in map.Layers do
      let layerId = %layer.Id
      dict[layerId] <- computeLayerIndices map layer

    dict

  /// Gets RenderGroup from layer properties, defaults to 0
  let inline private getRenderGroup(layer: MapLayer) : int =
    match layer.Properties |> HashMap.tryFindV "RenderGroup" with
    | ValueSome s ->
      match System.Int32.TryParse s with
      | true, v -> v
      | _ -> 0
    | ValueNone -> 0

  /// Emits terrain commands for a single layer (parallel over tile indices)
  let emitLayer
    (core: RenderCore)
    (data: TerrainRenderData)
    (map: MapDefinition)
    (layer: MapLayer)
    (viewBounds: struct (float32 * float32 * float32 * float32))
    : TerrainCommand[] =
    if not layer.Visible then
      Array.empty
    else
      let tileW = float32 map.TileWidth
      let tileH = float32 map.TileHeight
      let struct (viewLeft, viewRight, viewTop, viewBottom) = viewBounds

      // Use pre-computed indices from TerrainRenderData (computed once at load time)
      let layerId = %layer.Id

      let indices =
        match data.LayerRenderIndices.TryGetValue(layerId) with
        | true, arr -> arr
        | false, _ -> Array.init (layer.Width * layer.Height) id

      indices
      |> Array.Parallel.choose(fun i ->
        let x = i % layer.Width
        let y = i / layer.Width

        layer.Tiles.[x, y]
        |> ValueOption.bind(fun tile -> data.GetTileTexture(int tile.TileId))
        |> ValueOption.bind(fun texture ->
          let struct (posX, posY) =
            RenderMath.TileGridToLogic
              map.Orientation
              map.StaggerAxis
              map.StaggerIndex
              map.Width
              x
              y
              tileW
              tileH

          let drawX = posX
          let drawY = posY + tileH - float32 texture.Height

          // Culling check
          let tileRight = drawX + float32 texture.Width
          let tileBottom = drawY + float32 texture.Height

          let isVisible =
            tileRight >= viewLeft
            && drawX <= viewRight
            && tileBottom >= viewTop
            && drawY <= viewBottom

          if isVisible then
            let depthY =
              (drawY + float32 texture.Height) / core.PixelsPerUnit.Y

            let worldPos =
              RenderMath.TileToRender
                (Vector2(drawX, drawY))
                depthY
                core.PixelsPerUnit

            let size =
              Vector2(
                float32 texture.Width / core.PixelsPerUnit.X,
                float32 texture.Height / core.PixelsPerUnit.Y
              )

            ValueSome {
              Texture = texture
              Position = worldPos
              Size = size
            }
          else
            ValueNone)
        |> function
          | ValueSome cmd -> Some cmd
          | ValueNone -> None)

  /// Emits all terrain commands split by RenderGroup
  /// Returns (background commands, foreground commands)
  /// Background: RenderGroup < 2 (rendered before entities)
  /// Foreground: RenderGroup >= 2 (rendered after entities with depth testing)
  let emitAll
    (core: RenderCore)
    (data: TerrainRenderData)
    (map: MapDefinition)
    (viewBounds: struct (float32 * float32 * float32 * float32))
    : struct (TerrainCommand[] * TerrainCommand[]) =
    let bgLayers, fgLayers =
      map.Layers |> IndexList.partition(fun l -> getRenderGroup l < 2)


    let bgCmds =
      bgLayers
      |> IndexList.toArray
      |> Array.collect(fun l -> emitLayer core data map l viewBounds)

    let fgCmds =
      fgLayers
      |> IndexList.toArray
      |> Array.collect(fun l -> emitLayer core data map l viewBounds)

    struct (bgCmds, fgCmds)

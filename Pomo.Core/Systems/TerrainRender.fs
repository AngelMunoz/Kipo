namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.Core

open Pomo.Core.Stores

module TerrainRenderSystem =

  type TerrainRenderService =
    abstract Draw: Camera -> unit

  let create(game: Game, mapKey: string, layersToRender: string seq voption) =
    let layersToRender = layersToRender |> ValueOption.map HashSet.ofSeq

    let mutable mapDefinition: MapDefinition voption = ValueNone
    let mutable tilesetTexture: Texture2D voption = ValueNone
    let spriteBatch = new SpriteBatch(game.GraphicsDevice)
    let tileTextures = Collections.Generic.Dictionary<int, Texture2D>()

    let mapStore = game.Services.GetService<MapStore>()
    mapDefinition <- ValueSome(mapStore.find mapKey)

    match mapDefinition with
    | ValueSome map ->
      for tileset in map.Tilesets do
        for localId, tileDef in tileset.Tiles do
          let globalId = tileset.FirstGid + localId
          // Resolve path. ImageSource is like "../Tiles/64x64/Arc_EW.png"
          // We need to convert this to a ContentManager path.

          let cleanPath(p: string) =
            p.Replace("../", "").Replace(".png", "").Replace(".jpg", "")

          let assetPath = cleanPath tileDef.ImageSource

          try
            let texture = game.Content.Load<Texture2D> assetPath
            tileTextures[globalId] <- texture
          with e ->
            Console.WriteLine
              $"Failed to load texture: {assetPath} - {e.Message}"
    | ValueNone -> ()

    { new TerrainRenderService with
        member _.Draw(camera: Camera) =
          match mapDefinition with
          | ValueSome map ->
            let transform =
              Matrix.CreateTranslation(
                -camera.Position.X,
                -camera.Position.Y,
                0.0f
              )
              * Matrix.CreateScale(camera.Zoom)
              * Matrix.CreateTranslation(
                float32 camera.Viewport.Width / 2.0f,
                float32 camera.Viewport.Height / 2.0f,
                0.0f
              )

            spriteBatch.Begin(
              SpriteSortMode.Deferred,
              BlendState.AlphaBlend,
              SamplerState.PointClamp,
              transformMatrix = transform
            )

            let layers =
              match layersToRender with
              | ValueSome names ->
                map.Layers
                |> IndexList.filter(fun l -> names |> HashSet.contains l.Name)
              | ValueNone -> map.Layers

            // Render order: RightDown is standard
            // For staggered iso, we iterate Y then X usually, or just iterate the array.

            // Always iterate Right-Down (0..W, 0..H) for correct occlusion in Staggered/Isometric views.
            // Tiled's "RenderOrder" might specify otherwise (e.g. Left-Down), but for standard
            // top-down/isometric projection with overlapping tiles, we must draw back-to-front.
            // (0,0) is top-left (back), (W,H) is bottom-right (front).

            for layer in layers do
              if layer.Visible then
                // For Staggered X, we must draw "Unstaggered" columns (High) then "Staggered" columns (Low)
                // for each row to ensure correct occlusion.
                // For Staggered Y or Isometric, standard Y-then-X is usually fine (or X-then-Y).

                let isStaggeredX =
                  match map.Orientation, map.StaggerAxis with
                  | Staggered, ValueSome X -> true
                  | _ -> false

                for y in 0 .. layer.Height - 1 do
                  let xPasses =
                    if isStaggeredX then
                      // Pass 1: Unstaggered cols, Pass 2: Staggered cols
                      [|
                        [| 0 .. layer.Width - 1 |]
                        |> Array.filter(fun x ->
                          match map.StaggerIndex with
                          | ValueSome Odd -> x % 2 = 0 // Evens are unstaggered (High)
                          | ValueSome Even -> x % 2 = 1 // Odds are unstaggered (High)
                          | _ -> true)

                        [| 0 .. layer.Width - 1 |]
                        |> Array.filter(fun x ->
                          match map.StaggerIndex with
                          | ValueSome Odd -> x % 2 = 1 // Odds are staggered (Low)
                          | ValueSome Even -> x % 2 = 0 // Evens are staggered (Low)
                          | _ -> false)
                      |]
                    else
                      // Standard single pass
                      [| [| 0 .. layer.Width - 1 |] |]

                  for xIndices in xPasses do
                    for x in xIndices do
                      match layer.Tiles.[x, y] with
                      | ValueSome tile ->
                        let gid = int tile.TileId

                        if tileTextures.ContainsKey(gid) then
                          let texture = tileTextures[gid]

                          // Calculate position based on Stagger settings
                          let tileW = float32 map.TileWidth
                          let tileH = float32 map.TileHeight

                          let posX, posY =
                            match
                              map.Orientation, map.StaggerAxis, map.StaggerIndex
                            with
                            | Staggered, ValueSome X, ValueSome index ->
                              // Staggered X
                              let xStep = tileW / 2.0f
                              let yStep = tileH
                              let px = float32 x * xStep

                              let isStaggeredCol =
                                match index with
                                | Odd -> x % 2 = 1
                                | Even -> x % 2 = 0

                              let yOffset =
                                if isStaggeredCol then tileH / 2.0f else 0.0f

                              let py = float32 y * yStep + yOffset
                              px, py

                            | Staggered, ValueSome Y, ValueSome index ->
                              // Staggered Y
                              let xStep = tileW
                              let yStep = tileH / 2.0f
                              let py = float32 y * yStep

                              let isStaggeredRow =
                                match index with
                                | Odd -> y % 2 = 1
                                | Even -> y % 2 = 0

                              let xOffset =
                                if isStaggeredRow then tileW / 2.0f else 0.0f

                              let px = float32 x * xStep + xOffset
                              px, py

                            | Isometric, _, _ ->
                              // Standard Isometric (Diamond)
                              let originX = float32 map.Width * tileW / 2.0f

                              let px =
                                originX + (float32 x - float32 y) * tileW / 2.0f

                              let py = (float32 x + float32 y) * tileH / 2.0f
                              px, py

                            | _ ->
                              // Orthogonal or fallback
                              float32 x * tileW, float32 y * tileH

                          // Adjust for texture height (bottom-align)
                          let drawX = posX
                          let drawY = posY + tileH - float32 texture.Height

                          spriteBatch.Draw(
                            texture,
                            Vector2(drawX, drawY),
                            Color.White
                          )
                      | ValueNone -> ()

            spriteBatch.End()
          | _ -> ()
    }

namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Camera
open Pomo.Core.Graphics

open Pomo.Core.Stores

module TerrainRenderSystem =

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type TerrainRenderService =
    abstract Draw: Camera -> unit

  let create
    (
      game: Game,
      env: PomoEnvironment,
      mapKey: string,
      layersToRender: string seq voption
    ) =
    let (Stores stores) = env.StoreServices
    let layersToRender = layersToRender |> ValueOption.map HashSet.ofSeq

    let mutable mapDefinition: MapDefinition voption = ValueNone
    let mutable tilesetTexture: Texture2D voption = ValueNone
    let spriteBatch = new SpriteBatch(game.GraphicsDevice)
    let tileTextures = Collections.Generic.Dictionary<int, Texture2D>()

    let mapStore = stores.MapStore
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

    let quadBatch = new Pomo.Core.Graphics.QuadBatch(game.GraphicsDevice)

    { new TerrainRenderService with
        member _.Draw(camera: Camera) =
          match mapDefinition with
          | ValueSome map ->
            let layers =
              match layersToRender with
              | ValueSome names ->
                map.Layers
                |> IndexList.filter(fun l -> names |> HashSet.contains l.Name)
              | ValueNone -> map.Layers

            // Check if this is a Foreground service (RenderGroup >= 2)
            let isForeground =
              layers
              |> IndexList.tryAt 0
              |> Option.map(fun l ->
                match l.Properties |> HashMap.tryFindV "RenderGroup" with
                | ValueSome v -> (Int32.TryParse v |> snd) >= 2
                | ValueNone -> false)
              |> Option.defaultValue false

            if isForeground then
              // 3D Quad Rendering for Foreground (Walls)
              quadBatch.Begin(camera.View, camera.Projection)

              // Calculate visible viewport bounds in world coordinates
              let viewportWorldLeft =
                camera.Position.X
                - float32 camera.Viewport.Width / (2.0f * camera.Zoom)

              let viewportWorldRight =
                camera.Position.X
                + float32 camera.Viewport.Width / (2.0f * camera.Zoom)

              let viewportWorldTop =
                camera.Position.Y
                - float32 camera.Viewport.Height / (2.0f * camera.Zoom)

              let viewportWorldBottom =
                camera.Position.Y
                + float32 camera.Viewport.Height / (2.0f * camera.Zoom)

              for layer in layers do
                if layer.Visible then
                  let isStaggeredX =
                    match map.Orientation, map.StaggerAxis with
                    | Staggered, ValueSome X -> true
                    | _ -> false

                  for y in 0 .. layer.Height - 1 do
                    let xPasses =
                      if isStaggeredX then
                        [|
                          [| 0 .. layer.Width - 1 |]
                          |> Array.filter(fun x ->
                            match map.StaggerIndex with
                            | ValueSome Odd -> x % 2 = 0
                            | ValueSome Even -> x % 2 = 1
                            | _ -> true)

                          [| 0 .. layer.Width - 1 |]
                          |> Array.filter(fun x ->
                            match map.StaggerIndex with
                            | ValueSome Odd -> x % 2 = 1
                            | ValueSome Even -> x % 2 = 0
                            | _ -> false)
                        |]
                      else
                        [| [| 0 .. layer.Width - 1 |] |]

                    for xIndices in xPasses do
                      for x in xIndices do
                        match layer.Tiles.[x, y] with
                        | ValueSome tile ->
                          let gid = int tile.TileId

                          if tileTextures.ContainsKey(gid) then
                            let texture = tileTextures[gid]
                            let tileW = float32 map.TileWidth
                            let tileH = float32 map.TileHeight

                            let posX, posY =
                              match
                                map.Orientation,
                                map.StaggerAxis,
                                map.StaggerIndex
                              with
                              | Staggered, ValueSome X, ValueSome index ->
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
                                let originX = float32 map.Width * tileW / 2.0f

                                let px =
                                  originX
                                  + (float32 x - float32 y) * tileW / 2.0f

                                let py = (float32 x + float32 y) * tileH / 2.0f
                                px, py
                              | _ -> float32 x * tileW, float32 y * tileH

                            let drawX = posX
                            let drawY = posY + tileH - float32 texture.Height

                            // Culling
                            let tileLeft = drawX
                            let tileRight = drawX + float32 texture.Width
                            let tileTop = drawY
                            let tileBottom = drawY + float32 texture.Height

                            let isVisible =
                              tileRight >= viewportWorldLeft
                              && tileLeft <= viewportWorldRight
                              && tileBottom >= viewportWorldTop
                              && tileTop <= viewportWorldBottom

                            if isVisible then
                              // 3D Conversion
                              let pixelsPerUnit =
                                Vector2(
                                  float32 map.TileWidth,
                                  float32 map.TileHeight
                                )

                              let depthY =
                                (drawY + float32 texture.Height)
                                / pixelsPerUnit.Y

                              let worldPos =
                                RenderMath.Legacy.LogicToRenderWithDepth
                                  (Vector2(drawX, drawY))
                                  depthY
                                  pixelsPerUnit

                              let size =
                                Vector2(
                                  float32 texture.Width / pixelsPerUnit.X,
                                  float32 texture.Height / pixelsPerUnit.Y
                                )

                              quadBatch.Draw(texture, worldPos, size)
                        | ValueNone -> ()

              quadBatch.End()

            else
              // 2D SpriteBatch Rendering for Background (Ground)
              let transform =
                RenderMath.Legacy.GetSpriteBatchTransform
                  camera.Position
                  camera.Zoom
                  camera.Viewport.Width
                  camera.Viewport.Height

              spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointClamp,
                transformMatrix = transform
              )

              // Calculate visible viewport bounds in world coordinates
              let viewportWorldLeft =
                camera.Position.X
                - float32 camera.Viewport.Width / (2.0f * camera.Zoom)

              let viewportWorldRight =
                camera.Position.X
                + float32 camera.Viewport.Width / (2.0f * camera.Zoom)

              let viewportWorldTop =
                camera.Position.Y
                - float32 camera.Viewport.Height / (2.0f * camera.Zoom)

              let viewportWorldBottom =
                camera.Position.Y
                + float32 camera.Viewport.Height / (2.0f * camera.Zoom)

              for layer in layers do
                if layer.Visible then
                  let isStaggeredX =
                    match map.Orientation, map.StaggerAxis with
                    | Staggered, ValueSome X -> true
                    | _ -> false

                  for y in 0 .. layer.Height - 1 do
                    let xPasses =
                      if isStaggeredX then
                        [|
                          [| 0 .. layer.Width - 1 |]
                          |> Array.filter(fun x ->
                            match map.StaggerIndex with
                            | ValueSome Odd -> x % 2 = 0
                            | ValueSome Even -> x % 2 = 1
                            | _ -> true)

                          [| 0 .. layer.Width - 1 |]
                          |> Array.filter(fun x ->
                            match map.StaggerIndex with
                            | ValueSome Odd -> x % 2 = 1
                            | ValueSome Even -> x % 2 = 0
                            | _ -> false)
                        |]
                      else
                        [| [| 0 .. layer.Width - 1 |] |]

                    for xIndices in xPasses do
                      for x in xIndices do
                        match layer.Tiles.[x, y] with
                        | ValueSome tile ->
                          let gid = int tile.TileId

                          if tileTextures.ContainsKey(gid) then
                            let texture = tileTextures[gid]
                            let tileW = float32 map.TileWidth
                            let tileH = float32 map.TileHeight

                            let posX, posY =
                              match
                                map.Orientation,
                                map.StaggerAxis,
                                map.StaggerIndex
                              with
                              | Staggered, ValueSome X, ValueSome index ->
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
                                let originX = float32 map.Width * tileW / 2.0f

                                let px =
                                  originX
                                  + (float32 x - float32 y) * tileW / 2.0f

                                let py = (float32 x + float32 y) * tileH / 2.0f
                                px, py
                              | _ -> float32 x * tileW, float32 y * tileH

                            let drawX = posX
                            let drawY = posY + tileH - float32 texture.Height

                            let tileLeft = drawX
                            let tileRight = drawX + float32 texture.Width
                            let tileTop = drawY
                            let tileBottom = drawY + float32 texture.Height

                            let isVisible =
                              tileRight >= viewportWorldLeft
                              && tileLeft <= viewportWorldRight
                              && tileBottom >= viewportWorldTop
                              && tileTop <= viewportWorldBottom

                            if isVisible then
                              spriteBatch.Draw(
                                texture,
                                Vector2(drawX, drawY),
                                Color.White
                              )
                        | ValueNone -> ()

              spriteBatch.End()
          | _ -> ()
    }

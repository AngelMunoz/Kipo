namespace Pomo.Core.Rendering

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Map
open Pomo.Core.Graphics

module TerrainEmitter =

  /// Emits terrain commands for a layer (parallel over tile indices)
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

      let indices = Array.init (layer.Width * layer.Height) id

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
              RenderMath.LogicToRender
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

  /// Emits terrain commands for all foreground layers
  let emitForeground
    (core: RenderCore)
    (data: TerrainRenderData)
    (map: MapDefinition)
    (layers: MapLayer IndexList)
    (viewBounds: struct (float32 * float32 * float32 * float32))
    : TerrainCommand[] =
    layers
    |> IndexList.toArray
    |> Array.collect(fun layer -> emitLayer core data map layer viewBounds)

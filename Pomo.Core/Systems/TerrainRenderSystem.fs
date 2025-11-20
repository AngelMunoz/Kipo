namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.Map
open Pomo.Core
open FSharp.Data.Adaptive

module TerrainRenderSystem =

  let private drawLine
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (start: Vector2)
    (end': Vector2)
    (color: Color)
    =
    let edge = end' - start
    let angle = float32(Math.Atan2(float edge.Y, float edge.X))

    sb.Draw(
      pixel,
      Rectangle(int start.X, int start.Y, int(edge.Length()), 1),
      Nullable(),
      color,
      angle,
      Vector2.Zero,
      SpriteEffects.None,
      0.0f
    )

  let private rotate (point: Vector2) (radians: float32) =
    let c = float32(Math.Cos(float radians))
    let s = float32(Math.Sin(float radians))
    Vector2(point.X * c - point.Y * s, point.X * s + point.Y * c)

  let private drawPolygon
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (points: IndexList<Vector2>)
    (position: Vector2)
    (rotation: float32)
    (color: Color)
    =
    let count = points.Count
    let radians = MathHelper.ToRadians(rotation)

    for i in 0 .. count - 1 do
      let p1 = rotate points.[i] radians + position
      let p2 = rotate points.[(i + 1) % count] radians + position
      drawLine sb pixel p1 p2 color

  let private drawEllipse
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (position: Vector2)
    (width: float32)
    (height: float32)
    (rotation: float32)
    (color: Color)
    =
    let segments = 32
    let radiusX = width / 2.0f
    let radiusY = height / 2.0f
    // Tiled rotates around the top-left corner (position), but the ellipse is defined within the box.
    // We generate points relative to the center of the ellipse, then offset to top-left relative, then rotate, then add position.
    // Wait, Tiled objects (Ellipse) are defined by (x,y,w,h). Rotation is around (x,y) usually?
    // Actually, for Ellipse objects, Tiled rotates them around the center? Or top-left?
    // Standard Tiled rotation for objects is around the top-left.
    // So we need points relative to (0,0) (which is top-left).
    // Center of ellipse relative to top-left is (radiusX, radiusY).

    let centerOffset = Vector2(radiusX, radiusY)
    let step = MathHelper.TwoPi / float32 segments
    let radians = MathHelper.ToRadians(rotation)

    for i in 0 .. segments - 1 do
      let theta1 = float32 i * step
      let theta2 = float32(i + 1) * step

      // Points relative to center of ellipse
      let localP1 = Vector2(radiusX * cos theta1, radiusY * sin theta1)
      let localP2 = Vector2(radiusX * cos theta2, radiusY * sin theta2)

      // Points relative to top-left (0,0)
      let p1Unrotated = localP1 + centerOffset
      let p2Unrotated = localP2 + centerOffset

      // Rotate around (0,0)
      let p1Rotated = rotate p1Unrotated radians
      let p2Rotated = rotate p2Unrotated radians

      // Translate to world position
      let p1 = p1Rotated + position
      let p2 = p2Rotated + position

      drawLine sb pixel p1 p2 color

  type TerrainRenderSystem(game: Game, mapPath: string) =
    inherit DrawableGameComponent(game)

    let mutable mapDefinition: MapDefinition voption = ValueNone
    let mutable tilesetTexture: Texture2D voption = ValueNone
    let mutable spriteBatch: SpriteBatch voption = ValueNone
    let mutable pixel: Texture2D voption = ValueNone
    let tileTextures = Collections.Generic.Dictionary<int, Texture2D>()

    override this_.LoadContent() =
      spriteBatch <- ValueSome(new SpriteBatch(game.GraphicsDevice))

      let p = new Texture2D(game.GraphicsDevice, 1, 1)
      p.SetData([| Color.White |])
      pixel <- ValueSome p

      // Load the map
      let map = MapLoader.loadMap mapPath
      mapDefinition <- ValueSome map

      ()


    override _.Initialize() =
      base.Initialize()

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

    override _.Draw _ =
      match spriteBatch, mapDefinition with
      | ValueSome sb, ValueSome map ->
        sb.Begin(
          SpriteSortMode.Deferred,
          BlendState.AlphaBlend,
          SamplerState.PointClamp
        )

        // Render order: RightDown is standard
        // For staggered iso, we iterate Y then X usually, or just iterate the array.

        // Always iterate Right-Down (0..W, 0..H) for correct occlusion in Staggered/Isometric views.
        // Tiled's "RenderOrder" might specify otherwise (e.g. Left-Down), but for standard
        // top-down/isometric projection with overlapping tiles, we must draw back-to-front.
        // (0,0) is top-left (back), (W,H) is bottom-right (front).

        for layer in map.Layers do
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

                      sb.Draw(texture, Vector2(drawX, drawY), Color.White)
                  | ValueNone -> ()

        // Render Object Groups
        match pixel with
        | ValueSome px ->
          for group in map.ObjectGroups do
            if group.Visible then
              for obj in group.Objects do
                let color =
                  match obj.Type with
                  | ValueSome Wall -> Color.Red
                  | ValueSome Spawn -> Color.Green
                  | ValueSome Zone ->
                    if obj.Name.Contains("Void") then Color.Purple
                    elif obj.Name.Contains("Slow") then Color.Yellow
                    elif obj.Name.Contains("Speed") then Color.Cyan
                    elif obj.Name.Contains("Healing") then Color.LimeGreen
                    elif obj.Name.Contains("Damaging") then Color.Orange
                    else Color.Blue
                  | _ -> Color.White

                match obj.Points with
                | ValueSome points ->
                  drawPolygon
                    sb
                    px
                    points
                    (Vector2(obj.X, obj.Y))
                    obj.Rotation
                    color
                | ValueNone ->
                  // Assume ellipse/rectangle if no points, but Tiled objects usually have width/height
                  // If it's an ellipse, Tiled might not export points but just shape data.
                  // Our loader doesn't strictly distinguish ellipse vs rect in types yet,
                  // but we can check if it has width/height and no points.
                  // For now, draw ellipse as per plan if it looks like one (or just rect)
                  // The plan said DrawEllipse.
                  drawEllipse
                    sb
                    px
                    (Vector2(obj.X, obj.Y))
                    obj.Width
                    obj.Height
                    obj.Rotation
                    color
        | ValueNone -> ()

        sb.End()
      | _ -> ()

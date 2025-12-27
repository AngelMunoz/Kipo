namespace Pomo.Core.Graphics

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// A simple batcher for drawing 3D textured quads.
type QuadBatch(graphicsDevice: GraphicsDevice) =
  let effect = new AlphaTestEffect(graphicsDevice)
  let mutable vertices = Array.zeroCreate<VertexPositionTexture> 2048
  let mutable indices = Array.zeroCreate<int16> 3072 // 6 indices per quad
  let mutable quadCount = 0
  let mutable storedView = Matrix.Identity
  let mutable storedProjection = Matrix.Identity

  // Ensure indices are populated
  do
    for i in 0..511 do
      let vBase = i * 4
      let iBase = i * 6
      indices.[iBase + 0] <- int16(vBase + 0)
      indices.[iBase + 1] <- int16(vBase + 1)
      indices.[iBase + 2] <- int16(vBase + 2)
      indices.[iBase + 3] <- int16(vBase + 0)
      indices.[iBase + 4] <- int16(vBase + 2)
      indices.[iBase + 5] <- int16(vBase + 3)

  member _.Begin
    (view: inref<Matrix>, projection: inref<Matrix>, ?texture: Texture2D)
    =
    quadCount <- 0
    storedView <- view
    storedProjection <- projection
    effect.View <- view
    effect.Projection <- projection
    effect.World <- Matrix.Identity
    effect.VertexColorEnabled <- false
    effect.DiffuseColor <- Vector3.One
    effect.Alpha <- 1.0f

    match texture with
    | Some t -> effect.Texture <- t
    | None -> ()

  // Internal helper that reuses stored matrices from effect state
  member private _.BeginInternal([<Struct>] ?texture: Texture2D) =
    quadCount <- 0
    effect.View <- storedView
    effect.Projection <- storedProjection
    effect.World <- Matrix.Identity
    effect.VertexColorEnabled <- false
    effect.DiffuseColor <- Vector3.One
    effect.Alpha <- 1.0f

    texture |> ValueOption.iter(fun t -> effect.Texture <- t)

  member _.End() =
    if quadCount > 0 then
      // Enforce Render States
      graphicsDevice.BlendState <- BlendState.AlphaBlend
      graphicsDevice.DepthStencilState <- DepthStencilState.Default
      graphicsDevice.SamplerStates.[0] <- SamplerState.PointClamp
      graphicsDevice.RasterizerState <- RasterizerState.CullNone

      for pass in effect.CurrentTechnique.Passes do
        pass.Apply()

        graphicsDevice.DrawUserIndexedPrimitives(
          PrimitiveType.TriangleList,
          vertices,
          0,
          quadCount * 4,
          indices,
          0,
          quadCount * 2
        )

  member this.Draw
    (texture: Texture2D, position: Vector3, size: Vector2, ?color: Color)
    =
    // Flush if texture changes or buffer full
    if not(isNull effect.Texture) && effect.Texture <> texture then
      this.End()
      this.BeginInternal texture
    elif quadCount >= 512 then
      this.End()
      this.BeginInternal texture

    if isNull effect.Texture then
      effect.Texture <- texture

    let w = size.X
    let h = size.Y

    let x = position.X
    let y = position.Y // Up (Depth)
    let z = position.Z // Screen Y

    // Vertices
    // TL
    vertices.[quadCount * 4 + 0] <-
      VertexPositionTexture(Vector3(x, y, z), Vector2(0.0f, 0.0f))
    // TR
    vertices.[quadCount * 4 + 1] <-
      VertexPositionTexture(Vector3(x + w, y, z), Vector2(1.0f, 0.0f))
    // BR
    vertices.[quadCount * 4 + 2] <-
      VertexPositionTexture(Vector3(x + w, y, z + h), Vector2(1.0f, 1.0f))
    // BL
    vertices.[quadCount * 4 + 3] <-
      VertexPositionTexture(Vector3(x, y, z + h), Vector2(0.0f, 1.0f))

    quadCount <- quadCount + 1

namespace Pomo.Core.Graphics

open System
open System.Runtime.InteropServices
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

// Based on QuadBatch but specialized for billboards
type BillboardBatch(graphicsDevice: GraphicsDevice) =
  let mutable vertexBuffer: DynamicVertexBuffer = null
  let mutable indexBuffer: IndexBuffer = null
  let mutable vertices: VertexPositionColorTexture[] = Array.zeroCreate 2048
  let mutable indices: int16[] = Array.zeroCreate 3072 // 6 indices per quad
  let mutable spriteCount = 0
  let basicEffect = new BasicEffect(graphicsDevice)

  let ensureBuffers() =
    if isNull vertexBuffer then
      vertexBuffer <-
        new DynamicVertexBuffer(
          graphicsDevice,
          typeof<VertexPositionColorTexture>,
          vertices.Length,
          BufferUsage.WriteOnly
        )

      // Pre-calculate indices
      for i = 0 to (vertices.Length / 4) - 1 do
        indices.[i * 6 + 0] <- int16(i * 4 + 0)
        indices.[i * 6 + 1] <- int16(i * 4 + 1)
        indices.[i * 6 + 2] <- int16(i * 4 + 2)
        indices.[i * 6 + 3] <- int16(i * 4 + 0)
        indices.[i * 6 + 4] <- int16(i * 4 + 2)
        indices.[i * 6 + 5] <- int16(i * 4 + 3)

      indexBuffer <-
        new IndexBuffer(
          graphicsDevice,
          typeof<int16>,
          indices.Length,
          BufferUsage.WriteOnly
        )

      indexBuffer.SetData(indices)

  let ensureCapacity(numSprites: int) =
    let requiredVerts = (spriteCount + numSprites) * 4

    if requiredVerts > vertices.Length then
      let newSize = Math.Max(vertices.Length * 2, requiredVerts)
      Array.Resize(&vertices, newSize)

      let newIndicesSize = (newSize / 4) * 6
      Array.Resize(&indices, newIndicesSize)

      // Re-fill indices
      for i = 0 to (vertices.Length / 4) - 1 do
        indices.[i * 6 + 0] <- int16(i * 4 + 0)
        indices.[i * 6 + 1] <- int16(i * 4 + 1)
        indices.[i * 6 + 2] <- int16(i * 4 + 2)
        indices.[i * 6 + 3] <- int16(i * 4 + 0)
        indices.[i * 6 + 4] <- int16(i * 4 + 2)
        indices.[i * 6 + 5] <- int16(i * 4 + 3)

      vertexBuffer <-
        new DynamicVertexBuffer(
          graphicsDevice,
          typeof<VertexPositionColorTexture>,
          vertices.Length,
          BufferUsage.WriteOnly
        )

      indexBuffer <-
        new IndexBuffer(
          graphicsDevice,
          typeof<int16>,
          indices.Length,
          BufferUsage.WriteOnly
        )

      indexBuffer.SetData(indices)

  member this.Begin(view: Matrix, projection: Matrix, texture: Texture2D) =
    basicEffect.View <- view
    basicEffect.Projection <- projection
    basicEffect.TextureEnabled <- true
    basicEffect.Texture <- texture
    basicEffect.VertexColorEnabled <- true
    // Assuming Additive or AlphaBlend state is set externally by Render.fs

    spriteCount <- 0
    basicEffect.CurrentTechnique.Passes[0].Apply()

  member this.Draw
    (
      position: Vector3,
      size: float32,
      rotation: float32,
      color: Color,
      camRight: Vector3,
      camUp: Vector3
    ) =
    ensureCapacity(1)

    let halfSize = size * 0.5f

    // Apply rotation
    let cos = MathF.Cos(rotation)
    let sin = MathF.Sin(rotation)

    // Rotated basis vectors
    let rotRight = (camRight * cos) + (camUp * sin)
    let rotUp = (camUp * cos) - (camRight * sin)

    let w = rotRight * halfSize
    let h = rotUp * halfSize

    // Quad vertices relative to center position
    // TopLeft
    let v0 = position - w + h
    // TopRight
    let v1 = position + w + h
    // BottomRight
    let v2 = position + w - h
    // BottomLeft
    let v3 = position - w - h

    let idx = spriteCount * 4

    vertices.[idx + 0] <-
      VertexPositionColorTexture(v0, color, Vector2(0.0f, 0.0f))

    vertices.[idx + 1] <-
      VertexPositionColorTexture(v1, color, Vector2(1.0f, 0.0f))

    vertices.[idx + 2] <-
      VertexPositionColorTexture(v2, color, Vector2(1.0f, 1.0f))

    vertices.[idx + 3] <-
      VertexPositionColorTexture(v3, color, Vector2(0.0f, 1.0f))

    spriteCount <- spriteCount + 1

  member this.End() =
    if spriteCount > 0 then
      ensureBuffers()
      vertexBuffer.SetData(vertices, 0, spriteCount * 4)
      graphicsDevice.SetVertexBuffer(vertexBuffer)
      graphicsDevice.Indices <- indexBuffer

      graphicsDevice.DrawIndexedPrimitives(
        PrimitiveType.TriangleList,
        0,
        0,
        spriteCount * 2
      )

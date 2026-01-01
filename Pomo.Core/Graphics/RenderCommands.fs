namespace Pomo.Core.Graphics

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.Particles

[<Struct>]
type LoadedModel = { Model: Model; HasNormals: bool }

module LoadedModel =
  let fromModel(model: Model) : LoadedModel =
    let mutable hasNormals = false
    let mutable meshes = model.Meshes.GetEnumerator()

    while not hasNormals && meshes.MoveNext() do
      let mutable parts = meshes.Current.MeshParts.GetEnumerator()

      while not hasNormals && parts.MoveNext() do
        let elements =
          parts.Current.VertexBuffer.VertexDeclaration.GetVertexElements()

        let mutable i = 0

        while not hasNormals && i < elements.Length do
          if elements[i].VertexElementUsage = VertexElementUsage.Normal then
            hasNormals <- true

          i <- i + 1

    {
      Model = model
      HasNormals = hasNormals
    }

[<Struct>]
type MeshCommand = {
  LoadedModel: LoadedModel
  WorldMatrix: Matrix
}

[<Struct>]
type BillboardCommand = {
  Texture: Texture2D
  Position: Vector3
  Size: float32
  Color: Color
  BlendMode: BlendMode
}

[<Struct>]
type TerrainCommand = {
  Texture: Texture2D
  Position: Vector3
  Size: Vector2
}

[<Struct>]
type LineCommand = {
  Vertices: VertexPositionColor[]
  VertexCount: int
}

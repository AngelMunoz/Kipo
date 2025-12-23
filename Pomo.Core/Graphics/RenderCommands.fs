namespace Pomo.Core.Graphics

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.Particles

[<Struct>]
type MeshCommand = { Model: Model; WorldMatrix: Matrix }

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

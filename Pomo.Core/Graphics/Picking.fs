namespace Pomo.Core.Graphics

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

module Picking =
  open System

  let EntityBoundingBox =
    BoundingBox(Vector3(-0.5f, 0.0f, -0.5f), Vector3(0.5f, 2.0f, 0.5f))

  /// Larger hit box for easier picking of moving targets
  let EntityHitBox =
    BoundingBox(Vector3(-0.75f, 0.0f, -0.75f), Vector3(0.75f, 3.0f, 0.75f))



  let inline createPickRay
    (screenPos: Vector2)
    (viewport: Viewport)
    (view: Matrix)
    (projection: Matrix)
    : Ray =
    let nearPoint = Vector3(screenPos.X, screenPos.Y, 0.0f)
    let farPoint = Vector3(screenPos.X, screenPos.Y, 1.0f)

    let nearWorld =
      viewport.Unproject(nearPoint, projection, view, Matrix.Identity)

    let farWorld =
      viewport.Unproject(farPoint, projection, view, Matrix.Identity)

    let direction = Vector3.Normalize(farWorld - nearWorld)
    Ray(nearWorld, direction)

  let inline transformBoundingBox
    (localBox: BoundingBox)
    (worldMatrix: Matrix)
    =
    let corners = localBox.GetCorners()

    let mutable minPoint = Vector3(Single.MaxValue)
    let mutable maxPoint = Vector3(Single.MinValue)

    for i = 0 to corners.Length - 1 do
      let transformed = Vector3.Transform(corners.[i], worldMatrix)
      minPoint <- Vector3.Min(minPoint, transformed)
      maxPoint <- Vector3.Max(maxPoint, transformed)

    BoundingBox(minPoint, maxPoint)

  let inline rayIntersects (ray: Ray) (box: BoundingBox) : float32 voption =
    let distance = ray.Intersects(box)

    if distance.HasValue then
      ValueSome distance.Value
    else
      ValueNone

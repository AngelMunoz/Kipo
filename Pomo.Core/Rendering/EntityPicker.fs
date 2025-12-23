namespace Pomo.Core.Rendering

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Graphics

module EntityPicker =

  [<Struct>]
  type PickResult = {
    EntityId: Guid<EntityId>
    Distance: float32
  }

  let inline private computeEntityWorldMatrix
    (logicPos: Vector2)
    (facing: float32)
    (pixelsPerUnit: Vector2)
    (modelScale: float32)
    (squishFactor: float32)
    =
    let renderPos = RenderMath.LogicRender.toRender logicPos 0.0f pixelsPerUnit

    RenderMath.WorldMatrix.createMesh renderPos facing modelScale squishFactor

  let pickEntity
    (ray: Ray)
    (pixelsPerUnit: Vector2)
    (modelScale: float32)
    (squishFactor: float32)
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (rotations: HashMap<Guid<EntityId>, float32>)
    (excludeEntityId: Guid<EntityId>)
    : Guid<EntityId> voption =

    let mutable nearestEntity = ValueNone
    let mutable nearestDistance = Single.MaxValue

    for entityId, logicPos in positions do
      if entityId <> excludeEntityId then
        // 1. Compute translation (cheap)
        let renderPos =
          RenderMath.LogicRender.toRender logicPos 0.0f pixelsPerUnit

        // 2. Broad Phase Culling
        // Sphere Center: slightly above feet
        // Radius: ~2.0 units (very generous for 0.25 scaled models)
        let sphereCenter = renderPos + Vector3(0.0f, 1.0f, 0.0f)
        let broadPhaseSphere = BoundingSphere(sphereCenter, 2.0f)

        if ray.Intersects(broadPhaseSphere).HasValue then
          let facing =
            match rotations |> HashMap.tryFindV entityId with
            | ValueSome r -> r
            | ValueNone -> 0.0f

          let worldMatrix =
            RenderMath.WorldMatrix.createMesh
              renderPos
              facing
              modelScale
              squishFactor

          let worldBox =
            Picking.transformBoundingBox Picking.EntityHitBox worldMatrix


          match Picking.rayIntersects ray worldBox with
          | ValueSome distance when distance < nearestDistance ->
            nearestDistance <- distance
            nearestEntity <- ValueSome entityId
          | _ -> ()

    nearestEntity

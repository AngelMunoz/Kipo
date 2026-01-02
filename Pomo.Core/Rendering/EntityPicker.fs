namespace Pomo.Core.Rendering

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Graphics
open Pomo.Core.Domain.BlockMap

module EntityPicker =
  open System.Collections.Generic

  let pickEntityBlockMap3D
    (ray: Ray)
    (ppu: float32)
    (blockMap: BlockMapDefinition)
    (modelScale: float32)
    (positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>)
    (rotations: IReadOnlyDictionary<Guid<EntityId>, float32>)
    (excludeEntityId: Guid<EntityId>)
    : Guid<EntityId> voption =

    let centerOffset =
      RenderMath.BlockMap3D.calcCenterOffset blockMap.Width blockMap.Depth ppu

    let mutable nearestEntity = ValueNone
    let mutable nearestDistance = Single.MaxValue

    for KeyValue(entityId, logicPos) in positions do
      if entityId <> excludeEntityId then
        let renderPos = RenderMath.BlockMap3D.toRender logicPos ppu centerOffset

        let hitBoxCenterY =
          (Picking.EntityHitBox.Min.Y + Picking.EntityHitBox.Max.Y) * 0.5f

        let hitBoxExtents = Picking.EntityHitBox.Max - Picking.EntityHitBox.Min

        let broadPhaseRadius = hitBoxExtents.Length() * 0.5f * modelScale

        let sphereCenter =
          renderPos + Vector3(0.0f, hitBoxCenterY * modelScale, 0.0f)

        let broadPhaseSphere = BoundingSphere(sphereCenter, broadPhaseRadius)

        if ray.Intersects(broadPhaseSphere).HasValue then
          let facing =
            match rotations |> Dictionary.tryFindV entityId with
            | ValueSome r -> r
            | ValueNone -> 0.0f

          let worldMatrix =
            RenderMath.WorldMatrix3D.createMesh renderPos facing modelScale

          let worldBox =
            Picking.transformBoundingBox Picking.EntityHitBox worldMatrix

          match Picking.rayIntersects ray worldBox with
          | ValueSome distance when distance < nearestDistance ->
            nearestDistance <- distance
            nearestEntity <- ValueSome entityId
          | _ -> ()

    nearestEntity

  let pickEntity
    (ray: Ray)
    (pixelsPerUnit: Vector2)
    (modelScale: float32)
    (squishFactor: float32)
    (positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>)
    (rotations: IReadOnlyDictionary<Guid<EntityId>, float32>)
    (excludeEntityId: Guid<EntityId>)
    : Guid<EntityId> voption =

    let mutable nearestEntity = ValueNone
    let mutable nearestDistance = Single.MaxValue

    for KeyValue(entityId, logicPos) in positions do
      if entityId <> excludeEntityId then
        // 1. Compute translation (cheap)
        let renderPos = RenderMath.LogicRender.toRender logicPos pixelsPerUnit

        // 2. Broad Phase Culling
        // Sphere Center: slightly above feet
        // Radius: ~2.0 units (very generous for 0.25 scaled models)
        let sphereCenter = renderPos + Vector3(0.0f, 1.0f, 0.0f)
        let broadPhaseSphere = BoundingSphere(sphereCenter, 2.0f)

        if ray.Intersects(broadPhaseSphere).HasValue then
          let facing =
            match rotations |> Dictionary.tryFindV entityId with
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

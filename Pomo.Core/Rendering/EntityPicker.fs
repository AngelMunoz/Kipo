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
    (getPickBounds: string -> BoundingBox voption)
    (ppu: float32)
    (blockMap: BlockMapDefinition)
    (modelScale: float32)
    (positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>)
    (rotations: IReadOnlyDictionary<Guid<EntityId>, float32>)
    (modelConfigIds: IReadOnlyDictionary<Guid<EntityId>, string>)
    (excludeEntityId: Guid<EntityId>)
    : Guid<EntityId> voption =

    let centerOffset =
      RenderMath.BlockMap3D.calcCenterOffset blockMap.Width blockMap.Depth ppu

    let mutable nearestEntity = ValueNone
    let mutable nearestDistance = Single.MaxValue

    for KeyValue(entityId, logicPos) in positions do
      if entityId <> excludeEntityId then
        let renderPos = RenderMath.BlockMap3D.toRender logicPos ppu centerOffset

        let localHitBox =
          match modelConfigIds.TryGetValue entityId with
          | true, configId ->
            match getPickBounds configId with
            | ValueSome b -> b
            | ValueNone -> Picking.EntityHitBox
          | false, _ -> Picking.EntityHitBox

        let maxAbsX = max (abs localHitBox.Min.X) (abs localHitBox.Max.X)
        let maxAbsY = max (abs localHitBox.Min.Y) (abs localHitBox.Max.Y)
        let maxAbsZ = max (abs localHitBox.Min.Z) (abs localHitBox.Max.Z)

        let localRadius =
          Vector3(maxAbsX, maxAbsY, maxAbsZ).Length()

        let broadPhaseSphere =
          BoundingSphere(renderPos, localRadius * modelScale)

        if ray.Intersects(broadPhaseSphere).HasValue then
          let facing =
            match rotations |> Dictionary.tryFindV entityId with
            | ValueSome r -> r
            | ValueNone -> 0.0f

          let worldMatrix =
            RenderMath.WorldMatrix3D.createMesh renderPos facing modelScale

          let worldBox =
            Picking.transformBoundingBox localHitBox worldMatrix

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

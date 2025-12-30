namespace Pomo.Core.Rendering

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Projectile
open Pomo.Core.Domain.Animation
open Pomo.Core.Graphics
open Pomo.Core.Projections

/// Pre-resolved rig node for command emission
[<Struct>]
type ResolvedRigNode = {
  ModelAsset: string
  WorldMatrix: Matrix
}

/// Pre-resolved entity ready for parallel command emission
type ResolvedEntity = {
  EntityId: Guid<EntityId>
  Nodes: ResolvedRigNode[]
}

module PoseResolver =

  /// Gets entity facing from rotations
  let inline private getEntityFacing
    (rotations: IReadOnlyDictionary<Guid<EntityId>, float32>)
    (entityId: Guid<EntityId>)
    =
    match rotations.TryGetValue entityId with
    | true, r -> r
    | false, _ -> 0.0f

  /// Cached empty pose to avoid per-frame allocations
  let private emptyPose: System.Collections.Generic.Dictionary<string, Matrix> =
    System.Collections.Generic.Dictionary()

  /// Gets entity pose or empty
  let inline private getEntityPose
    (poses:
      System.Collections.Generic.IReadOnlyDictionary<
        Guid<EntityId>,
        System.Collections.Generic.Dictionary<string, Matrix>
       >)
    (entityId: Guid<EntityId>)
    =
    match poses |> Dictionary.tryFindV entityId with
    | ValueSome pose -> pose
    | ValueNone -> emptyPose

  /// Computes projectile altitude
  let inline private computeAltitude
    (liveProjectiles: HashMap<Guid<EntityId>, LiveProjectile>)
    (entityId: Guid<EntityId>)
    (pixelsPerUnitY: float32)
    =
    match liveProjectiles |> HashMap.tryFindV entityId with
    | ValueSome proj ->
      match proj.Info.Variations with
      | ValueSome(Descending(currentAltitude, _)) ->
        currentAltitude / pixelsPerUnitY
      | _ -> 0.0f
    | ValueNone -> 0.0f

  /// Computes projectile tilt and facing
  let inline private getProjectileTiltAndFacing
    (altitude: float32)
    (facing: float32)
    =
    if altitude > 0.0f then
      struct (0.0f, 0.0f)
    else
      struct (MathHelper.PiOver2, facing)

  /// Gets node animation matrix
  let inline private getNodeAnimation
    (entityPose: IReadOnlyDictionary<string, Matrix>)
    (nodeName: string)
    =
    match entityPose |> Dictionary.tryFindV nodeName with
    | ValueSome m -> m
    | ValueNone -> Matrix.Identity

  /// Collects path from node to root (tail-recursive)
  [<TailCall>]
  let rec private collectPath
    (nodeTransforms: Dictionary<string, Matrix>)
    (rigData: HashMap<string, RigNode>)
    (currentName: string)
    (currentNode: RigNode)
    (path: struct (string * RigNode) list)
    : struct (struct (string * RigNode) list * Matrix) =
    if nodeTransforms.ContainsKey currentName then
      struct (path, nodeTransforms.[currentName])
    else
      match currentNode.Parent with
      | ValueNone ->
        struct (struct (currentName, currentNode) :: path, Matrix.Identity)
      | ValueSome pName ->
        match rigData |> HashMap.tryFindV pName with
        | ValueNone ->
          struct (struct (currentName, currentNode) :: path, Matrix.Identity)
        | ValueSome pNode ->
          collectPath
            nodeTransforms
            rigData
            pName
            pNode
            (struct (currentName, currentNode) :: path)

  /// Applies transforms along rig hierarchy (tail-recursive)
  [<TailCall>]
  let rec private applyTransforms
    (entityPose: IReadOnlyDictionary<string, Matrix>)
    (nodeTransforms: Dictionary<string, Matrix>)
    (struct (nodes, currentParentWorld):
      struct (struct (string * RigNode) list * Matrix))
    : Matrix =
    match nodes with
    | [] -> currentParentWorld
    | struct (name, node) :: rest ->
      let localAnim = getNodeAnimation entityPose name

      let localWorld =
        RenderMath.Rig.applyNodeTransform node.Pivot node.Offset localAnim

      let world = localWorld * currentParentWorld
      nodeTransforms.[name] <- world
      applyTransforms entityPose nodeTransforms (struct (rest, world))

  /// Resolves a single entity's rig nodes (writes to output ResizeArray)
  let inline private resolveEntity
    (core: RenderCore)
    (data: EntityRenderData)
    (snapshot: MovementSnapshot)
    (nodeTransformsPool: Dictionary<string, Matrix>)
    (nodesBuffer: ResizeArray<ResolvedRigNode>)
    (entityId: Guid<EntityId>)
    (logicPos: WorldPosition)
    : ResolvedEntity voption =

    match snapshot.ModelConfigIds.TryGetValue entityId with
    | false, _ -> ValueNone
    | true, configId ->
      match data.ModelStore.tryFind configId with
      | ValueNone -> ValueNone
      | ValueSome config ->
        let facingBase = getEntityFacing snapshot.Rotations entityId
        let facing = facingBase + config.FacingOffset
        let entityPose = getEntityPose data.EntityPoses entityId
        let isProjectile = data.LiveProjectiles |> HashMap.containsKey entityId

        let altitude =
          computeAltitude data.LiveProjectiles entityId core.PixelsPerUnit.Y

        let posWithAltitude = {
          logicPos with
              Y = logicPos.Y + altitude
        }

        let renderPos =
          RenderMath.LogicRender.toRender posWithAltitude core.PixelsPerUnit

        let entityBaseMatrix =
          if isProjectile then
            let struct (tilt, projFacing) =
              getProjectileTiltAndFacing altitude facing

            RenderMath.WorldMatrix.createProjectile
              renderPos
              projFacing
              tilt
              data.ModelScale
              data.SquishFactor
          else
            RenderMath.WorldMatrix.createMesh
              renderPos
              facing
              data.ModelScale
              data.SquishFactor

        nodeTransformsPool.Clear()
        nodesBuffer.Clear()

        // Direct iteration over rig - no HashMap.toArray
        for nodeName, node in config.Rig do

          let nodeLocalWorld =
            collectPath nodeTransformsPool config.Rig nodeName node []
            |> applyTransforms entityPose nodeTransformsPool

          let finalWorld = nodeLocalWorld * entityBaseMatrix

          nodesBuffer.Add {
            ModelAsset = node.ModelAsset
            WorldMatrix = finalWorld
          }

        ValueSome {
          EntityId = entityId
          Nodes = nodesBuffer.ToArray()
        }

  /// Resolves all entity poses in one sequential pass.
  /// Uses shared pools for efficiency - minimal allocations.
  let resolveAll
    (core: RenderCore)
    (data: EntityRenderData)
    (snapshot: MovementSnapshot)
    (nodeTransformsPool: Dictionary<string, Matrix>)
    : ResolvedEntity[] =
    // Pre-allocate output buffer based on entity count
    let estimatedCount = snapshot.Positions.Count
    let results = ResizeArray<ResolvedEntity>(estimatedCount)
    let nodesBuffer = ResizeArray<ResolvedRigNode>(16)

    for kv in snapshot.Positions do
      let entityId = kv.Key
      let logicPos = kv.Value

      resolveEntity
        core
        data
        snapshot
        nodeTransformsPool
        nodesBuffer
        entityId
        logicPos
      |> ValueOption.iter results.Add

    results.ToArray()

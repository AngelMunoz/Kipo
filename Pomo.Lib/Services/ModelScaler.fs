namespace Pomo.Lib.Services

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Pomo.Lib

/// Service for automatically computing model scales based on bounding boxes
type ModelScalerService =
  /// Get the computed scale for a model to fit in a 1x1x1 grid cell
  /// Returns 1.0f if model cannot be loaded
  abstract GetScale: modelPath: string -> float32
  /// Get the offset needed to center the model in a grid cell
  /// Returns Vector3.Zero if model cannot be loaded
  abstract GetCenterOffset: modelPath: string -> Vector3

/// Capability interface for accessing ModelScalerService
type ModelScalerCap =
  abstract ModelScaler: ModelScalerService

module ModelScaler =
  /// Compute bounding box from model meshes using actual vertex data
  let private computeModelBounds(model: Model) : BoundingBox voption =
    let mutable hasAny = false
    let mutable minVec = Vector3(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue)
    let mutable maxVec = Vector3(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue)

    for mesh in model.Meshes do
      let meshTransform = mesh.ParentBone.Transform

      for part in mesh.MeshParts do
        let vertexBuffer = part.VertexBuffer
        let declaration = vertexBuffer.VertexDeclaration
        let vertexSize = declaration.VertexStride
        let vertexData = Array.zeroCreate<byte>(vertexBuffer.VertexCount * vertexSize)
        vertexBuffer.GetData(vertexData)

        // Find position element in declaration
        let posElementOpt =
          declaration.GetVertexElements()
          |> Seq.tryFind(fun e -> e.VertexElementUsage = VertexElementUsage.Position)

        match posElementOpt with
        | Some posElement ->
          let offset = posElement.Offset
          let mutable i = 0

          while i < vertexBuffer.VertexCount do
            let start = i * vertexSize + offset
            let x = BitConverter.ToSingle(vertexData, start)
            let y = BitConverter.ToSingle(vertexData, start + 4)
            let z = BitConverter.ToSingle(vertexData, start + 8)

            let localPos = Vector3(x, y, z)
            let worldPos = Vector3.Transform(localPos, meshTransform)

            minVec <- Vector3.Min(minVec, worldPos)
            maxVec <- Vector3.Max(maxVec, worldPos)
            hasAny <- true
            i <- i + 1
        | None -> ()

    if hasAny then
      ValueSome(BoundingBox(minVec, maxVec))
    else
      ValueNone

  /// Compute scale to fit model in a CellSize x CellSize x CellSize grid cell
  let private computeScale(bounds: BoundingBox) : float32 =
    let size = bounds.Max - bounds.Min
    let maxDimension = Math.Max(Math.Max(size.X, size.Y), size.Z)
    if maxDimension > 0.0001f then
      GridDimensions.CellSize / maxDimension
    else
      GridDimensions.CellSize

  /// Compute the center of the model bounds (for centering in grid cells)
  let private computeBoundsCenter(bounds: BoundingBox) : Vector3 =
    (bounds.Min + bounds.Max) * 0.5f

  [<Struct>]
  type private ModelMetrics = {
    Scale: float32
    CenterOffset: Vector3
  }

  let live(ctx: GameContext) : ModelScalerService =
    let metricsCache = Dictionary<string, ModelMetrics>()
    let lockObj = obj()

    let getMetrics modelPath =
      lock lockObj (fun () ->
        match metricsCache.TryGetValue(modelPath) with
        | true, cached -> cached
        | false, _ ->
          try
            let model = ctx.Content.Load<Model>(modelPath)
            let metrics =
              match computeModelBounds model with
              | ValueSome bounds ->
                let scale = computeScale bounds
                let centerOffset = computeBoundsCenter bounds
                { Scale = scale; CenterOffset = centerOffset }
              | ValueNone ->
                { Scale = 1.0f; CenterOffset = Vector3.Zero }

            metricsCache.[modelPath] <- metrics
            metrics
          with _ ->
            { Scale = 1.0f; CenterOffset = Vector3.Zero }
      )

    { new ModelScalerService with
        member _.GetScale(modelPath) =
          (getMetrics modelPath).Scale
        member _.GetCenterOffset(modelPath) =
          (getMetrics modelPath).CenterOffset
    }

  /// Helper to get scale from environment
  let getScale (env: #ModelScalerCap) modelPath =
    env.ModelScaler.GetScale modelPath

  /// Helper to get center offset from environment
  let getCenterOffset (env: #ModelScalerCap) modelPath =
    env.ModelScaler.GetCenterOffset modelPath

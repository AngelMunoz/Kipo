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
  /// Compute bounding box from model meshes using BoundingSpheres
  let private computeModelBounds(model: Model) : BoundingBox voption =
    let mutable hasAny = false
    let mutable minVec = Vector3(System.Single.MaxValue, System.Single.MaxValue, System.Single.MaxValue)
    let mutable maxVec = Vector3(System.Single.MinValue, System.Single.MinValue, System.Single.MinValue)

    for mesh in model.Meshes do
      // Use BoundingSphere for each mesh
      let sphere = mesh.BoundingSphere
      let center = Vector3.Transform(sphere.Center, mesh.ParentBone.Transform)
      let radius = sphere.Radius

      // Approximate box from sphere
      let boxMin = center - Vector3(radius, radius, radius)
      let boxMax = center + Vector3(radius, radius, radius)

      minVec <- Vector3.Min(minVec, boxMin)
      maxVec <- Vector3.Max(maxVec, boxMax)
      hasAny <- true

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

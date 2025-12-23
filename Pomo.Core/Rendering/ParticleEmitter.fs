namespace Pomo.Core.Rendering

open System
open System.Buffers
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Particles
open Pomo.Core.Graphics
open Pomo.Core.Stores

module ParticleEmitter =

  /// Pre-loads all particle textures and mesh models from ParticleStore
  let loadAssets
    (content: ContentManager)
    (particleStore: ParticleStore)
    : struct (IReadOnlyDictionary<string, Texture2D> *
      IReadOnlyDictionary<string, Model>)
    =
    let textureCache = Dictionary<string, Texture2D>()
    let modelCache = Dictionary<string, Model>()

    for _, emitters in particleStore.all() do
      for emitter in emitters do
        match emitter.RenderMode with
        | Billboard texturePath ->
          if not(textureCache.ContainsKey texturePath) then
            try
              let texture = content.Load<Texture2D>(texturePath)
              textureCache[texturePath] <- texture
            with _ ->
              ()
        | Mesh modelPath ->
          if not(modelCache.ContainsKey modelPath) then
            try
              let model = content.Load<Model>(modelPath)
              modelCache[modelPath] <- model
            with _ ->
              ()

    struct (textureCache, modelCache)

  let inline private computeEffectPosition
    (owner: Guid<EntityId> voption)
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (fallbackPos: Vector3)
    =
    match owner with
    | ValueSome ownerId ->
      positions
      |> HashMap.tryFindV ownerId
      |> ValueOption.map(fun p -> Vector3(p.X, 0.0f, p.Y))
      |> ValueOption.defaultValue fallbackPos
    | ValueNone -> fallbackPos

  let inline private computeParticleWorldPosition
    (config: EmitterConfig)
    (particlePos: Vector3)
    (effectPos: Vector3)
    (effectRotation: Quaternion)
    =
    match config.SimulationSpace with
    | SimulationSpace.World -> particlePos
    | SimulationSpace.Local ->
      Vector3.Transform(particlePos, effectRotation) + effectPos

  let inline private transformBillboardParticle
    (texture: Texture2D)
    (blendMode: BlendMode)
    (config: EmitterConfig)
    (effectPos: Vector3)
    (effectRotation: Quaternion)
    (pixelsPerUnit: Vector2)
    (modelScale: float32)
    (particle: Particle)
    : BillboardCommand =
    let worldPos =
      computeParticleWorldPosition
        config
        particle.Position
        effectPos
        effectRotation

    let renderPos = RenderMath.ParticleToRender worldPos pixelsPerUnit

    {
      Texture = texture
      Position = renderPos
      Size = particle.Size * modelScale
      Color = particle.Color
      BlendMode = blendMode
    }

  let inline private transformMeshParticle
    (model: Model)
    (config: EmitterConfig)
    (effectPos: Vector3)
    (effectRotation: Quaternion)
    (pixelsPerUnit: Vector2)
    (modelScale: float32)
    (squishFactor: float32)
    (particle: MeshParticle)
    : MeshCommand =
    let worldPos =
      computeParticleWorldPosition
        config
        particle.Position
        effectPos
        effectRotation

    let renderPos = RenderMath.ParticleToRender worldPos pixelsPerUnit

    let worldMatrix =
      RenderMath.CreateMeshParticleWorldMatrix
        renderPos
        particle.Rotation
        (particle.Scale * modelScale)
        config.ScaleAxis
        config.ScalePivot
        squishFactor

    {
      Model = model
      WorldMatrix = worldMatrix
    }

  let inline private collectWithPool<'T>
    (estimatedCount: int)
    ([<InlineIfLambda>] fillBuffer: 'T[] -> int)
    : 'T[] =
    if estimatedCount = 0 then
      Array.empty
    else
      let rented = ArrayPool<'T>.Shared.Rent(estimatedCount)
      let count = fillBuffer rented
      let result = Array.init count (fun i -> rented[i])
      ArrayPool<'T>.Shared.Return(rented, clearArray = false)
      result

  let private collectBillboardsForEffect
    (core: RenderCore)
    (data: ParticleRenderData)
    (effect: VisualEffect)
    : BillboardCommand[] =

    let effectPos =
      computeEffectPosition
        effect.Owner
        data.EntityPositions
        effect.Position.Value

    let effectRotation = effect.Rotation.Value

    // Count particles
    let mutable estimatedCount = 0

    for emitter in effect.Emitters do
      match emitter.Config.RenderMode with
      | Billboard _ ->
        estimatedCount <- estimatedCount + emitter.Particles.Count
      | Mesh _ -> ()

    collectWithPool estimatedCount (fun buffer ->
      let mutable idx = 0

      for emitter in effect.Emitters do
        match emitter.Config.RenderMode with
        | Billboard textureId ->
          data.GetTexture textureId
          |> ValueOption.iter(fun texture ->
            for particle in emitter.Particles do
              buffer[idx] <-
                transformBillboardParticle
                  texture
                  emitter.Config.BlendMode
                  emitter.Config
                  effectPos
                  effectRotation
                  core.PixelsPerUnit
                  data.ModelScale
                  particle

              idx <- idx + 1)
        | Mesh _ -> ()

      idx)

  let private collectMeshesForEffect
    (core: RenderCore)
    (data: ParticleRenderData)
    (effect: VisualEffect)
    : MeshCommand[] =

    let effectPos = effect.Position.Value
    let effectRotation = effect.Rotation.Value

    let mutable estimatedCount = 0

    for meshEmitter in effect.MeshEmitters do
      estimatedCount <- estimatedCount + meshEmitter.Particles.Count

    collectWithPool estimatedCount (fun buffer ->
      let mutable idx = 0

      for meshEmitter in effect.MeshEmitters do
        data.GetModelByAsset meshEmitter.ModelPath
        |> ValueOption.iter(fun model ->
          for particle in meshEmitter.Particles do
            buffer[idx] <-
              transformMeshParticle
                model
                meshEmitter.Config
                effectPos
                effectRotation
                core.PixelsPerUnit
                data.ModelScale
                data.SquishFactor
                particle

            idx <- idx + 1)

      idx)

  let emitBillboards
    (core: RenderCore)
    (data: ParticleRenderData)
    (effects: VisualEffect[])
    : BillboardCommand[] =
    effects
    |> Array.Parallel.collect(fun effect ->
      if effect.IsAlive.Value then
        collectBillboardsForEffect core data effect
      else
        Array.empty)

  let emitMeshes
    (core: RenderCore)
    (data: ParticleRenderData)
    (effects: VisualEffect[])
    : MeshCommand[] =
    effects
    |> Array.Parallel.collect(fun effect ->
      if effect.IsAlive.Value then
        collectMeshesForEffect core data effect
      else
        Array.empty)

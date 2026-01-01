namespace Pomo.Core.Rendering

open System
open System.Buffers
open System.Collections.Concurrent
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Particles
open Pomo.Core.Graphics
open Pomo.Core.Stores

module ParticleEmitter =

  /// Creates thread-safe lazy loaders for particle assets
  let createLazyAssetLoaders
    (content: ContentManager)
    : struct ((string -> Texture2D voption) * (string -> Model voption)) =

    let textureCache = ConcurrentDictionary<string, Lazy<Texture2D voption>>()
    let modelCache = ConcurrentDictionary<string, Lazy<Model voption>>()

    let getTexture path =
      let loader =
        textureCache.GetOrAdd(
          path,
          fun p ->
            lazy
              (try
                lock content (fun () ->
                  let tex = content.Load<Texture2D>(p)
                  ValueSome tex)
               with ex ->
                 printfn
                   $"[ParticleEmitter] Failed to load texture: {p} - {ex.Message}"

                 ValueNone)
        )

      loader.Value

    let getModel path =
      let loader =
        modelCache.GetOrAdd(
          path,
          fun p ->
            lazy
              (try
                lock content (fun () ->
                  let model = content.Load<Model>(p)
                  ValueSome model)
               with ex ->
                 printfn
                   $"[ParticleEmitter] Failed to load model: {p} - {ex.Message}"

                 ValueNone)
        )

      loader.Value

    struct (getTexture, getModel)

  /// Pre-loads all particle textures and mesh models from ParticleStore
  let loadAssets
    (content: ContentManager)
    (particleStore: ParticleStore)
    : struct (IReadOnlyDictionary<string, Texture2D> *
      IReadOnlyDictionary<string, LoadedModel>)
    =
    let textureCache = Dictionary<string, Texture2D>()
    let modelCache = Dictionary<string, LoadedModel>()

    for _, emitters in particleStore.all() do
      for emitter in emitters do
        match emitter.RenderMode with
        | Billboard texturePath ->
          if not(textureCache.ContainsKey texturePath) then
            try
              let texture = content.Load<Texture2D>(texturePath)
              textureCache[texturePath] <- texture
            with ex ->
              printfn
                $"[ParticleEmitter] Failed to load texture: {texturePath} - {ex.Message}"
        | Mesh modelPath ->
          if not(modelCache.ContainsKey modelPath) then
            try
              let model = content.Load<Model>(modelPath)
              let loaded = LoadedModel.fromModel model
              modelCache[modelPath] <- loaded

              if not loaded.HasNormals then
                printfn
                  $"[ParticleEmitter] Model '{modelPath}' missing normals, lighting will be disabled"
            with ex ->
              printfn
                $"[ParticleEmitter] Failed to load model: {modelPath} - {ex.Message}"

    struct (textureCache, modelCache)

  let inline private computeEffectPosition
    (owner: Guid<EntityId> voption)
    (positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>)
    (fallbackPos: Vector3)
    =
    match owner with
    | ValueSome ownerId ->
      positions
      |> Dictionary.tryFindV ownerId
      |> ValueOption.map(fun p -> WorldPosition.toVector3 p)
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
    (core: RenderCore)
    (modelScale: float32)
    (particle: Particle)
    : BillboardCommand =
    let worldPos =
      computeParticleWorldPosition
        config
        particle.Position
        effectPos
        effectRotation

    let renderPos = core.ToRenderParticlePos worldPos

    {
      Texture = texture
      Position = renderPos
      Size = particle.Size * modelScale
      Color = particle.Color
      BlendMode = blendMode
    }

  let inline private transformMeshParticle
    (loadedModel: LoadedModel)
    (config: EmitterConfig)
    (effectPos: Vector3)
    (effectRotation: Quaternion)
    (core: RenderCore)
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

    let renderPos = core.ToRenderParticlePos worldPos

    let worldMatrix =
      match core.Space with
      | RenderSpace.Isometric ->
        RenderMath.WorldMatrix.createMeshParticle
          renderPos
          particle.Rotation
          (particle.Scale * modelScale)
          config.ScaleAxis
          config.ScalePivot
          squishFactor
      | RenderSpace.True3D ->
        RenderMath.WorldMatrix3D.createMeshParticle
          renderPos
          particle.Rotation
          (particle.Scale * modelScale)
          config.ScaleAxis
          config.ScalePivot

    {
      LoadedModel = loadedModel
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
                  core
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
        data.GetLoadedModelByAsset meshEmitter.ModelPath
        |> ValueOption.iter(fun loadedModel ->
          for particle in meshEmitter.Particles do
            buffer[idx] <-
              transformMeshParticle
                loadedModel
                meshEmitter.Config
                effectPos
                effectRotation
                core
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

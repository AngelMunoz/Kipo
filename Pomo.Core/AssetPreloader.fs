namespace Pomo.Core

open System
open System.Collections.Generic
open System.Collections.Concurrent
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain
open Pomo.Core.Domain.AssetManifest
open Pomo.Core.Serialization

module AssetPreloader =

  module Constants =
    /// Minimum duration to show loading overlay for consistency
    let MinLoadingOverlayDuration = TimeSpan.FromMilliseconds(50.0)

  /// Collect assets using heuristics when no manifest exists
  let discoverHeuristicAssets
    (mapDef: Map.MapDefinition)
    (modelStore: Stores.ModelStore)
    (particleStore: Stores.ParticleStore)
    : ManifestAssets =

    let models = HashSet<string>()
    let textures = HashSet<string>()

    // 1. All entity models from ModelStore
    for modelConfig in modelStore.all() do
      for _, node in modelConfig.Rig do
        models.Add(node.ModelAsset) |> ignore

    // 2. All particle assets from ParticleStore
    for _, emitters in particleStore.all() do
      for emitter in emitters do
        match emitter.RenderMode with
        | Particles.Billboard texPath -> textures.Add(texPath) |> ignore
        | Particles.Mesh modelPath -> models.Add(modelPath) |> ignore

    {
      Models = models |> Seq.toArray
      Textures = textures |> Seq.toArray
      Icons = [||] // Future
    }

  /// Load a manifest from disk, or return UseHeuristics
  let tryLoadManifest(mapKey: string) : ManifestLookup =
    let manifestPath = $"AssetManifests/{mapKey}"

    try
      let deserializer = Serialization.create()

      match
        JsonFileLoader.readJson
          deserializer
          Serialization.LoadingManifest.decoder
          manifestPath
      with
      | Ok manifest -> Explicit manifest
      | Error _ -> UseHeuristics
    with _ ->
      UseHeuristics

  /// Get assets to preload (manifest or heuristics)
  let getAssetsToPreload
    (mapKey: string)
    (mapDef: Map.MapDefinition)
    (modelStore: Stores.ModelStore)
    (particleStore: Stores.ParticleStore)
    : ManifestAssets =

    match tryLoadManifest mapKey with
    | Explicit manifest ->
      printfn $"[AssetPreloader] Using manifest for {mapKey}"
      manifest.Assets
    | UseHeuristics ->
      printfn
        $"[AssetPreloader] No manifest found for {mapKey}, using heuristics"

      discoverHeuristicAssets mapDef modelStore particleStore

  /// Preload models into cache
  let preloadModels
    (content: ContentManager)
    (modelCache: ConcurrentDictionary<string, Lazy<Model>>)
    (modelPaths: string[])
    =
    let mutable loaded = 0
    let mutable failed = 0

    for path in modelPaths do
      if not(modelCache.ContainsKey(path)) then
        try
          let model = content.Load<Model>(path)
          modelCache[path] <- Lazy<Model>(fun () -> model)
          loaded <- loaded + 1
        with ex ->
          printfn
            $"[AssetPreloader] Failed to load model: {path} - {ex.Message}"

          failed <- failed + 1

    struct (loaded, failed)

  /// Preload textures into cache
  let preloadTextures
    (content: ContentManager)
    (textureCache: ConcurrentDictionary<string, Lazy<Texture2D>>)
    (texturePaths: string[])
    =
    let mutable loaded = 0
    let mutable failed = 0

    for path in texturePaths do
      if not(textureCache.ContainsKey(path)) then
        try
          let texture = content.Load<Texture2D>(path)
          textureCache[path] <- Lazy<Texture2D>(fun () -> texture)
          loaded <- loaded + 1
        with ex ->
          printfn
            $"[AssetPreloader] Failed to load texture: {path} - {ex.Message}"

          failed <- failed + 1

    struct (loaded, failed)

  /// Main preload function
  /// Returns (total assets attempted, total loaded, total failed)
  let preloadAssets
    (content: ContentManager)
    (modelCache: ConcurrentDictionary<string, Lazy<Model>>)
    (textureCache: ConcurrentDictionary<string, Lazy<Texture2D>>)
    (mapKey: string)
    (mapDef: Map.MapDefinition)
    (modelStore: Stores.ModelStore)
    (particleStore: Stores.ParticleStore)
    : struct (int * int * int) =

    let assets = getAssetsToPreload mapKey mapDef modelStore particleStore

    let struct (modelsLoaded, modelsFailed) =
      preloadModels content modelCache assets.Models

    let struct (texturesLoaded, texturesFailed) =
      preloadTextures content textureCache assets.Textures

    let totalAttempted = assets.Models.Length + assets.Textures.Length
    let totalLoaded = modelsLoaded + texturesLoaded
    let totalFailed = modelsFailed + texturesFailed

    printfn
      $"[AssetPreloader] Preloaded {totalLoaded}/{totalAttempted} assets ({modelsFailed + texturesFailed} failed)"

    struct (totalAttempted, totalLoaded, totalFailed)

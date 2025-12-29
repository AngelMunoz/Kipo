namespace Pomo.Core.Rendering

open System.Collections.Concurrent
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open FSharp.Data.Adaptive
open Pomo.Core.Graphics
open Pomo.Core.Projections
open Pomo.Core.Stores

module EntityEmitter =

  let createLazyModelLoader
    (content: ContentManager)
    : string -> LoadedModel voption =
    let cache = ConcurrentDictionary<string, Lazy<LoadedModel voption>>()

    fun path ->
      let loader =
        cache.GetOrAdd(
          path,
          fun p ->
            lazy
              (try
                lock content (fun () ->
                  let model = content.Load<Model>(p)
                  let loaded = LoadedModel.fromModel model

                  if not loaded.HasNormals then
                    printfn
                      $"[EntityEmitter] Model '{p}' missing normals, lighting will be disabled"

                  ValueSome loaded)
               with ex ->
                 printfn
                   $"[EntityEmitter] Failed to load model: {p} - {ex.Message}"

                 ValueNone)
        )

      loader.Value

  let loadModels
    (content: ContentManager)
    (modelStore: ModelStore)
    : IReadOnlyDictionary<string, LoadedModel> =
    let cache = Dictionary<string, LoadedModel>()

    for config in modelStore.all() do
      for _, node in config.Rig do
        if not(cache.ContainsKey node.ModelAsset) then
          try
            let model = content.Load<Model>(node.ModelAsset)
            let loaded = LoadedModel.fromModel model
            cache[node.ModelAsset] <- loaded

            if not loaded.HasNormals then
              printfn
                $"[EntityEmitter] Model '{node.ModelAsset}' missing normals, lighting will be disabled"
          with ex ->
            printfn
              $"[EntityEmitter] Failed to load model: {node.ModelAsset} - {ex.Message}"

    cache

  let emit
    (getLoadedModelByAsset: string -> LoadedModel voption)
    (entities: ResolvedEntity[])
    : MeshCommand[] =
    entities
    |> Array.Parallel.collect(fun entity ->
      entity.Nodes
      |> Array.choose(fun node ->
        getLoadedModelByAsset node.ModelAsset
        |> ValueOption.map(fun loadedModel -> {
          LoadedModel = loadedModel
          WorldMatrix = node.WorldMatrix
        })
        |> function
          | ValueSome cmd -> Some cmd
          | ValueNone -> None))

  let emitAll
    (core: RenderCore)
    (data: EntityRenderData)
    (snapshot: MovementSnapshot)
    (nodeTransformsPool: Dictionary<string, Matrix>)
    : MeshCommand[] =
    let resolvedEntities =
      PoseResolver.resolveAll core data snapshot nodeTransformsPool

    emit data.GetLoadedModelByAsset resolvedEntities

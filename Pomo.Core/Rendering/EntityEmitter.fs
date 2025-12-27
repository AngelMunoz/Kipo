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

  let createLazyModelLoader (content: ContentManager) : string -> Model voption =
      let cache = ConcurrentDictionary<string, Lazy<Model voption>>()
      fun path ->
          let loader = cache.GetOrAdd(path, fun p ->
              lazy (
                  try
                      lock content (fun () ->
                          let model = content.Load<Model>(p)
                          ValueSome model)
                  with _ -> ValueNone
              )
          )
          loader.Value

  let loadModels
    (content: ContentManager)
    (modelStore: ModelStore)
    : IReadOnlyDictionary<string, Model> =
    let cache = Dictionary<string, Model>()

    for config in modelStore.all() do
      for _, node in config.Rig do
        if not(cache.ContainsKey node.ModelAsset) then
          try
            let model = content.Load<Model>(node.ModelAsset)
            cache[node.ModelAsset] <- model
          with _ ->
            ()

    cache

  let emit
    (getModelByAsset: string -> Model voption)
    (entities: ResolvedEntity[])
    : MeshCommand[] =
    entities
    |> Array.Parallel.collect(fun entity ->
      entity.Nodes
      |> Array.choose(fun node ->
        getModelByAsset node.ModelAsset
        |> ValueOption.map(fun model -> {
          Model = model
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

    emit data.GetModelByAsset resolvedEntities

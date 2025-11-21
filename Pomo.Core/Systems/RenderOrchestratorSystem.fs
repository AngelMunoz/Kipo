namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Map
open Pomo.Core.Stores
open Pomo.Core.Domain

module RenderOrchestratorSystem =

  let private findRenderGroup(layer: Map.MapLayer) : int =
    match layer.Properties |> HashMap.tryFindV "RenderGroup" with
    | ValueSome value ->
      match Int32.TryParse(value) with
      | true, intValue -> intValue
      | false, _ -> 0
    | ValueNone -> 0

  type RenderOrchestratorSystem
    (game: Game, mapKey: string, playerId: Guid<EntityId>) =
    inherit GameComponent(game)

    // Store the components this orchestrator creates so they can be removed if needed
    let mutable createdComponents: IGameComponent list = [] // Renamed 'component' to 'comp' related vars where needed

    override _.Initialize() =
      base.Initialize()

      let mapStore = game.Services.GetService<MapStore>()
      let map = mapStore.find mapKey

      // Remove any previously created components to ensure a clean state,
      // especially important if maps are dynamically reloaded.
      for comp in createdComponents do // Renamed 'component' to 'comp'
        game.Components.Remove(comp) |> ignore

      createdComponents <- []

      // Group map layers by their RenderGroup property
      let layerGroups =
        map.Layers
        |> IndexList.fold
          (fun (acc: HashMap<int, MapLayer IndexList>) layer ->
            let group = findRenderGroup layer

            match acc |> HashMap.tryFindV group with
            | ValueSome layers -> acc.Add(group, layers |> IndexList.add layer)
            | ValueNone -> acc.Add(group, IndexList.single layer))
          HashMap.empty

      // Create and add TerrainRenderSystem for each group of layers
      for groupIndex, layers in layerGroups do
        let layerNames = layers |> IndexList.map(fun l -> l.Name)

        let terrainRenderer =
          new TerrainRenderSystem.TerrainRenderSystem(
            game,
            mapKey,
            layersToRender = layerNames
          ) // Explicitly qualified
        // Scale groupIndex to get distinct DrawOrder values
        terrainRenderer.DrawOrder <- groupIndex * 10
        game.Components.Add terrainRenderer

        createdComponents <-
          terrainRenderer :> IGameComponent :: createdComponents


      // Create and add RenderSystem for entities, using convention for RenderGroup = 1
      let entityRenderer = new Render.RenderSystem(game, playerId) // Explicitly qualified
      // Entities get DrawOrder 10 times their conventional RenderGroup (1)
      entityRenderer.DrawOrder <- 1 * 10
      game.Components.Add entityRenderer
      createdComponents <- entityRenderer :> IGameComponent :: createdComponents

    // For safety, add DebugRenderSystem last and ensure it has a high DrawOrder
    // if it's managed by this orchestrator, or ensure it's added separately with higher order.
    // Assuming DebugRenderSystem is added by PomoGame and will handle its own DrawOrder for now.

    override _.Dispose(disposing) =
      if disposing then
        // Remove all components created by this orchestrator when it's disposed
        for comp in createdComponents do // Renamed 'component' to 'comp'
          game.Components.Remove(comp) |> ignore

        createdComponents <- [] // Clear the list

      base.Dispose(disposing)

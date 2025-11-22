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
    let createdComponents = ResizeArray() // Renamed 'component' to 'comp' related vars where needed

    override _.Initialize() =
      base.Initialize()

      let mapStore = game.Services.GetService<MapStore>()
      let map = mapStore.find mapKey

      // Remove any previously created components to ensure a clean state,
      // especially important if maps are dynamically reloaded.
      for comp in createdComponents do // Renamed 'component' to 'comp'
        game.Components.Remove(comp) |> ignore

      createdComponents.Clear()

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
            layersToRender = layerNames,
            // Scale groupIndex to get distinct DrawOrder values
            DrawOrder = Render.Layer.TerrainBase + groupIndex * 20
          )

        game.Components.Add terrainRenderer

        createdComponents.Add(terrainRenderer :> IGameComponent)


      // Create and add RenderSystem for entities, using convention for RenderGroup = 1
      // Entities get DrawOrder 10 times their conventional RenderGroup (1)
      let entityRenderer =
        new Render.RenderSystem(
          game,
          playerId,
          DrawOrder = Render.Layer.Entities
        )

      game.Components.Add entityRenderer
      createdComponents.Add(entityRenderer :> IGameComponent)

    override _.Dispose(disposing) =
      if disposing then
        for comp in createdComponents do // Renamed 'component' to 'comp'
          game.Components.Remove(comp) |> ignore

        createdComponents.Clear()

      base.Dispose(disposing)

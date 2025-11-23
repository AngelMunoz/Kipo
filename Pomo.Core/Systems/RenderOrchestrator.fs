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
  open Pomo.Core.Projections

  let private findRenderGroup(layer: Map.MapLayer) : int =
    match layer.Properties |> HashMap.tryFindV "RenderGroup" with
    | ValueSome value ->
      match Int32.TryParse(value) with
      | true, intValue -> intValue
      | false, _ -> 0
    | ValueNone -> 0

  type RenderOrchestratorSystem
    (game: Game, mapKey: string, playerId: Guid<EntityId>) =
    inherit DrawableGameComponent(game)

    let mutable renderServices: Render.RenderService list = []
    let mutable terrainServices: TerrainRenderSystem.TerrainRenderService list = []

    let world = game.Services.GetService<World.World>()

    let targetingService =
      game.Services.GetService<Targeting.TargetingService>()

    let projections = game.Services.GetService<ProjectionService>()

    override _.Initialize() =
      base.Initialize()

      let mapStore = game.Services.GetService<MapStore>()
      let map = mapStore.find mapKey

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

      // Create TerrainRenderService for each group
      terrainServices <-
        layerGroups
        |> HashMap.toArray
        |> Array.sortBy fst
        |> Array.map(fun (_, layers) ->
          let layerNames = layers |> IndexList.map(fun l -> l.Name)

          TerrainRenderSystem.create(game, mapKey, ValueSome layerNames))
        |> List.ofArray

      // Create RenderService
      let cameraService = game.Services.GetService<Core.CameraService>()

      let renderService =
        Render.create(
          game,
          world,
          targetingService,
          projections,
          cameraService,
          playerId
        )

      renderServices <- [ renderService ]

    override _.Draw(gameTime) =
      let graphicsDevice = game.GraphicsDevice
      let originalViewport = graphicsDevice.Viewport
      let cameraService = game.Services.GetService<Core.CameraService>()

      // Iterate through cameras and render
      for (playerId, camera) in cameraService.GetAllCameras() do
        // Render Terrain (Background)
        for terrainService in terrainServices do
          terrainService.Draw(camera)

        // Render Entities
        for renderService in renderServices do
          renderService.Draw(camera)

      // Restore viewport
      graphicsDevice.Viewport <- originalViewport

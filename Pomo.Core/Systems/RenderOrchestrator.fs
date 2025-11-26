namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Map
open Pomo.Core.Stores
open Pomo.Core.Domain
open Pomo.Core.Domain.Camera

module RenderOrchestratorSystem =
  open Pomo.Core.Projections
  open Pomo.Core.Environment

  let private findRenderGroup(layer: Map.MapLayer) : int =
    match layer.Properties |> HashMap.tryFindV "RenderGroup" with
    | ValueSome value ->
      match Int32.TryParse(value) with
      | true, intValue -> intValue
      | false, _ -> 0
    | ValueNone -> 0

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type RenderOrchestratorSystem
    (game: Game, env: PomoEnvironment, mapKey: string, playerId: Guid<EntityId>)
    =
    inherit DrawableGameComponent(game)

    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let (MonoGame monoGame) = env.MonoGameServices

    let mutable renderServices: Render.RenderService list = []
    let mutable terrainServices: TerrainRenderSystem.TerrainRenderService list = []

    let mutable foregroundTerrainServices
      : TerrainRenderSystem.TerrainRenderService list = []

    let world = core.World

    let targetingService = gameplay.TargetingService

    let projections = gameplay.Projections

    override _.Initialize() =
      base.Initialize()

      let mapStore = stores.MapStore
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

      // Create TerrainRenderService for each group, split into background/foreground
      let backgroundServices = ResizeArray()
      let foregroundServices = ResizeArray()

      layerGroups
      |> HashMap.toArray
      |> Array.sortBy fst
      |> Array.iter(fun (group, layers) ->
        let layerNames = layers |> IndexList.map(fun l -> l.Name)

        let service =
          TerrainRenderSystem.create(game, env, mapKey, ValueSome layerNames)

        if group < 2 then
          backgroundServices.Add(service)
        else
          foregroundServices.Add(service))

      terrainServices <- backgroundServices |> List.ofSeq
      foregroundTerrainServices <- foregroundServices |> List.ofSeq

      // Create RenderService
      let cameraService = gameplay.CameraService

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
      let cameraService = gameplay.CameraService

      // Iterate through cameras and render
      for (playerId, camera) in cameraService.GetAllCameras() do
        // Render Background Terrain (RenderGroup < 2)
        for terrainService in terrainServices do
          terrainService.Draw(camera)

        // Render Entities
        for renderService in renderServices do
          renderService.Draw(camera)

        // Render Foreground Terrain (RenderGroup >= 2) - Decorations on top
        for terrainService in foregroundTerrainServices do
          terrainService.Draw(camera)

      // Restore viewport
      graphicsDevice.Viewport <- originalViewport

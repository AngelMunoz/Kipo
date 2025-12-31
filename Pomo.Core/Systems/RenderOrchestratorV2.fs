namespace Pomo.Core.Systems

open System
open System.Collections.Generic
open System.Collections.Concurrent
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Environment
open Pomo.Core.Environment.Patterns
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Particles
open Pomo.Core.Graphics
open Pomo.Core.Rendering
open Pomo.Core.AssetPreloader
open Pomo.Core.Domain.Core

module RenderOrchestrator =

  type RenderResources = {
    GraphicsDevice: GraphicsDevice
    Content: ContentManager
    BillboardBatch: BillboardBatch
    QuadBatch: QuadBatch
    SpriteBatch: SpriteBatch
    NodeTransformsPool: Dictionary<string, Matrix>
    ModelCache: ConcurrentDictionary<string, Lazy<LoadedModel>>
    TextureCache: ConcurrentDictionary<string, Lazy<Texture2D>>
    TileTextureCache: Dictionary<int, Texture2D>
    GetLoadedModel: string -> LoadedModel voption
    GetTexture: string -> Texture2D voption
    GetTileTexture: int -> Texture2D voption
    GetBlockModel: string -> LoadedModel voption
    MapSource: MapSource
    LayerRenderIndices: IReadOnlyDictionary<int, int[]>
    FallbackTexture: Texture2D
    HudFont: SpriteFont
    HudPalette: UI.HUDColorPalette
    LoadQueue: ConcurrentQueue<(unit -> unit)>
    PendingModels: ConcurrentDictionary<string, byte>
    PendingTextures: ConcurrentDictionary<string, byte>
  }

  module RenderPasses =
    let inline private setBlendState
      (device: inref<GraphicsDevice>)
      (blend: BlendMode)
      =
      match blend with
      | BlendMode.AlphaBlend -> device.BlendState <- BlendState.AlphaBlend
      | BlendMode.Additive -> device.BlendState <- BlendState.Additive

    let inline renderMeshes
      (device: GraphicsDevice)
      (view: inref<Matrix>)
      (projection: inref<Matrix>)
      (commands: MeshCommand[])
      =
      device.DepthStencilState <- DepthStencilState.Default
      device.BlendState <- BlendState.Opaque
      device.RasterizerState <- RasterizerState.CullNone

      for cmd in commands do
        let loadedModel = cmd.LoadedModel
        let model = loadedModel.Model
        let world = cmd.WorldMatrix

        for mesh in model.Meshes do
          for effect in mesh.Effects do
            match effect with
            | :? BasicEffect as be ->
              be.World <- world
              be.View <- view
              be.Projection <- projection

              if loadedModel.HasNormals then
                LightEmitter.applyDefaultLighting &be
              else
                be.LightingEnabled <- false
                be.TextureEnabled <- true
            | _ -> ()

          mesh.Draw()

    let inline renderBillboards
      (batch: BillboardBatch)
      (device: GraphicsDevice)
      (view: inref<Matrix>)
      (projection: inref<Matrix>)
      (commands: BillboardCommand[])
      =
      if commands.Length > 0 then
        device.DepthStencilState <- DepthStencilState.None
        device.RasterizerState <- RasterizerState.CullNone

        let struct (right, up) = RenderMath.Billboard.getVectors &view

        let grouped =
          commands |> Array.groupBy(fun c -> struct (c.Texture, c.BlendMode))

        for struct (tex, blend), cmds in grouped do
          setBlendState &device blend
          batch.Begin(&view, &projection, tex)

          for cmd in cmds do
            batch.Draw(cmd.Position, cmd.Size, 0.0f, cmd.Color, right, up)

          batch.End()

    let inline renderTerrainBackground
      (batch: SpriteBatch)
      (camera: inref<Camera.Camera>)
      (map: Map.MapDefinition)
      (commands: TerrainCommand[])
      =
      // 2D SpriteBatch rendering for background - no depth buffer, no fighting
      let transform =
        RenderMath.Camera.get2DViewMatrix
          camera.Position
          camera.Zoom
          camera.Viewport

      batch.Begin(
        SpriteSortMode.Deferred,
        BlendState.AlphaBlend,
        SamplerState.PointClamp,
        DepthStencilState.None,
        RasterizerState.CullNone,
        transformMatrix = transform
      )

      for cmd in commands do
        // Convert 3D position back to pixel position for SpriteBatch
        let drawX = cmd.Position.X * float32 map.TileWidth
        let drawY = cmd.Position.Z * float32 map.TileHeight

        let destRect =
          Rectangle(
            int drawX,
            int drawY,
            int(cmd.Size.X * float32 map.TileWidth),
            int(cmd.Size.Y * float32 map.TileHeight)
          )

        batch.Draw(cmd.Texture, destRect, Color.White)

      batch.End()

    let inline renderTerrainForeground
      (batch: QuadBatch)
      (view: inref<Matrix>)
      (projection: inref<Matrix>)
      (commands: TerrainCommand[])
      =
      batch.Begin(&view, &projection)

      for cmd in commands do
        batch.Draw(cmd.Texture, cmd.Position, cmd.Size)

      batch.End()

    let inline renderText
      (batch: SpriteBatch)
      (font: SpriteFont)
      (camera: inref<Camera.Camera>)
      (commands: TextCommand[])
      =
      if commands.Length > 0 then
        let transform =
          RenderMath.Camera.get2DViewMatrix
            camera.Position
            camera.Zoom
            camera.Viewport

        batch.Begin(
          SpriteSortMode.Deferred,
          BlendState.AlphaBlend,
          SamplerState.PointClamp,
          DepthStencilState.None,
          RasterizerState.CullNone,
          transformMatrix = transform
        )

        for cmd in commands do
          let color = cmd.Color * cmd.Alpha
          let origin = font.MeasureString(cmd.Text) / 2.0f

          batch.DrawString(
            font,
            cmd.Text,
            cmd.ScreenPosition,
            color,
            0.0f,
            origin,
            cmd.Scale,
            SpriteEffects.None,
            0.0f
          )

        batch.End()

  let inline private prepareContexts
    (res: RenderResources)
    (stores: StoreServices)
    (mapSource: MapSource)
    (snapshot: Pomo.Core.Projections.MovementSnapshot)
    (poses:
      System.Collections.Generic.IReadOnlyDictionary<
        Guid<EntityId>,
        System.Collections.Generic.Dictionary<string, Matrix>
       >)
    (projectiles:
      HashMap<Guid<EntityId>, Pomo.Core.Domain.Projectile.LiveProjectile>)
    (layerRenderIndices:
      System.Collections.Generic.IReadOnlyDictionary<int, int[]>)
    =
    let ppu = MapSource.getPixelsPerUnit mapSource
    let renderCore = RenderCore.createFromMapSource mapSource
    let squish = RenderMath.WorldMatrix.getSquishFactor ppu

    let entityData = {
      ModelStore = stores.ModelStore
      GetLoadedModelByAsset = res.GetLoadedModel
      EntityPoses = poses
      LiveProjectiles = projectiles
      SquishFactor = squish
      ModelScale = Core.Constants.Entity.ModelScale
    }

    let particleData = {
      GetTexture = res.GetTexture
      GetLoadedModelByAsset = res.GetLoadedModel
      EntityPositions = snapshot.Positions
      SquishFactor = squish
      ModelScale = Core.Constants.Entity.ModelScale
      FallbackTexture = res.FallbackTexture
    }

    let terrainData = {
      GetTileTexture = res.GetTileTexture
      LayerRenderIndices = layerRenderIndices
    }

    struct (renderCore, entityData, particleData, terrainData)

  let private renderFrame
    (res: RenderResources)
    (env: PomoEnvironment)
    (playerId: Guid<EntityId>)
    =
    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let world = core.World
    let projections = gameplay.Projections
    let visualEffects = world.VisualEffects.ToArray()

    let scenarioIdOpt =
      world.EntityScenario |> AMap.force |> HashMap.tryFind playerId

    match scenarioIdOpt with
    | Some scenarioId ->
      let snapshot = projections.ComputeMovementSnapshot scenarioId

      let struct (renderCore, entityData, particleData, terrainData) =
        prepareContexts
          res
          stores
          res.MapSource
          snapshot
          world.Poses
          (world.LiveProjectiles |> AMap.force)
          res.LayerRenderIndices

      let meshCommandsEntities =
        EntityEmitter.emitAll
          renderCore
          entityData
          snapshot
          res.NodeTransformsPool

      let billboardCommandsParticles =
        ParticleEmitter.emitBillboards renderCore particleData visualEffects

      let meshCommandsParticles =
        ParticleEmitter.emitMeshes renderCore particleData visualEffects

      let cameras = gameplay.CameraService.GetAllCameras()

      for struct (_, camera) in cameras do
        res.GraphicsDevice.Viewport <- camera.Viewport
        res.GraphicsDevice.Clear Color.Black

        let view = camera.View
        let projection = camera.Projection

        let viewBounds =
          RenderMath.Camera.getViewBounds
            camera.Position
            (float32 camera.Viewport.Width)
            (float32 camera.Viewport.Height)
            camera.Zoom

        // Collect terrain commands conditionally based on MapSource
        let struct (terrainBG, terrainFG) =
          match res.MapSource |> MapSource.tryGetTileMap with
          | ValueSome map ->
            TerrainEmitter.emitAll renderCore terrainData map viewBounds
          | ValueNone -> struct (Array.empty, Array.empty)

        // Collect block mesh commands conditionally based on MapSource
        let meshCommandsBlocks =
          match res.MapSource |> MapSource.tryGetBlockMap with
          | ValueSome blockMap ->
            let ppu = renderCore.PixelsPerUnit
            let visibleHeightRange = float32 blockMap.Height * BlockMap.CellSize

            BlockEmitter.emit
              res.GetBlockModel
              blockMap
              viewBounds
              camera.Position.Y
              visibleHeightRange
              ppu
          | ValueNone -> Array.empty

        // Render passes in correct order
        // 1. Background terrain (no depth to avoid tile fighting)
        res.GraphicsDevice.DepthStencilState <- DepthStencilState.None

        res.MapSource
        |> MapSource.tryGetTileMap
        |> ValueOption.iter(fun map ->
          RenderPasses.renderTerrainBackground
            res.SpriteBatch
            &camera
            map
            terrainBG)

        // Clear depth buffer after background terrain
        // This ensures entities always render on top of background
        res.GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Black, 1.0f, 0)

        // 2. Blocks first (they form the ground/structures)
        res.GraphicsDevice.DepthStencilState <- DepthStencilState.Default

        RenderPasses.renderMeshes
          res.GraphicsDevice
          &view
          &projection
          meshCommandsBlocks

        // 3. Entities and mesh particles (with depth testing)
        res.GraphicsDevice.DepthStencilState <- DepthStencilState.Default
        // Render entity meshes and particle meshes separately to avoid Array.append allocation
        RenderPasses.renderMeshes
          res.GraphicsDevice
          &view
          &projection
          meshCommandsEntities

        RenderPasses.renderMeshes
          res.GraphicsDevice
          &view
          &projection
          meshCommandsParticles

        // 3. Billboard particles
        RenderPasses.renderBillboards
          res.BillboardBatch
          res.GraphicsDevice
          &view
          &projection
          billboardCommandsParticles

        // 4. Foreground terrain/decorations (with depth testing for occlusion)
        res.GraphicsDevice.DepthStencilState <- DepthStencilState.Default

        RenderPasses.renderTerrainForeground
          res.QuadBatch
          &view
          &projection
          terrainFG

        // 5. World text (notifications, damage numbers - rendered in 2D)
        let textCommands = TextEmitter.emit world.Notifications viewBounds

        RenderPasses.renderText
          res.SpriteBatch
          res.HudFont
          &camera
          (textCommands res.HudPalette)

    | None ->
      // Player died or removed - render nothing (black screen)
      // UI (higher DrawOrder) will still render on top showing main menu button
      ()

  let create
    (
      game: Game,
      env: PomoEnvironment,
      mapSource: MapSource,
      playerId: Guid<EntityId>,
      drawOrder: int
    ) : DrawableGameComponent =

    let mutable res: RenderResources voption = ValueNone
    let (Stores stores) = env.StoreServices

    { new DrawableGameComponent(game, DrawOrder = drawOrder) with
        override _.Initialize() =
          base.Initialize()
          let fallback = new Texture2D(game.GraphicsDevice, 1, 1)
          fallback.SetData [| Color.White |]

          // Load assets via domain emitters (each owns their loading logic)
          let entityModelCache =
            EntityEmitter.loadModels game.Content stores.ModelStore

          let struct (particleTextureCache, particleModelCache) =
            ParticleEmitter.loadAssets game.Content stores.ParticleStore

          // Merge entity and particle model caches
          let modelCache = ConcurrentDictionary<string, Lazy<LoadedModel>>()

          for kvp in entityModelCache do
            modelCache[kvp.Key] <- Lazy<LoadedModel>(fun () -> kvp.Value)

          for kvp in particleModelCache do
            modelCache[kvp.Key] <- Lazy<LoadedModel>(fun () -> kvp.Value)

          let textureCache = ConcurrentDictionary<string, Lazy<Texture2D>>()

          for kvp in particleTextureCache do
            textureCache[kvp.Key] <- Lazy<Texture2D>(fun () -> kvp.Value)

          // Conditional tile loading based on MapSource
          let tileCache, layerIndices =
            match mapSource |> MapSource.tryGetTileMap with
            | ValueSome map ->
              TerrainEmitter.loadTileTextures game.Content map,
              TerrainEmitter.computeLayerRenderIndices map
            | ValueNone ->
              Dictionary(), Dictionary() :> IReadOnlyDictionary<_, _>

          // Asset preloading (use mapKey from TileMap if present)
          mapSource
          |> MapSource.tryGetTileMap
          |> ValueOption.iter(fun map ->
            AssetPreloader.preloadAssets
              game.Content
              modelCache
              textureCache
              map.Key
              map
              stores.ModelStore
              stores.ParticleStore
            |> ignore)


          // Main-thread load queue and pending sets
          let loadQueue = ConcurrentQueue<(unit -> unit)>()
          let pendingModels = ConcurrentDictionary<string, byte>()
          let pendingTextures = ConcurrentDictionary<string, byte>()

          let enqueueUnique
            (pending: ConcurrentDictionary<string, byte>)
            (queue: ConcurrentQueue<unit -> unit>)
            (key: string)
            (work: unit -> unit)
            =
            if pending.TryAdd(key, 0uy) then
              queue.Enqueue(fun () ->
                try
                  work()
                with _ ->
                  ()

                pending.TryRemove(key) |> ignore)

          let getLoadedModel asset =
            match modelCache.TryGetValue asset with
            | true, lazyModel -> ValueSome(lazyModel.Value)
            | false, _ ->
              enqueueUnique pendingModels loadQueue asset (fun () ->
                let lazyModel =
                  modelCache.GetOrAdd(
                    asset,
                    fun key ->
                      Lazy<LoadedModel>(fun () ->
                        let model = game.Content.Load<Model>(key)
                        let loaded = LoadedModel.fromModel model

                        if not loaded.HasNormals then
                          printfn
                            $"[RenderOrchestrator] Model '{key}' missing normals, lighting will be disabled"

                        loaded)
                  )

                lazyModel.Force() |> ignore)

              ValueNone

          let getTexture asset =
            match textureCache.TryGetValue asset with
            | true, lazyTexture -> ValueSome(lazyTexture.Value)
            | false, _ ->
              enqueueUnique pendingTextures loadQueue asset (fun () ->
                let lazyTexture =
                  textureCache.GetOrAdd(
                    asset,
                    fun key ->
                      Lazy<Texture2D>(fun () ->
                        game.Content.Load<Texture2D>(key))
                  )

                lazyTexture.Force() |> ignore)

              ValueNone

          // Create block model loader using BlockEmitter's lazy loader
          let getBlockModel = BlockEmitter.createLazyModelLoader game.Content

          res <-
            ValueSome {
              GraphicsDevice = game.GraphicsDevice
              Content = game.Content
              BillboardBatch = new BillboardBatch(game.GraphicsDevice)
              QuadBatch = new QuadBatch(game.GraphicsDevice)
              SpriteBatch = new SpriteBatch(game.GraphicsDevice)
              NodeTransformsPool = Dictionary()
              ModelCache = modelCache
              TextureCache = textureCache
              TileTextureCache = tileCache
              GetLoadedModel = getLoadedModel
              GetTexture = getTexture
              GetTileTexture = fun gid -> tileCache |> Dictionary.tryFindV gid
              GetBlockModel = getBlockModel
              MapSource = mapSource
              LayerRenderIndices = layerIndices
              FallbackTexture = fallback
              HudFont = TextEmitter.loadFont game.Content
              HudPalette =
                env.CoreServices.HUDService.Config
                |> AVal.map _.Theme.Colors
                |> AVal.force
              LoadQueue = loadQueue
              PendingModels = pendingModels
              PendingTextures = pendingTextures
            }

        override _.Update _ =
          // Drain the load queue on the main thread to keep Content.Load thread-safe
          res
          |> ValueOption.iter(fun r ->
            let mutable work = Unchecked.defaultof<unit -> unit>

            while r.LoadQueue.TryDequeue(&work) do
              work())

        override _.Draw _ =
          res
          |> ValueOption.iter(fun r ->
            let originalViewport = r.GraphicsDevice.Viewport
            renderFrame r env playerId
            r.GraphicsDevice.Viewport <- originalViewport)
    }

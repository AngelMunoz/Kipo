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
open Pomo.Core.Simulation

module RenderOrchestratorV2 =

  type RenderResources = {
    GraphicsDevice: GraphicsDevice
    Content: ContentManager
    BillboardBatch: BillboardBatch
    QuadBatch: QuadBatch
    NodeTransformsPool: Dictionary<string, Matrix>
    ModelCache: IReadOnlyDictionary<string, Model>
    TextureCache: IReadOnlyDictionary<string, Texture2D>
    TileTextureCache: Dictionary<int, Texture2D>
    FallbackTexture: Texture2D
  }

  let private setBlendState (device: GraphicsDevice) (blend: BlendMode) =
    match blend with
    | BlendMode.AlphaBlend -> device.BlendState <- BlendState.AlphaBlend
    | BlendMode.Additive -> device.BlendState <- BlendState.Additive

  /// Pre-loads all entity models from ModelStore at initialization
  let private preloadEntityModels
    (content: ContentManager)
    (modelStore: Pomo.Core.Stores.ModelStore)
    (cache: Dictionary<string, Model>)
    =
    for config in modelStore.all() do
      for _, node in config.Rig do
        if not(cache.ContainsKey node.ModelAsset) then

          let loaded =
            try
              ValueSome(content.Load<Model>(node.ModelAsset))
            with _ ->
              ValueNone

          loaded |> ValueOption.iter(fun m -> cache[node.ModelAsset] <- m)

  /// Pre-loads all particle textures and mesh models from ParticleStore
  let private preloadParticleAssets
    (content: ContentManager)
    (particleStore: Pomo.Core.Stores.ParticleStore)
    (textureCache: Dictionary<string, Texture2D>)
    (modelCache: Dictionary<string, Model>)
    =
    for _, emitters in particleStore.all() do
      for emitter in emitters do
        match emitter.RenderMode with
        | Particles.Billboard texturePath ->
          if not(textureCache.ContainsKey texturePath) then
            let loaded =
              try
                ValueSome(content.Load<Texture2D>(texturePath))
              with _ ->
                ValueNone

            loaded |> ValueOption.iter(fun t -> textureCache[texturePath] <- t)
        | Particles.Mesh modelPath ->
          if not(modelCache.ContainsKey modelPath) then
            let loaded =
              try
                ValueSome(content.Load<Model>(modelPath))
              with _ ->
                ValueNone

            loaded |> ValueOption.iter(fun m -> modelCache[modelPath] <- m)

  /// Pure lookup - no loading (all assets pre-loaded at init)
  let private getModel
    (cache: IReadOnlyDictionary<string, Model>)
    (asset: string)
    =
    cache |> Dictionary.tryFindV asset

  /// Pure lookup - no loading (all assets pre-loaded at init)
  let private getTexture
    (cache: IReadOnlyDictionary<string, Texture2D>)
    (asset: string)
    =
    cache |> Dictionary.tryFindV asset

  let inline private getTileTexture
    (cache: Dictionary<int, Texture2D>)
    (gid: int)
    : Texture2D voption =
    cache |> Dictionary.tryFindV gid

  let private prepareContexts
    (res: RenderResources)
    (stores: StoreServices)
    (map: Pomo.Core.Domain.Map.MapDefinition)
    (snapshot: Pomo.Core.Projections.MovementSnapshot)
    (poses: HashMap<Guid<EntityId>, HashMap<string, Matrix>>)
    (projectiles:
      HashMap<Guid<EntityId>, Pomo.Core.Domain.Projectile.LiveProjectile>)
    =
    let ppu = Vector2(float32 map.TileWidth, float32 map.TileHeight)
    let renderCore = RenderCore.create ppu
    let squish = RenderMath.GetSquishFactor ppu

    let entityData = {
      ModelStore = stores.ModelStore
      GetModelByAsset = getModel res.ModelCache
      EntityPoses = poses
      LiveProjectiles = projectiles
      SquishFactor = squish
      ModelScale = Core.Constants.Entity.ModelScale
    }

    let particleData = {
      GetTexture = getTexture res.TextureCache
      GetModelByAsset = getModel res.ModelCache
      EntityPositions = snapshot.Positions
      SquishFactor = squish
      ModelScale = Core.Constants.Entity.ModelScale
      FallbackTexture = res.FallbackTexture
    }

    let terrainData = {
      GetTileTexture = getTileTexture res.TileTextureCache
    }

    struct (renderCore, entityData, particleData, terrainData)

  let private renderMeshes
    (device: GraphicsDevice)
    (view: Matrix)
    (projection: Matrix)
    (commands: MeshCommand[])
    =
    device.DepthStencilState <- DepthStencilState.Default
    device.BlendState <- BlendState.Opaque
    device.RasterizerState <- RasterizerState.CullNone

    for cmd in commands do
      let model = cmd.Model
      let world = cmd.WorldMatrix

      for mesh in model.Meshes do
        for effect in mesh.Effects do
          match effect with
          | :? BasicEffect as be ->
            be.World <- world
            be.View <- view
            be.Projection <- projection
            LightEmitter.applyDefaultLighting &be
          | _ -> ()

        mesh.Draw()

  let private renderBillboards
    (batch: BillboardBatch)
    (device: GraphicsDevice)
    (view: Matrix)
    (projection: Matrix)
    (commands: BillboardCommand[])
    =
    if commands.Length > 0 then
      device.DepthStencilState <- DepthStencilState.None
      device.RasterizerState <- RasterizerState.CullNone

      let struct (right, up) = RenderMath.GetBillboardVectors view

      let grouped =
        commands |> Array.groupBy(fun c -> struct (c.Texture, c.BlendMode))

      for struct (tex, blend), cmds in grouped do
        setBlendState device blend
        batch.Begin(view, projection, tex)

        for cmd in cmds do
          batch.Draw(cmd.Position, cmd.Size, 0.0f, cmd.Color, right, up)

        batch.End()

  let private renderTerrainBackground
    (batch: QuadBatch)
    (view: Matrix)
    (projection: Matrix)
    (commands: TerrainCommand[])
    =
    batch.Begin(view, projection)

    for cmd in commands do
      batch.Draw(cmd.Texture, cmd.Position, cmd.Size)

    batch.End()

  let private renderTerrainForeground
    (batch: QuadBatch)
    (view: Matrix)
    (projection: Matrix)
    (commands: TerrainCommand[])
    =
    batch.Begin(view, projection)

    for cmd in commands do
      batch.Draw(cmd.Texture, cmd.Position, cmd.Size)

    batch.End()

  let private renderFrame
    (res: RenderResources)
    (env: PomoEnvironment)
    (mapKey: string)
    (playerId: Guid<EntityId>)
    =
    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let world = core.World
    let projections = gameplay.Projections
    let map = stores.MapStore.find mapKey
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
          map
          snapshot
          (world.Poses |> AMap.force)
          (world.LiveProjectiles |> AMap.force)

      let resolvedEntities =
        PoseResolver.resolveAll
          renderCore
          entityData
          snapshot
          res.NodeTransformsPool

      let meshCommandsEntities =
        EntityEmitter.emit entityData.GetModelByAsset resolvedEntities

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
          RenderMath.GetViewBounds
            camera.Position
            (float32 camera.Viewport.Width)
            (float32 camera.Viewport.Height)
            camera.Zoom

        // Collect terrain commands via TerrainEmitter
        let struct (terrainBG, terrainFG) =
          TerrainEmitter.emitAll renderCore terrainData map viewBounds

        // Render passes in correct order
        // 1. Background terrain (no depth to avoid tile fighting)
        res.GraphicsDevice.DepthStencilState <- DepthStencilState.None
        renderTerrainBackground res.QuadBatch view projection terrainBG

        // Clear depth buffer after background terrain
        // This ensures entities always render on top of background
        res.GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Black, 1.0f, 0)

        // 2. Entities and mesh particles (with depth testing)
        res.GraphicsDevice.DepthStencilState <- DepthStencilState.Default
        let allMeshes = Array.append meshCommandsEntities meshCommandsParticles
        renderMeshes res.GraphicsDevice view projection allMeshes

        // 3. Billboard particles
        renderBillboards
          res.BillboardBatch
          res.GraphicsDevice
          view
          projection
          billboardCommandsParticles

        // 4. Foreground terrain/decorations (with depth testing for occlusion)
        res.GraphicsDevice.DepthStencilState <- DepthStencilState.Default
        renderTerrainForeground res.QuadBatch view projection terrainFG

    | None ->
      // Player died or removed - render nothing (black screen)
      // UI (higher DrawOrder) will still render on top showing main menu button
      ()

  let create
    (
      game: Game,
      env: PomoEnvironment,
      mapKey: string,
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
          let map = stores.MapStore.find mapKey

          // Create mutable caches for pre-loading
          let modelCache = Dictionary<string, Model>()
          let textureCache = Dictionary<string, Texture2D>()

          // Pre-load all assets at initialization (sequential, thread-safe)
          preloadEntityModels game.Content stores.ModelStore modelCache

          preloadParticleAssets
            game.Content
            stores.ParticleStore
            textureCache
            modelCache

          // Use TerrainEmitter to load tile textures
          let tileCache = TerrainEmitter.loadTileTextures game.Content map

          res <-
            ValueSome {
              GraphicsDevice = game.GraphicsDevice
              Content = game.Content
              BillboardBatch = new BillboardBatch(game.GraphicsDevice)
              QuadBatch = new QuadBatch(game.GraphicsDevice)
              NodeTransformsPool = Dictionary()
              ModelCache = modelCache
              TextureCache = textureCache
              TileTextureCache = tileCache
              FallbackTexture = fallback
            }

        override _.Draw _ =
          res
          |> ValueOption.iter(fun r ->
            let originalViewport = r.GraphicsDevice.Viewport
            renderFrame r env mapKey playerId
            r.GraphicsDevice.Viewport <- originalViewport)
    }

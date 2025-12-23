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

module RenderOrchestratorV2 =

  type RenderResources = {
    GraphicsDevice: GraphicsDevice
    Content: ContentManager
    BillboardBatch: BillboardBatch
    QuadBatch: QuadBatch
    SpriteBatch: SpriteBatch
    NodeTransformsPool: Dictionary<string, Matrix>
    ModelCache: IReadOnlyDictionary<string, Model>
    TextureCache: IReadOnlyDictionary<string, Texture2D>
    TileTextureCache: Dictionary<int, Texture2D>
    LayerRenderIndices: IReadOnlyDictionary<int, int[]>
    FallbackTexture: Texture2D
    HudFont: SpriteFont
  }

  let private setBlendState (device: GraphicsDevice) (blend: BlendMode) =
    match blend with
    | BlendMode.AlphaBlend -> device.BlendState <- BlendState.AlphaBlend
    | BlendMode.Additive -> device.BlendState <- BlendState.Additive

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
    (layerRenderIndices:
      System.Collections.Generic.IReadOnlyDictionary<int, int[]>)
    =
    let ppu = Vector2(float32 map.TileWidth, float32 map.TileHeight)
    let renderCore = RenderCore.create ppu
    let squish = RenderMath.WorldMatrix.getSquishFactor ppu

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
      LayerRenderIndices = layerRenderIndices
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

      let struct (right, up) = RenderMath.Billboard.getVectors view

      let grouped =
        commands |> Array.groupBy(fun c -> struct (c.Texture, c.BlendMode))

      for struct (tex, blend), cmds in grouped do
        setBlendState device blend
        batch.Begin(view, projection, tex)

        for cmd in cmds do
          batch.Draw(cmd.Position, cmd.Size, 0.0f, cmd.Color, right, up)

        batch.End()

  let private renderTerrainBackground
    (batch: SpriteBatch)
    (camera: Camera.Camera)
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

  let private renderText
    (batch: SpriteBatch)
    (font: SpriteFont)
    (camera: Camera.Camera)
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
        let color = Color.White * cmd.Alpha
        let textSize = font.MeasureString(cmd.Text)
        let textPosition = cmd.ScreenPosition - textSize / 2.0f
        batch.DrawString(font, cmd.Text, textPosition, color)

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

        // Collect terrain commands via TerrainEmitter
        let struct (terrainBG, terrainFG) =
          TerrainEmitter.emitAll renderCore terrainData map viewBounds

        // Render passes in correct order
        // 1. Background terrain (no depth to avoid tile fighting)
        res.GraphicsDevice.DepthStencilState <- DepthStencilState.None
        renderTerrainBackground res.SpriteBatch camera map terrainBG

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

        // 5. World text (notifications, damage numbers - rendered in 2D)
        let textCommands = TextEmitter.emit world.Notifications viewBounds

        renderText res.SpriteBatch res.HudFont camera textCommands

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

          // Load assets via domain emitters (each owns their loading logic)
          let entityModelCache =
            EntityEmitter.loadModels game.Content stores.ModelStore

          let struct (particleTextureCache, particleModelCache) =
            ParticleEmitter.loadAssets game.Content stores.ParticleStore

          // Merge entity and particle model caches
          let modelCache = Dictionary<string, Model>()

          for kvp in entityModelCache do
            modelCache[kvp.Key] <- kvp.Value

          for kvp in particleModelCache do
            modelCache[kvp.Key] <- kvp.Value

          let textureCache = particleTextureCache

          // Use TerrainEmitter to load tile textures and pre-compute render indices
          let tileCache = TerrainEmitter.loadTileTextures game.Content map
          let layerIndices = TerrainEmitter.computeLayerRenderIndices map

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
              LayerRenderIndices = layerIndices
              FallbackTexture = fallback
              HudFont = TextEmitter.loadFont game.Content
            }

        override _.Draw _ =
          res
          |> ValueOption.iter(fun r ->
            let originalViewport = r.GraphicsDevice.Viewport
            renderFrame r env mapKey playerId
            r.GraphicsDevice.Viewport <- originalViewport)
    }

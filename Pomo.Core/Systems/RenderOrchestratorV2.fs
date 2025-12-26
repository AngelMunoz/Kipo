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

module RenderOrchestrator =

  type RenderResources = {
    GraphicsDevice: GraphicsDevice
    Content: ContentManager
    BillboardBatch: BillboardBatch
    QuadBatch: QuadBatch
    SpriteBatch: SpriteBatch
    NodeTransformsPool: Dictionary<string, Matrix>
    ModelCache: Dictionary<string, Model>
    TextureCache: Dictionary<string, Texture2D>
    TileTextureCache: Dictionary<int, Texture2D>
    GetModel: string -> Model voption
    GetTexture: string -> Texture2D voption
    GetTileTexture: int -> Texture2D voption
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

        let inline blendOrd(b: BlendMode) =
          match b with
          | BlendMode.Additive -> 0
          | BlendMode.AlphaBlend -> 1

        commands
        |> Array.sortInPlaceBy(fun c ->
          struct (c.Texture.GetHashCode(), blendOrd c.BlendMode))

        let mutable i = 0

        while i < commands.Length do
          let first = commands.[i]
          let tex = first.Texture
          let blend = first.BlendMode

          setBlendState &device blend
          batch.Begin(&view, &projection, tex)
          batch.Draw(first.Position, first.Size, 0.0f, first.Color, right, up)
          i <- i + 1

          // Continue drawing while same texture and blend
          while i < commands.Length
                && Object.ReferenceEquals(commands.[i].Texture, tex)
                && commands.[i].BlendMode = blend do
            let cmd = commands.[i]
            batch.Draw(cmd.Position, cmd.Size, 0.0f, cmd.Color, right, up)
            i <- i + 1

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
    (map: Pomo.Core.Domain.Map.MapDefinition)
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
    let ppu = Vector2(float32 map.TileWidth, float32 map.TileHeight)
    let renderCore = RenderCore.create ppu
    let squish = RenderMath.WorldMatrix.getSquishFactor ppu

    let entityData = {
      ModelStore = stores.ModelStore
      GetModelByAsset = res.GetModel
      EntityPoses = poses
      LiveProjectiles = projectiles
      SquishFactor = squish
      ModelScale = Core.Constants.Entity.ModelScale
    }

    let particleData = {
      GetTexture = res.GetTexture
      GetModelByAsset = res.GetModel
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

        // Collect terrain commands via TerrainEmitter
        let struct (terrainBG, terrainFG) =
          TerrainEmitter.emitAll renderCore terrainData map viewBounds

        // Render passes in correct order
        // 1. Background terrain (no depth to avoid tile fighting)
        res.GraphicsDevice.DepthStencilState <- DepthStencilState.None

        RenderPasses.renderTerrainBackground
          res.SpriteBatch
          &camera
          map
          terrainBG

        // Clear depth buffer after background terrain
        // This ensures entities always render on top of background
        res.GraphicsDevice.Clear(ClearOptions.DepthBuffer, Color.Black, 1.0f, 0)

        // 2. Entities and mesh particles (with depth testing)
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

          let textureCache = Dictionary<string, Texture2D>()

          for kvp in particleTextureCache do
            textureCache[kvp.Key] <- kvp.Value

          // Use TerrainEmitter to load tile textures and pre-compute render indices
          let tileCache = TerrainEmitter.loadTileTextures game.Content map
          let layerIndices = TerrainEmitter.computeLayerRenderIndices map

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

          let getModel asset =
            match modelCache |> Dictionary.tryFindV asset with
            | ValueSome m -> ValueSome m
            | ValueNone ->
              enqueueUnique pendingModels loadQueue asset (fun () ->
                if not(modelCache.ContainsKey asset) then
                  let m = game.Content.Load<Model>(asset)
                  modelCache[asset] <- m)

              ValueNone

          let getTexture asset =
            match textureCache |> Dictionary.tryFindV asset with
            | ValueSome t -> ValueSome t
            | ValueNone ->
              enqueueUnique pendingTextures loadQueue asset (fun () ->
                if not(textureCache.ContainsKey asset) then
                  let t = game.Content.Load<Texture2D>(asset)
                  textureCache[asset] <- t)

              ValueNone

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
              GetModel = getModel
              GetTexture = getTexture
              GetTileTexture = fun gid -> tileCache |> Dictionary.tryFindV gid
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
            renderFrame r env mapKey playerId
            r.GraphicsDevice.Viewport <- originalViewport)
    }

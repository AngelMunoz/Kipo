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
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Core.Constants

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
    GetLoadedModel: string -> LoadedModel voption
    GetTexture: string -> Texture2D voption
    GetBlockModel: string -> LoadedModel voption
    MapSource: MapSource
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
      (count: int voption)
      =
      device.DepthStencilState <- DepthStencilState.Default
      device.BlendState <- BlendState.Opaque
      device.RasterizerState <- RasterizerState.CullNone

      let len = defaultValueArg count commands.Length

      for i = 0 to len - 1 do
        let cmd = commands.[i]
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

    let inline renderText
      (batch: SpriteBatch)
      (font: SpriteFont)
      (commands: TextCommand[])
      =
      if commands.Length > 0 then
        batch.Begin(
          SpriteSortMode.Deferred,
          BlendState.AlphaBlend,
          SamplerState.PointClamp,
          DepthStencilState.None,
          RasterizerState.CullNone
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

    /// Render line primitives (grid, cursor wireframe)
    let inline renderLines
      (device: GraphicsDevice)
      (effect: BasicEffect)
      (view: inref<Matrix>)
      (projection: inref<Matrix>)
      (vertices: VertexPositionColor[])
      (vertexCount: int)
      =
      if vertexCount > 0 then
        effect.World <- Matrix.Identity
        effect.View <- view
        effect.Projection <- projection

        for pass in effect.CurrentTechnique.Passes do
          pass.Apply()

          device.DrawUserPrimitives(
            PrimitiveType.LineList,
            vertices,
            0,
            vertexCount / 2
          )

    /// Render a ghost mesh with transparency
    let inline renderGhost
      (device: GraphicsDevice)
      (view: inref<Matrix>)
      (projection: inref<Matrix>)
      (cmd: MeshCommand)
      =
      device.DepthStencilState <- DepthStencilState.DepthRead
      device.BlendState <- BlendState.AlphaBlend
      device.RasterizerState <- RasterizerState.CullNone

      for mesh in cmd.LoadedModel.Model.Meshes do
        for eff in mesh.Effects do
          match eff with
          | :? BasicEffect as be ->
            be.World <- cmd.WorldMatrix
            be.View <- view
            be.Projection <- projection
            be.Alpha <- 0.6f

            if cmd.LoadedModel.HasNormals then
              LightEmitter.applyDefaultLighting &be
            else
              be.LightingEnabled <- false
              be.TextureEnabled <- true
          | _ -> ()

        mesh.Draw()

        // Reset alpha
        for eff in mesh.Effects do
          match eff with
          | :? BasicEffect as be -> be.Alpha <- 1.0f
          | _ -> ()

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
    =
    let ppu = MapSource.getPixelsPerUnit mapSource
    let renderCore = RenderCore.createFromMapSource mapSource

    let entityData = {
      ModelStore = stores.ModelStore
      GetLoadedModelByAsset = res.GetLoadedModel
      EntityPoses = poses
      LiveProjectiles = projectiles
      ModelScale = Core.Constants.Entity.ModelScale
    }

    let particleData = {
      GetTexture = res.GetTexture
      GetLoadedModelByAsset = res.GetLoadedModel
      EntityPositions = snapshot.Positions
      ModelScale = Core.Constants.Entity.ModelScale
      FallbackTexture = res.FallbackTexture
    }

    struct (renderCore, entityData, particleData)

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

      let struct (renderCore, entityData, particleData) =
        prepareContexts
          res
          stores
          res.MapSource
          snapshot
          world.Poses
          (world.LiveProjectiles |> AMap.force)

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

        let viewBoundsFallback =
          RenderMath.Camera.getViewBounds
            camera.Position
            (float32 camera.Viewport.Width)
            (float32 camera.Viewport.Height)
            camera.Zoom

        let viewBounds2D =
          RenderMath.Camera.tryGetViewBoundsFromMatrices
            camera.Position
            camera.Viewport
            camera.Zoom
            view
            projection
            0.0f
          |> ValueOption.defaultValue viewBoundsFallback

        // Collect block mesh commands conditionally based on MapSource
        let meshCommandsBlocks =
          match res.MapSource |> MapSource.tryGetBlockMap with
          | ValueSome blockMap ->
            let ppu = renderCore.PixelsPerUnit
            let visibleHeightRange = float32 blockMap.Height * BlockMap.CellSize

            let viewBounds3D =
              RenderMath.Camera.tryGetViewBoundsFromMatrices
                camera.Position
                camera.Viewport
                camera.Zoom
                view
                projection
                visibleHeightRange
              |> ValueOption.defaultValue viewBounds2D

            BlockEmitter.emitToArray
              res.GetBlockModel
              blockMap
              viewBounds3D
              camera.Position.Y
              visibleHeightRange
              ppu
          | ValueNone -> Array.empty

        // Render passes in correct order
        // 1. Blocks first (they form the ground/structures)
        res.GraphicsDevice.DepthStencilState <- DepthStencilState.Default

        RenderPasses.renderMeshes
          res.GraphicsDevice
          &view
          &projection
          meshCommandsBlocks
          ValueNone

        // 2. Entities and mesh particles (with depth testing)
        res.GraphicsDevice.DepthStencilState <- DepthStencilState.Default
        // Render entity meshes and particle meshes separately to avoid Array.append allocation
        RenderPasses.renderMeshes
          res.GraphicsDevice
          &view
          &projection
          meshCommandsEntities
          ValueNone

        RenderPasses.renderMeshes
          res.GraphicsDevice
          &view
          &projection
          meshCommandsParticles
          ValueNone

        // 3. Billboard particles
        RenderPasses.renderBillboards
          res.BillboardBatch
          res.GraphicsDevice
          &view
          &projection
          billboardCommandsParticles

        // 4. World text (notifications, damage numbers - rendered in 2D)
        let textCommands =
          TextEmitter.emit
            renderCore
            camera.Viewport
            view
            projection
            world.Notifications
            viewBounds2D

        RenderPasses.renderText
          res.SpriteBatch
          res.HudFont
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
              GetLoadedModel = getLoadedModel
              GetTexture = getTexture
              GetBlockModel = getBlockModel
              MapSource = mapSource
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

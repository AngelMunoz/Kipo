namespace Pomo.Core.Systems

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive

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
    ModelCache: Dictionary<string, Model voption>
    TileTextureCache: Dictionary<int, Texture2D>
    FallbackTexture: Texture2D
  }

  let private setBlendState (device: GraphicsDevice) (blend: BlendMode) =
    match blend with
    | BlendMode.AlphaBlend -> device.BlendState <- BlendState.AlphaBlend
    | BlendMode.Additive -> device.BlendState <- BlendState.Additive

  let private getModel (res: RenderResources) (asset: string) =
    match res.ModelCache.TryGetValue asset with
    | true, m -> m
    | _ ->
      let loaded =
        try
          ValueSome(res.Content.Load<Model>(asset))
        with _ ->
          ValueNone

      res.ModelCache[asset] <- loaded
      loaded

  let inline private getTileTexture
    (cache: Dictionary<int, Texture2D>)
    (gid: int)
    : Texture2D voption =
    match cache.TryGetValue gid with
    | true, tex -> ValueSome tex
    | _ -> ValueNone

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
      GetModelByAsset = getModel res
      EntityPoses = poses
      LiveProjectiles = projectiles
      SquishFactor = squish
      ModelScale = 1.0f
    }

    let particleData = {
      GetTexture =
        fun asset ->
          try
            ValueSome(res.Content.Load<Texture2D>(asset))
          with _ ->
            ValueNone
      GetModelByAsset = getModel res
      EntityPositions = snapshot.Positions
      SquishFactor = squish
      ModelScale = 1.0f
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
    // Background terrain - no depth testing needed
    batch.Begin(view, projection)

    for cmd in commands do
      batch.Draw(cmd.Texture, cmd.Position, cmd.Size)

    batch.End()

  let private renderTerrainForeground
    (batch: QuadBatch)
    (device: GraphicsDevice)
    (view: Matrix)
    (projection: Matrix)
    (commands: TerrainCommand[])
    =
    // Foreground terrain - needs depth testing for entity occlusion
    device.DepthStencilState <- DepthStencilState.Default
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
        // 1. Background terrain (no depth testing)
        renderTerrainBackground res.QuadBatch view projection terrainBG

        // 2. Entities and mesh particles (with depth testing)
        let allMeshes = Array.append meshCommandsEntities meshCommandsParticles
        renderMeshes res.GraphicsDevice view projection allMeshes

        // 3. Billboard particles (no depth testing)
        renderBillboards
          res.BillboardBatch
          res.GraphicsDevice
          view
          projection
          billboardCommandsParticles

        // 4. Foreground terrain/decorations (with depth testing for occlusion)
        renderTerrainForeground
          res.QuadBatch
          res.GraphicsDevice
          view
          projection
          terrainFG

    | None -> ()

  let create
    (game: Game)
    (env: PomoEnvironment)
    (mapKey: string)
    (playerId: Guid<EntityId>)
    : DrawableGameComponent =

    let mutable res: RenderResources voption = ValueNone
    let (Stores stores) = env.StoreServices

    { new DrawableGameComponent(game) with
        override _.Initialize() =
          base.Initialize()
          let fallback = new Texture2D(game.GraphicsDevice, 1, 1)
          fallback.SetData [| Color.White |]
          let map = stores.MapStore.find mapKey
          // Use TerrainEmitter to load tile textures
          let tileCache = TerrainEmitter.loadTileTextures game.Content map

          res <-
            ValueSome {
              GraphicsDevice = game.GraphicsDevice
              Content = game.Content
              BillboardBatch = new BillboardBatch(game.GraphicsDevice)
              QuadBatch = new QuadBatch(game.GraphicsDevice)
              NodeTransformsPool = Dictionary()
              ModelCache = Dictionary()
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

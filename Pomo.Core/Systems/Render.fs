namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Graphics

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Camera
open Pomo.Core.Systems.Targeting
open Pomo.Core.Stores
open Pomo.Core.Domain.Animation


module Render =
  open Pomo.Core.Domain.Projectile
  open Microsoft.Xna.Framework.Input
  open Pomo.Core.Environment
  open System.Collections.Generic

  // ============================================================================
  // DrawCommands (legacy 2D commands, kept for targeting indicator)
  // ============================================================================

  type DrawCommand =
    | DrawPlayer of rect: Rectangle
    | DrawEnemy of rect: Rectangle
    | DrawProjectile of rect: Rectangle
    | DrawTargetingIndicator of rect: Rectangle

  let private generateEntityCommands
    (snapshot: Projections.MovementSnapshot)
    (projectiles: HashMap<Guid<EntityId>, LiveProjectile>)
    (liveEntities: HashSet<Guid<EntityId>>)
    (playerId: Guid<EntityId>)
    (cameraService: CameraService)
    =
    let positions = snapshot.Positions
    let projectileKeys = projectiles |> HashMap.keys

    positions
    |> HashMap.chooseV(fun entityId pos ->
      if projectileKeys.Contains entityId then
        ValueNone
      elif not(liveEntities.Contains entityId) then
        ValueNone
      else
        let size = Core.Constants.Entity.Size

        let entityRect =
          Rectangle(
            int(pos.X - size.X / 2.0f),
            int(pos.Y - size.Y / 2.0f),
            int size.X,
            int size.Y
          )

        let cullingPadding = 64.0f

        let isVisible =
          cameraService.GetAllCameras()
          |> Array.exists(fun struct (_, camera) ->
            let paddedViewportRect =
              Rectangle(
                int(
                  camera.Position.X
                  - float32 camera.Viewport.Width / (2.0f * camera.Zoom)
                  - cullingPadding
                ),
                int(
                  camera.Position.Y
                  - float32 camera.Viewport.Height / (2.0f * camera.Zoom)
                  - cullingPadding
                ),
                int(
                  float32 camera.Viewport.Width / camera.Zoom
                  + 2.0f * cullingPadding
                ),
                int(
                  float32 camera.Viewport.Height / camera.Zoom
                  + 2.0f * cullingPadding
                )
              )

            paddedViewportRect.Intersects entityRect)

        if isVisible then
          if entityId = playerId then
            ValueSome(DrawPlayer entityRect)
          else
            ValueSome(DrawEnemy entityRect)
        else
          ValueNone)
    |> HashMap.toValueArray

  let private generateProjectileCommands
    (projectiles: HashMap<Guid<EntityId>, LiveProjectile>)
    (snapshot: Projections.MovementSnapshot)
    =
    let positions = snapshot.Positions

    projectiles
    |> HashMap.keys
    |> HashSet.chooseV(fun projectileId ->
      match positions |> HashMap.tryFindV projectileId with
      | ValueSome pos ->
        let projectileSize = Core.Constants.Projectile.Size

        let rect =
          Rectangle(
            int(pos.X - projectileSize.X / 2.0f),
            int(pos.Y - projectileSize.Y / 2.0f),
            int projectileSize.X,
            int projectileSize.Y
          )

        ValueSome(DrawProjectile rect)
      | ValueNone -> ValueNone)

  let private generateTargetingIndicatorCommands
    (targetingMode: Skill.Targeting voption)
    (mouseState: MouseState voption)
    (cameraService: CameraService)
    (playerId: Guid<EntityId>)
    =
    targetingMode
    |> ValueOption.bind(fun _ -> mouseState)
    |> ValueOption.map(fun mouseState ->
      let rawPos =
        Vector2(float32 mouseState.Position.X, float32 mouseState.Position.Y)

      let indicatorPosition =
        match cameraService.ScreenToWorld(rawPos, playerId) with
        | ValueSome worldPos -> worldPos
        | ValueNone -> rawPos

      let indicatorSize = Core.Constants.UI.TargetingIndicatorSize

      let rect =
        Rectangle(
          int(indicatorPosition.X - indicatorSize.X / 2.0f),
          int(indicatorPosition.Y - indicatorSize.Y / 2.0f),
          int indicatorSize.X,
          int indicatorSize.Y
        )

      DrawTargetingIndicator rect)

  [<Struct>]
  type DrawCommandContext = {
    LiveProjectiles: HashMap<Guid<EntityId>, LiveProjectile>
    LiveEntities: HashSet<Guid<EntityId>>
    CameraService: CameraService
    PlayerId: Guid<EntityId>
    Snapshot: Projections.MovementSnapshot
    TargetingMode: Skill.Targeting voption
    MouseState: MouseState voption
  }

  let generateDrawCommands(context: DrawCommandContext) =
    let entityCmds =
      generateEntityCommands
        context.Snapshot
        context.LiveProjectiles
        context.LiveEntities
        context.PlayerId
        context.CameraService

    let projectileCmds =
      generateProjectileCommands context.LiveProjectiles context.Snapshot

    let targetingCmds =
      generateTargetingIndicatorCommands
        context.TargetingMode
        context.MouseState
        context.CameraService
        context.PlayerId

    seq {
      yield! entityCmds
      yield! projectileCmds

      match targetingCmds with
      | ValueSome cmd -> cmd
      | ValueNone -> ()
    }

  // ============================================================================
  // Context Structs for Semantic Grouping
  // ============================================================================

  /// Context for computing an entity's render position and transform
  [<Struct>]
  type EntityRenderContext = {
    EntityId: Guid<EntityId>
    LogicPosition: Vector2
    Facing: float32
    PixelsPerUnit: Vector2
    SquishFactor: float32
  }

  /// Context for rendering particles as camera-facing billboards
  [<Struct>]
  type ParticleRenderContext = {
    CamRight: Vector3
    CamUp: Vector3
    PixelsPerUnit: Vector2
    ModelScale: float32
  }

  /// Context for a scenario render pass (per-frame data)
  [<Struct>]
  type ScenarioRenderContext = {
    PixelsPerUnit: Vector2
    SquishFactor: float32
    Snapshot: Projections.MovementSnapshot
    EntityPoses: HashMap<Guid<EntityId>, HashMap<string, Matrix>>
    LiveProjectiles: HashMap<Guid<EntityId>, LiveProjectile>
  }

  /// Lifetime resources for entity rendering (allocated once at creation)
  type EntityRenderResources = {
    Game: Game
    Models: HashMap<string, Model>
    ModelStore: ModelStore
    NodeTransformsPool: System.Collections.Generic.Dictionary<string, Matrix>
  }

  /// Lifetime resources for particle rendering
  type ParticleRenderResources = {
    Game: Game
    BillboardBatch: BillboardBatch
    GetTexture: string -> Texture2D voption
    GetModel: string -> Model voption
    FallbackTexture: Texture2D
  }

  /// Lifetime resources for UI rendering
  type UIRenderResources = {
    Game: Game
    SpriteBatch: SpriteBatch
    Texture: Texture2D
    World: World.World
    TargetingService: TargetingService
    CameraService: CameraService
    PlayerId: Guid<EntityId>
  }

  /// Combined frame context (camera + scenario)
  [<Struct>]
  type FrameRenderContext = {
    Camera: Camera
    Scenario: ScenarioRenderContext
  }

  // ============================================================================
  // Pure Inline Helper Functions (Hot Path)
  // ============================================================================

  /// Computes altitude for descending projectiles. Returns 0 for grounded entities.
  let inline computeProjectileAltitude
    (liveProjectiles: HashMap<Guid<EntityId>, LiveProjectile>)
    (entityId: Guid<EntityId>)
    (pixelsPerUnitY: float32)
    : float32 =
    match liveProjectiles |> HashMap.tryFindV entityId with
    | ValueSome proj ->
      match proj.Info.Variations with
      | ValueSome(Projectile.Descending(currentAltitude, _)) ->
        currentAltitude / pixelsPerUnitY
      | _ -> 0.0f
    | ValueNone -> 0.0f

  /// Applies isometric altitude offset to base render position
  let inline logicToRenderWithAltitude
    (logicPos: Vector2)
    (altitude: float32)
    (pixelsPerUnit: Vector2)
    : Vector3 =
    let basePos = RenderMath.Legacy.LogicToRender logicPos pixelsPerUnit
    Vector3(basePos.X, basePos.Y + altitude, basePos.Z - altitude)

  /// Computes projectile tilt based on altitude (falling vs horizontal flight)
  let inline computeProjectileTilt(altitude: float32) : float32 =
    if altitude > 0.0f then 0.0f else MathHelper.PiOver2

  /// Computes projectile facing (no facing while falling)
  let inline computeProjectileFacing
    (altitude: float32)
    (facing: float32)
    : float32 =
    if altitude > 0.0f then 0.0f else facing

  /// Computes the world matrix for a projectile entity
  let inline computeProjectileWorldMatrix
    (context: EntityRenderContext)
    (altitude: float32)
    : Matrix =
    let renderPos =
      logicToRenderWithAltitude
        context.LogicPosition
        altitude
        context.PixelsPerUnit

    let tilt = computeProjectileTilt altitude
    let facing = computeProjectileFacing altitude context.Facing

    RenderMath.Legacy.GetTiltedEntityWorldMatrix
      renderPos
      facing
      tilt
      0.0f
      MathHelper.PiOver4
      context.SquishFactor
      Core.Constants.Entity.ModelScale

  /// Computes the world matrix for a regular entity
  let inline computeEntityWorldMatrix(context: EntityRenderContext) : Matrix =
    let renderPos =
      RenderMath.Legacy.LogicToRender
        context.LogicPosition
        context.PixelsPerUnit

    RenderMath.Legacy.GetEntityWorldMatrix
      renderPos
      context.Facing
      MathHelper.PiOver4
      context.SquishFactor
      Core.Constants.Entity.ModelScale

  /// Computes the base world matrix for an entity or projectile
  let inline computeEntityBaseMatrix
    (context: EntityRenderContext)
    (altitude: float32)
    (isProjectile: bool)
    : Matrix =
    if isProjectile then
      computeProjectileWorldMatrix context altitude
    else
      computeEntityWorldMatrix context

  /// Computes particle world position based on simulation space
  let inline computeParticleWorldPosition
    (config: Particles.EmitterConfig)
    (particlePos: Vector3)
    (effectPos: Vector3)
    : Vector3 =
    match config.SimulationSpace with
    | Particles.SimulationSpace.World -> particlePos
    | Particles.SimulationSpace.Local -> particlePos + effectPos

  /// Transforms particle position to render space with altitude
  let inline particleToRenderPosition
    (pWorldPos: Vector3)
    (pixelsPerUnit: Vector2)
    : Vector3 =
    let logicPos = Vector2(pWorldPos.X, pWorldPos.Z)
    let baseRenderPos = RenderMath.Legacy.LogicToRender logicPos pixelsPerUnit
    let altitude = (pWorldPos.Y / pixelsPerUnit.Y) * 2.0f

    Vector3(
      baseRenderPos.X,
      baseRenderPos.Y + altitude,
      baseRenderPos.Z - altitude
    )

  /// Computes effect position from owner's interpolated position
  let inline computeEffectPosition
    (owner: Guid<EntityId> voption)
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (fallbackPos: Vector3)
    : Vector3 =
    match owner with
    | ValueSome ownerId ->
      match positions |> HashMap.tryFindV ownerId with
      | ValueSome interpPos -> Vector3(interpPos.X, 0.0f, interpPos.Y)
      | ValueNone -> fallbackPos
    | ValueNone -> fallbackPos

  // ============================================================================
  // Rig Hierarchy Functions
  // ============================================================================

  [<TailCall>]
  let rec private collectPath
    (nodeTransforms: System.Collections.Generic.Dictionary<string, Matrix>)
    (rigData: HashMap<string, RigNode>)
    (currentName: string)
    (currentNode: RigNode)
    (path: (string * RigNode) list)
    =
    if nodeTransforms.ContainsKey currentName then
      struct (path, nodeTransforms.[currentName])
    else
      match currentNode.Parent with
      | ValueNone ->
        struct ((currentName, currentNode) :: path, Matrix.Identity)
      | ValueSome pName ->
        match rigData |> HashMap.tryFindV pName with
        | ValueNone ->
          struct ((currentName, currentNode) :: path, Matrix.Identity)
        | ValueSome pNode ->
          collectPath
            nodeTransforms
            rigData
            pName
            pNode
            ((currentName, currentNode) :: path)

  /// Gets animation matrix for a node, using ValueOption to avoid GC
  let inline private getNodeAnimation
    (entityPose: HashMap<string, Matrix>)
    (nodeName: string)
    : Matrix =
    match entityPose |> HashMap.tryFindV nodeName with
    | ValueSome m -> m
    | ValueNone -> Matrix.Identity

  [<TailCall>]
  let rec private applyTransforms
    (entityPose: HashMap<string, Matrix>)
    (nodeTransforms: System.Collections.Generic.Dictionary<string, Matrix>)
    (struct (nodes, currentParentWorld):
      struct ((string * RigNode) list * Matrix))
    =
    match nodes with
    | [] -> currentParentWorld
    | (name, node) :: rest ->
      let localAnim = getNodeAnimation entityPose name

      let pivotTranslation = Matrix.CreateTranslation node.Pivot
      let inversePivotTranslation = Matrix.CreateTranslation -node.Pivot
      let offsetTranslation = Matrix.CreateTranslation node.Offset

      let localWorld =
        inversePivotTranslation
        * localAnim
        * pivotTranslation
        * offsetTranslation

      let world = localWorld * currentParentWorld
      nodeTransforms.[name] <- world
      applyTransforms entityPose nodeTransforms struct (rest, world)

  // ============================================================================
  // Granular Render Functions
  // ============================================================================

  /// Draws a single model with lighting setup
  let private drawModel (camera: Camera) (model: Model) (worldMatrix: Matrix) =
    for mesh in model.Meshes do
      for effect in mesh.Effects do
        let effect = effect :?> BasicEffect
        effect.EnableDefaultLighting()
        effect.PreferPerPixelLighting <- true

        effect.AmbientLightColor <- Vector3(0.2f, 0.2f, 0.2f)
        effect.SpecularColor <- Vector3.Zero

        effect.DirectionalLight0.Direction <- Vector3(-1.0f, -1.7f, 0.0f)
        effect.DirectionalLight0.DiffuseColor <- Vector3(0.6f, 0.6f, 0.6f)
        effect.DirectionalLight0.SpecularColor <- Vector3.Zero

        effect.DirectionalLight1.Enabled <- false
        effect.DirectionalLight2.Enabled <- false

        effect.World <- worldMatrix
        effect.View <- camera.View
        effect.Projection <- camera.Projection

      mesh.Draw()

  /// Draws a single rig node's model
  let inline private drawRigNode
    (camera: Camera)
    (models: HashMap<string, Model>)
    (node: RigNode)
    (finalWorld: Matrix)
    =
    models
    |> HashMap.tryFindV node.ModelAsset
    |> ValueOption.iter(fun model -> drawModel camera model finalWorld)

  /// Renders an entire entity's rig hierarchy
  let private renderEntityRig
    (camera: Camera)
    (res: EntityRenderResources)
    (rigData: HashMap<string, RigNode>)
    (entityPose: HashMap<string, Matrix>)
    (entityBaseMatrix: Matrix)
    =
    res.NodeTransformsPool.Clear()

    for nodeName, node in rigData do
      let nodeLocalWorld =
        collectPath res.NodeTransformsPool rigData nodeName node []
        |> applyTransforms entityPose res.NodeTransformsPool

      let finalWorld = nodeLocalWorld * entityBaseMatrix
      drawRigNode camera res.Models node finalWorld

  /// Gets entity pose or empty, using ValueOption
  let inline private getEntityPose
    (poses: HashMap<Guid<EntityId>, HashMap<string, Matrix>>)
    (entityId: Guid<EntityId>)
    : HashMap<string, Matrix> =
    match poses |> HashMap.tryFindV entityId with
    | ValueSome pose -> pose
    | ValueNone -> HashMap.empty

  /// Gets entity facing or default, using ValueOption
  let inline private getEntityFacing
    (rotations: HashMap<Guid<EntityId>, float32>)
    (entityId: Guid<EntityId>)
    : float32 =
    match rotations |> HashMap.tryFindV entityId with
    | ValueSome r -> r
    | ValueNone -> 0.0f

  /// Renders a single entity (handles both regular entities and projectiles)
  let private renderSingleEntity
    (res: EntityRenderResources)
    (frame: FrameRenderContext)
    (entityId: Guid<EntityId>)
    (pos: Vector2)
    =
    let ctx = frame.Scenario
    let configId = ctx.Snapshot.ModelConfigIds |> HashMap.tryFindV entityId

    match configId with
    | ValueNone -> ()
    | ValueSome cfgId ->
      match res.ModelStore.tryFind cfgId with
      | ValueNone -> ()
      | ValueSome config ->
        let facing = getEntityFacing ctx.Snapshot.Rotations entityId
        let entityPose = getEntityPose ctx.EntityPoses entityId
        let isProjectile = ctx.LiveProjectiles |> HashMap.containsKey entityId

        let altitude =
          computeProjectileAltitude
            ctx.LiveProjectiles
            entityId
            ctx.PixelsPerUnit.Y

        let renderContext: EntityRenderContext = {
          EntityId = entityId
          LogicPosition = pos
          Facing = facing
          PixelsPerUnit = ctx.PixelsPerUnit
          SquishFactor = ctx.SquishFactor
        }

        let entityBaseMatrix =
          computeEntityBaseMatrix renderContext altitude isProjectile

        renderEntityRig frame.Camera res config.Rig entityPose entityBaseMatrix

  /// Renders all entities in the snapshot
  let private renderAllEntities
    (res: EntityRenderResources)
    (frame: FrameRenderContext)
    =
    res.Game.GraphicsDevice.DepthStencilState <- DepthStencilState.Default
    res.Game.GraphicsDevice.RasterizerState <- RasterizerState.CullNone

    for entityId, pos in frame.Scenario.Snapshot.Positions do
      renderSingleEntity res frame entityId pos

  /// Draws a single particle billboard
  let inline private drawParticleBillboard
    (billboardBatch: BillboardBatch)
    (ctx: ParticleRenderContext)
    (config: Particles.EmitterConfig)
    (particle: Particles.Particle)
    (effectPos: Vector3)
    =
    let pWorldPos =
      computeParticleWorldPosition config particle.Position effectPos

    let finalPos = particleToRenderPosition pWorldPos ctx.PixelsPerUnit
    let size = particle.Size * ctx.ModelScale

    billboardBatch.Draw(
      finalPos,
      size,
      0.0f,
      particle.Color,
      ctx.CamRight,
      ctx.CamUp
    )

  let inline private setBlendState
    (device: GraphicsDevice)
    (blendMode: Particles.BlendMode)
    =
    match blendMode with
    | Particles.BlendMode.AlphaBlend ->
      device.BlendState <- BlendState.AlphaBlend
    | Particles.BlendMode.Additive -> device.BlendState <- BlendState.Additive

  /// Particle data grouped by (textureId, blendMode) for batched rendering
  [<Struct>]
  type ParticleDrawData = {
    Config: Particles.EmitterConfig
    Particle: Particles.Particle
    EffectPos: Vector3
  }

  type BillboardParticleGroups =
    Dictionary<
      struct (string * Particles.BlendMode),
      ResizeArray<ParticleDrawData>
     >

  /// Groups billboard particles by (texture, blendMode) in a single pass
  let private groupBillboardParticles
    (visualEffects: ResizeArray<Particles.VisualEffect>)
    (positions: HashMap<Guid<EntityId>, Vector2>)
    : BillboardParticleGroups =

    let groups: BillboardParticleGroups = Dictionary()

    for effect in visualEffects do
      if effect.IsAlive.Value then
        let effectPos =
          computeEffectPosition effect.Owner positions effect.Position.Value

        for emitter in effect.Emitters do
          if emitter.Particles.Count > 0 then
            match emitter.Config.RenderMode with
            | Particles.Billboard tex ->
              let key = struct (tex, emitter.Config.BlendMode)

              let group =
                match groups.TryGetValue(key) with
                | true, existing -> existing
                | false, _ ->
                  let newGroup = ResizeArray()
                  groups.[key] <- newGroup
                  newGroup

              for particle in emitter.Particles do
                group.Add {
                  Config = emitter.Config
                  Particle = particle
                  EffectPos = effectPos
                }
            | Particles.Mesh _ -> () // Skip mesh particles in billboard pass

    groups

  /// Renders a batch of particles with the given texture
  let inline private renderParticleBatch
    (billboardBatch: BillboardBatch)
    (ctx: ParticleRenderContext)
    (camera: Camera)
    (texture: Texture2D)
    (group: ResizeArray<ParticleDrawData>)
    =
    billboardBatch.Begin(camera.View, camera.Projection, texture)

    for data in group do
      drawParticleBillboard
        billboardBatch
        ctx
        data.Config
        data.Particle
        data.EffectPos

    billboardBatch.End()

  /// Renders all particles
  let private renderAllParticles
    (res: ParticleRenderResources)
    (world: World.World)
    (frame: FrameRenderContext)
    =
    res.Game.GraphicsDevice.DepthStencilState <- DepthStencilState.None
    res.Game.GraphicsDevice.RasterizerState <- RasterizerState.CullNone

    let viewInv = Matrix.Invert(frame.Camera.View)

    let ctx: ParticleRenderContext = {
      CamRight = viewInv.Right
      CamUp = viewInv.Up
      PixelsPerUnit = frame.Scenario.PixelsPerUnit
      ModelScale = Core.Constants.Entity.ModelScale
    }

    let particleGroups =
      groupBillboardParticles
        world.VisualEffects
        frame.Scenario.Snapshot.Positions

    for kvp in particleGroups do
      let struct (textureId, blendMode) = kvp.Key
      let group = kvp.Value

      match res.GetTexture textureId with
      | ValueSome tex ->
        setBlendState res.Game.GraphicsDevice blendMode

        renderParticleBatch res.BillboardBatch ctx frame.Camera tex group
      | ValueNone ->
        res.Game.GraphicsDevice.BlendState <- BlendState.Additive

        renderParticleBatch
          res.BillboardBatch
          ctx
          frame.Camera
          res.FallbackTexture
          group

  /// Renders all mesh particles
  let private renderAllMeshParticles
    (res: ParticleRenderResources)
    (world: World.World)
    (frame: FrameRenderContext)
    =
    res.Game.GraphicsDevice.DepthStencilState <- DepthStencilState.Default
    res.Game.GraphicsDevice.RasterizerState <- RasterizerState.CullNone
    res.Game.GraphicsDevice.BlendState <- BlendState.AlphaBlend

    let ppu = frame.Scenario.PixelsPerUnit
    let modelScale = Core.Constants.Entity.ModelScale

    for effect in world.VisualEffects do
      // Skip dead effects (same as billboard particles)
      if effect.IsAlive.Value then
        let effectPos = effect.Position.Value

        for meshEmitter in effect.MeshEmitters do
          // ModelPath pre-extracted at creation - no hot-path branching
          match res.GetModel meshEmitter.ModelPath with
          | ValueSome model ->
            let squishFactor = ppu.X / ppu.Y

            for particle in meshEmitter.Particles do
              let pWorldPos =
                computeParticleWorldPosition
                  meshEmitter.Config
                  particle.Position
                  effectPos

              let renderPos = particleToRenderPosition pWorldPos ppu
              let baseScale = particle.Scale * modelScale

              // Apply non-uniform scaling via ScaleAxis
              // ScaleAxis determines which axes participate in scaling
              // (1,1,1) = uniform, (0,1,0) = height only, (1,0,1) = width/depth only
              let axis = meshEmitter.Config.ScaleAxis
              let scaleX = 1.0f + (baseScale - 1.0f) * axis.X
              let scaleY = 1.0f + (baseScale - 1.0f) * axis.Y
              let scaleZ = 1.0f + (baseScale - 1.0f) * axis.Z
              let scaleMatrix = Matrix.CreateScale(scaleX, scaleY, scaleZ)

              // Squish compensation for isometric view
              let squishCompensation =
                Matrix.CreateScale(1.0f, 1.0f, squishFactor)

              // Isometric correction (same as used in RenderMath)
              let isoRot =
                Matrix.CreateLookAt(
                  Vector3.Zero,
                  Vector3.Normalize(Vector3(-1.0f, -1.0f, -1.0f)),
                  Vector3.Up
                )

              let topDownRot =
                Matrix.CreateLookAt(Vector3.Zero, Vector3.Down, Vector3.Forward)

              let correction = isoRot * Matrix.Invert topDownRot

              let worldMatrix =
                // ScalePivot as SCREEN-SPACE offset (after iso correction)
                // Y = screen vertical (up), X = screen horizontal (right)
                let pivot = meshEmitter.Config.ScalePivot

                Matrix.CreateFromQuaternion(particle.Rotation)
                * scaleMatrix
                * correction
                * squishCompensation
                * Matrix.CreateTranslation(pivot) // Now: Y = screen up
                * Matrix.CreateTranslation(renderPos)

              drawModel frame.Camera model worldMatrix
          | ValueNone -> ()

  /// Renders targeting indicator UI overlay
  let private renderTargetingIndicator
    (res: UIRenderResources)
    (camera: Camera)
    =
    res.Game.GraphicsDevice.DepthStencilState <- DepthStencilState.None

    let mouseState = res.World.RawInputStates |> AMap.map' _.Mouse |> AMap.force

    let targetingMode = res.TargetingService.TargetingMode |> AVal.force
    let mouseState = HashMap.tryFindV res.PlayerId mouseState

    let targetingCmds =
      generateTargetingIndicatorCommands
        targetingMode
        mouseState
        res.CameraService
        res.PlayerId

    match targetingCmds with
    | ValueSome(DrawTargetingIndicator rect) ->
      let transform =
        RenderMath.Legacy.GetSpriteBatchTransform
          camera.Position
          camera.Zoom
          camera.Viewport.Width
          camera.Viewport.Height

      res.Game.GraphicsDevice.Viewport <- camera.Viewport
      res.SpriteBatch.Begin(transformMatrix = transform)
      res.SpriteBatch.Draw(res.Texture, rect, Color.Blue * 0.5f)
      res.SpriteBatch.End()
    | _ -> ()

  // ============================================================================
  // RenderService Factory
  // ============================================================================

  type RenderService =
    abstract Draw: Camera -> unit

  let create
    (
      game: Game,
      env: PomoEnvironment,
      playerId: Guid<EntityId>,
      modelStore: ModelStore
    ) =
    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices

    let world = core.World
    let targetingService = gameplay.TargetingService
    let projections = gameplay.Projections
    let cameraService = gameplay.CameraService
    let spriteBatch = new SpriteBatch(game.GraphicsDevice)
    let texture = new Texture2D(game.GraphicsDevice, 1, 1)
    texture.SetData [| Color.White |]

    let billboardBatch = new BillboardBatch(game.GraphicsDevice)

    // Texture cache
    let textureCache =
      System.Collections.Generic.Dictionary<string, Texture2D voption>()

    let getTexture(id: string) =
      match textureCache.TryGetValue id with
      | true, cached -> cached
      | false, _ ->
        try
          let loaded = game.Content.Load<Texture2D>(id)
          let result = ValueSome loaded
          textureCache.[id] <- result
          result
        with _ ->
          let result = ValueNone
          textureCache.[id] <- result
          result

    // Mesh particle model cache (lazy-loaded like textures)
    let particleModelCache = Dictionary<string, Model voption>()

    let getParticleModel(id: string) =
      match particleModelCache |> Dictionary.tryFindV id with
      | ValueSome cached -> cached
      | ValueNone ->
        try
          let loaded = game.Content.Load<Model>(id)
          let result = ValueSome loaded
          particleModelCache.[id] <- result
          result
        with _ ->
          let result = ValueNone
          particleModelCache.[id] <- result
          result

    // Load Models (one-time at creation)
    let models =
      modelStore.all()
      |> Seq.collect(fun config ->
        config.Rig
        |> HashMap.toValueArray
        |> Array.map(fun node -> node.ModelAsset))
      |> Seq.distinct
      |> Seq.choose(fun assetName ->
        try
          let path = "3d_models/kaykit_prototype/" + assetName
          Some(assetName, game.Content.Load<Model> path)
        with _ ->
          None)
      |> HashMap.ofSeq

    // Pooled dictionary for node transforms (reused across entities)
    let nodeTransformsPool =
      System.Collections.Generic.Dictionary<string, Matrix>()

    // Build lifetime resource contexts
    let entityRes: EntityRenderResources = {
      Game = game
      Models = models
      ModelStore = modelStore
      NodeTransformsPool = nodeTransformsPool
    }

    let particleRes: ParticleRenderResources = {
      Game = game
      BillboardBatch = billboardBatch
      GetTexture = getTexture
      GetModel = getParticleModel
      FallbackTexture = texture
    }

    let uiRes: UIRenderResources = {
      Game = game
      SpriteBatch = spriteBatch
      Texture = texture
      World = world
      TargetingService = targetingService
      CameraService = cameraService
      PlayerId = playerId
    }

    { new RenderService with
        member _.Draw(camera: Camera) =
          let entityScenarios = world.EntityScenario |> AMap.force
          let scenarios = world.Scenarios |> AMap.force
          let currentPoses = world.Poses |> AMap.force
          let liveProjectiles = world.LiveProjectiles |> AMap.force

          match entityScenarios |> HashMap.tryFindV playerId with
          | ValueNone -> ()
          | ValueSome scenarioId ->
            match scenarios |> HashMap.tryFindV scenarioId with
            | ValueNone -> ()
            | ValueSome scenario ->
              let map = scenario.Map

              let pixelsPerUnit =
                Vector2(float32 map.TileWidth, float32 map.TileHeight)

              let squishFactor = pixelsPerUnit.X / pixelsPerUnit.Y
              let snapshot = projections.ComputeMovementSnapshot scenarioId

              // Build per-frame context
              let frame: FrameRenderContext = {
                Camera = camera
                Scenario = {
                  PixelsPerUnit = pixelsPerUnit
                  SquishFactor = squishFactor
                  Snapshot = snapshot
                  EntityPoses = currentPoses
                  LiveProjectiles = liveProjectiles
                }
              }

              // === Render Passes (Orchestrator Style) ===
              renderAllEntities entityRes frame
              renderAllMeshParticles particleRes world frame
              renderAllParticles particleRes world frame
              renderTargetingIndicator uiRes camera
    }

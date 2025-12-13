namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Graphics

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Camera
open Pomo.Core.Systems.Targeting
open Pomo.Core.Stores
open Pomo.Core.Domain.Animation


module Render =
  open Pomo.Core
  open Pomo.Core.Domain.Projectile
  open Microsoft.Xna.Framework.Input
  open Pomo.Core.Environment

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

        // Check if entity is within any camera's viewport (for split-screen)
        let entityRect =
          Rectangle(
            int(pos.X - size.X / 2.0f),
            int(pos.Y - size.Y / 2.0f),
            int size.X,
            int size.Y
          )

        // Add padding to prevent entities from popping in/out at viewport boundaries
        let cullingPadding = 64.0f // Add 64 pixels of padding around viewport

        // Get all camera viewports and check if entity is within any of them
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
      let localAnim =
        entityPose
        |> HashMap.tryFind name
        |> Option.defaultValue Matrix.Identity

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

    // Load Models
    // The store returns Rigs (HashMap<string, RigNode>)
    // We need to extract all unique ModelAsset names from all nodes in all Rigs
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

    let drawModel (camera: Camera) (model: Model) (worldMatrix: Matrix) =
      for mesh in model.Meshes do
        for effect in mesh.Effects do
          let effect = effect :?> BasicEffect
          effect.EnableDefaultLighting()
          effect.PreferPerPixelLighting <- true

          // Lighting adjustments to match isometric background and remove "plastic" look
          effect.AmbientLightColor <- Vector3(0.2f, 0.2f, 0.2f) // Darker to increase contrast
          effect.SpecularColor <- Vector3.Zero // No shine at all

          effect.DirectionalLight0.Direction <- Vector3(-1.0f, -1.7f, 0.0f) // 60-degree elevation from Right, neutral Z
          effect.DirectionalLight0.DiffuseColor <- Vector3(0.6f, 0.6f, 0.6f) // Less intense diffuse
          effect.DirectionalLight0.SpecularColor <- Vector3.Zero // No specular from light

          // Disable other default lights that might be causing excessive brightness/shine
          effect.DirectionalLight1.Enabled <- false
          effect.DirectionalLight2.Enabled <- false

          effect.World <- worldMatrix
          effect.View <- camera.View
          effect.Projection <- camera.Projection

        mesh.Draw()

    { new RenderService with
        member _.Draw(camera: Camera) =
          let entityScenarios = world.EntityScenario |> AMap.force
          let scenarios = world.Scenarios |> AMap.force
          let currentPoses = world.Poses |> AMap.force
          let liveProjectiles = world.LiveProjectiles |> AMap.force

          match entityScenarios |> HashMap.tryFindV playerId with
          | ValueSome scenarioId ->
            match scenarios |> HashMap.tryFindV scenarioId with
            | ValueSome scenario ->
              let map = scenario.Map

              let pixelsPerUnit =
                Vector2(float32 map.TileWidth, float32 map.TileHeight)

              let squishFactor = pixelsPerUnit.X / pixelsPerUnit.Y

              let snapshot = projections.ComputeMovementSnapshot scenarioId

              // 3D Pass
              game.GraphicsDevice.DepthStencilState <- DepthStencilState.Default
              game.GraphicsDevice.RasterizerState <- RasterizerState.CullNone

              for id, pos in snapshot.Positions do
                let configId = snapshot.ModelConfigIds |> HashMap.tryFindV id

                // Standard Facing Rotation (Yaw)
                let facing =
                  snapshot.Rotations
                  |> HashMap.tryFind id
                  |> Option.defaultValue 0.0f

                let modelConfig =
                  match configId with
                  | ValueSome id -> modelStore.tryFind id
                  | ValueNone -> ValueNone

                match modelConfig with
                | ValueSome config ->
                  let rigData = config.Rig

                  let entityPose =
                    currentPoses
                    |> HashMap.tryFind id
                    |> Option.defaultValue HashMap.empty

                  // Calculate altitude offset for descending projectiles
                  let altitude, isDescending =
                    match liveProjectiles |> HashMap.tryFindV id with
                    | ValueSome proj ->
                      match proj.Info.Variations with
                      | ValueSome(Projectile.Descending(currentAltitude, _)) ->
                        currentAltitude / pixelsPerUnit.Y, true
                      | _ -> 0.0f, false
                    | ValueNone -> 0.0f, false

                  // Apply altitude to render position (shifts up on screen via Z)
                  let baseRenderPos = RenderMath.LogicToRender pos pixelsPerUnit

                  let renderPos =
                    Vector3(
                      baseRenderPos.X,
                      baseRenderPos.Y + altitude, // Depth sorting
                      baseRenderPos.Z - altitude // Screen-space up (north = up on screen)
                    )

                  // Calculate Base Transform (Position + Facing)
                  // Note: We apply squish and scale here for the root,
                  // but child nodes should inherit appropriately.
                  // RenderMath helpers combine everything, so we might need to decompose
                  // if we want strict hierarchy.
                  // For now, let's treat "Root" special or apply BaseTransform to all if parent is None.

                  // Simplified Hierarchy Traversal
                  // We need World Matrices for each node.
                  let nodeTransforms =
                    System.Collections.Generic.Dictionary<string, Matrix>()

                  // Calculate Entity World Transform (Location in game world)
                  let entityBaseMatrix =
                    // Check if projectile to apply special tilt
                    match configId, isDescending with
                    | _, true ->
                      // Descending projectiles: tilt to point downward (falling from sky)
                      // No horizontal tilt (0), just straight down orientation
                      RenderMath.GetTiltedEntityWorldMatrix
                        renderPos
                        0.0f // No horizontal facing - falling straight down
                        0.0f // No X tilt - model points down naturally with camera
                        0.0f // No spin for falling objects
                        MathHelper.PiOver4
                        squishFactor
                        Core.Constants.Entity.ModelScale
                    | ValueSome "Projectile", false ->
                      // Regular projectiles: tilt to fly horizontally
                      RenderMath.GetTiltedEntityWorldMatrix
                        renderPos
                        facing
                        MathHelper.PiOver2
                        0.0f // Spin handled by animation system now!
                        MathHelper.PiOver4
                        squishFactor
                        Core.Constants.Entity.ModelScale
                    | ValueSome "Barrel_B", false ->
                      // Barrel projectiles: same as regular tilted projectiles
                      RenderMath.GetTiltedEntityWorldMatrix
                        renderPos
                        facing
                        MathHelper.PiOver2
                        0.0f
                        MathHelper.PiOver4
                        squishFactor
                        Core.Constants.Entity.ModelScale
                    | _ ->
                      RenderMath.GetEntityWorldMatrix
                        (RenderMath.LogicToRender pos pixelsPerUnit)
                        facing
                        MathHelper.PiOver4
                        squishFactor
                        Core.Constants.Entity.ModelScale

                  for nodeName, node in rigData do
                    let nodeLocalWorld =
                      collectPath nodeTransforms rigData nodeName node []
                      |> applyTransforms entityPose nodeTransforms

                    let finalWorld = nodeLocalWorld * entityBaseMatrix

                    models
                    |> HashMap.tryFindV node.ModelAsset
                    |> ValueOption.iter(fun model ->
                      drawModel camera model finalWorld)

                | ValueNone -> () // No rig, can't draw

              // Particle Pass - Render on top (no depth test)
              game.GraphicsDevice.DepthStencilState <- DepthStencilState.None

              game.GraphicsDevice.RasterizerState <- RasterizerState.CullNone

              let viewInv = Matrix.Invert(camera.View)
              let camRight = viewInv.Right
              let camUp = viewInv.Up

              let particlesToDraw =
                world.VisualEffects
                |> Seq.filter(fun e -> e.IsAlive.Value)
                |> Seq.collect(fun e ->
                  // Fix for Jitter: If Local Space, we MUST use the Interpolated Position from the Snapshot
                  // matching the Owner. Otherwise, particles lag behind the rendered entity.
                  let effectPos =
                    match e.Owner with
                    | ValueSome ownerId ->
                      match snapshot.Positions.TryFindV ownerId with
                      | ValueSome interpPos ->
                        // Map Entity (X, Y) to Particle Space (X, 0, Z or whatever logic uses)
                        // Particle System uses: X, Z(Depth), Y(Altitude) logic?
                        // No, ParticleSystem uses Vector3(X, Y_Altitude, Z_Depth).
                        // Entity Pos is Vector2(X, Y_Depth).
                        // So we map Entity.Y -> Particle.Z
                        Vector3(interpPos.X, 0.0f, interpPos.Y)
                      | ValueNone -> e.Position.Value // Fallback to logic ticks
                    | ValueNone -> e.Position.Value

                  e.Emitters
                  |> Seq.filter(fun em -> em.Particles.Count > 0)
                  |> Seq.collect(fun em ->
                    em.Particles |> Seq.map(fun p -> em.Config, p, effectPos)))
                |> Seq.groupBy(fun (config, _, _) ->
                  config.Texture, config.BlendMode)

              // Check if any particles (Debug)
              // if Seq.isEmpty particlesToDraw then
              //   Console.WriteLine "No particles to draw"

              for (textureId, blendMode), group in particlesToDraw do
                match getTexture textureId with
                | ValueSome tex ->
                  match blendMode with
                  | Particles.BlendMode.AlphaBlend ->
                    game.GraphicsDevice.BlendState <- BlendState.AlphaBlend
                  | Particles.BlendMode.Additive ->
                    game.GraphicsDevice.BlendState <- BlendState.Additive

                  billboardBatch.Begin(camera.View, camera.Projection, tex)

                  for config, p, effectPos in group do
                    // Determine world position based on Simulation Space
                    let pWorldPos =
                      match config.SimulationSpace with
                      | Particles.SimulationSpace.World -> p.Position
                      | Particles.SimulationSpace.Local ->
                        // If Local, p.Position is relative. Add Effect Position.
                        // Effect Pos: X = WorldX, Y = Altitude (0), Z = WorldY (Depth)
                        // Particle: X, Y(Alt), Z
                        p.Position + effectPos

                    // Transform Logic Position (X, Z, Y-Up) to Render Space
                    // Transform Logic Position (X, Z, Y-Up) to Render Space
                    let logicPos = Vector2(pWorldPos.X, pWorldPos.Z)

                    let baseRenderPos =
                      RenderMath.LogicToRender logicPos pixelsPerUnit

                    // Fix for Verticality: Simulate isometric height by shifting Z (Screen Y) and Y (Depth).
                    let altitude = (pWorldPos.Y / pixelsPerUnit.Y) * 2.0f

                    let finalPos =
                      Vector3(
                        baseRenderPos.X,
                        baseRenderPos.Y + altitude,
                        baseRenderPos.Z - altitude
                      )

                    // Scale Size: Use ModelScale for consistency with entity rendering
                    let size = p.Size * Core.Constants.Entity.ModelScale

                    billboardBatch.Draw(
                      finalPos,
                      size,
                      0.0f, // Rotation removed per user request
                      p.Color,
                      camRight,
                      camUp
                    )

                  billboardBatch.End()
                | ValueNone ->
                  // DEBUG FALLBACK: Draw with white texture
                  // Console.WriteLine($"Fallback texture for {textureId}")

                  // Use Additive for fallback
                  game.GraphicsDevice.BlendState <- BlendState.Additive
                  billboardBatch.Begin(camera.View, camera.Projection, texture)

                  for config, p, effectPos in group do
                    // Determine world position based on Simulation Space
                    let pWorldPos =
                      match config.SimulationSpace with
                      | Particles.SimulationSpace.World -> p.Position
                      | Particles.SimulationSpace.Local ->
                        p.Position + effectPos

                    // APPLY TRANSFORM HERE TOO
                    let logicPos = Vector2(pWorldPos.X, pWorldPos.Z)

                    let baseRenderPos =
                      RenderMath.LogicToRender logicPos pixelsPerUnit

                    // Fix for Verticality: Simulate isometric height by shifting Z (Screen Y) and Y (Depth).
                    let altitude = (pWorldPos.Y / pixelsPerUnit.Y) * 2.0f

                    let finalPos =
                      Vector3(
                        baseRenderPos.X,
                        baseRenderPos.Y + altitude,
                        baseRenderPos.Z - altitude
                      )

                    let size = p.Size * Core.Constants.Entity.ModelScale

                    billboardBatch.Draw(
                      finalPos,
                      size,
                      0.0f, // Rotation removed per user request
                      p.Color,
                      camRight,
                      camUp
                    )

                  billboardBatch.End()

              // 2D Pass (UI / Debug)
              // Restore states for SpriteBatch if needed
              game.GraphicsDevice.DepthStencilState <- DepthStencilState.None

              // ... (Keep existing 2D logic if needed for UI, or remove if fully 3D)
              // For now, we'll keep the targeting indicator logic but remove the 2D entity sprites

              let mouseState =
                world.RawInputStates |> AMap.map' _.Mouse |> AMap.force

              let targetingMode = targetingService.TargetingMode |> AVal.force
              let mouseState = HashMap.tryFindV playerId mouseState

              let targetingCmds =
                generateTargetingIndicatorCommands
                  targetingMode
                  mouseState
                  cameraService
                  playerId

              match targetingCmds with
              | ValueSome(DrawTargetingIndicator rect) ->
                let transform =
                  RenderMath.GetSpriteBatchTransform
                    camera.Position
                    camera.Zoom
                    camera.Viewport.Width
                    camera.Viewport.Height

                game.GraphicsDevice.Viewport <- camera.Viewport
                spriteBatch.Begin(transformMatrix = transform)
                spriteBatch.Draw(texture, rect, Color.Blue * 0.5f)
                spriteBatch.End()
              | _ -> ()
            | ValueNone -> ()
          | ValueNone -> ()
    }

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
    |> HashMap.toArrayV
    |> Array.choose(fun struct (entityId, pos) ->
      if projectileKeys.Contains entityId then
        None
      elif not(liveEntities.Contains entityId) then
        None
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
            Some(DrawPlayer entityRect)
          else
            Some(DrawEnemy entityRect)
        else
          None)

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
    match targetingMode with
    | ValueSome _ ->
      match mouseState with
      | ValueNone -> IndexList.empty
      | ValueSome mouseState ->
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

        IndexList.single(DrawTargetingIndicator rect)
    | ValueNone -> IndexList.empty

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
      yield! targetingCmds
    }

  type RenderService =
    abstract Draw: Camera -> unit

  open Pomo.Core.Environment.Patterns

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

    // Load Models
    let models =
      modelStore.all()
      |> Seq.collect id
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
          effect.AmbientLightColor <- Vector3(0.5f, 0.5f, 0.5f)
          effect.DirectionalLight0.Direction <- Vector3(1.0f, -1.0f, -1.0f)
          effect.World <- worldMatrix
          effect.View <- camera.View
          effect.Projection <- camera.Projection

        mesh.Draw()

    { new RenderService with
        member _.Draw(camera: Camera) =
          let entityScenarios = world.EntityScenario |> AMap.force
          let scenarios = world.Scenarios |> AMap.force

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
                let rotation =
                  snapshot.Rotations
                  |> HashMap.tryFind id
                  |> Option.defaultValue 0.0f

                let modelParts =
                  match snapshot.ModelConfigIds |> HashMap.tryFindV id with
                  | ValueSome configId -> modelStore.find configId
                  | ValueNone -> [| "Dummy_Base" |] // Fallback

                let renderPos = RenderMath.LogicToRender pos pixelsPerUnit

                let worldMatrix =
                  RenderMath.GetEntityWorldMatrix
                    renderPos
                    rotation
                    squishFactor
                    Core.Constants.Entity.ModelScale

                for partName in modelParts do
                  models
                  |> HashMap.tryFindV partName
                  |> ValueOption.iter(fun model ->
                    drawModel camera model worldMatrix)

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

              if not(IndexList.isEmpty targetingCmds) then
                let transform =
                  RenderMath.GetSpriteBatchTransform
                    camera.Position
                    camera.Zoom
                    camera.Viewport.Width
                    camera.Viewport.Height

                game.GraphicsDevice.Viewport <- camera.Viewport
                spriteBatch.Begin(transformMatrix = transform)

                for cmd in targetingCmds do
                  match cmd with
                  | DrawTargetingIndicator rect ->
                    spriteBatch.Draw(texture, rect, Color.Blue * 0.5f)
                  | _ -> ()

                spriteBatch.End()
            | ValueNone -> ()

          | ValueNone -> ()
    }

namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Graphics
open Pomo.Core.Projections

/// Camera system for BlockMap 3D scenes.
/// Uses Camera3D module for true 3D orthographic projection.
module BlockMapCameraSystem =

  /// Creates a CameraService for BlockMap3D gameplay.
  /// Camera follows player entity, uses 3D orthographic projection.
  let create
    (game: Game)
    (projections: ProjectionService)
    (blockMap: BlockMapDefinition)
    (playerId: Guid<EntityId>)
    : Camera.CameraService =

    // Mutable camera state - updated each frame following player
    let mutable camState = Camera3D.defaultState
    let ppu = Constants.BlockMap3DPixelsPerUnit.X // Use X component for uniform scale

    let centerOffset =
      RenderMath.BlockMap3D.calcCenterOffset blockMap.Width blockMap.Depth ppu

    { new Camera.CameraService with
        member _.GetCamera(entityId: Guid<EntityId>) =
          if entityId = playerId then
            let viewport = game.GraphicsDevice.Viewport

            // Get player position from projections
            let scenarioIdOpt =
              projections.EntityScenarios
              |> AMap.force
              |> HashMap.tryFindV playerId

            let position =
              scenarioIdOpt
              |> ValueOption.bind(fun sid ->
                projections.ComputeMovementSnapshot(sid).Positions
                |> Dictionary.tryFindV playerId)
              |> ValueOption.defaultValue WorldPosition.zero

            // Convert world position to render position using centralized math
            let renderPos =
              RenderMath.BlockMap3D.toRender position ppu centerOffset

            // Update camera to follow player in render space
            camState <- { camState with Position = renderPos }

            let view = Camera3D.getViewMatrix camState
            let proj = Camera3D.getProjectionMatrix camState viewport ppu

            ValueSome {
              Position = position
              Zoom = camState.Zoom
              Viewport = viewport
              View = view
              Projection = proj
            }
          else
            ValueNone

        member this.GetAllCameras() =
          match this.GetCamera(playerId) with
          | ValueSome cam -> [| struct (playerId, cam) |]
          | ValueNone -> Array.empty

        member _.ScreenToWorld(screenPos: Vector2, entityId: Guid<EntityId>) =
          if entityId = playerId then
            let viewport = game.GraphicsDevice.Viewport
            // Pick at ground plane (Y = 0)
            ValueSome(Camera3D.screenToWorld camState screenPos viewport ppu 0f)
          else
            ValueNone

        member _.CreatePickRay(screenPos: Vector2, entityId: Guid<EntityId>) =
          if entityId = playerId then
            let viewport = game.GraphicsDevice.Viewport
            ValueSome(Camera3D.getPickRay camState screenPos viewport ppu)
          else
            ValueNone
    }

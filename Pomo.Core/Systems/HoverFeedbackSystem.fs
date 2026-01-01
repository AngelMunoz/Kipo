namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Camera
open Pomo.Core.Domain.Cursor
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Core
open Pomo.Core.Projections
open Pomo.Core.Rendering

module HoverFeedback =
  open Pomo.Core.Environment
  open Pomo.Core.Graphics

  let inline private determineCursorForEntity
    (hoveredEntityId: Guid<EntityId>)
    (factions: HashMap<Guid<EntityId>, HashSet<Faction>>)
    (playerId: Guid<EntityId>)
    (targetingMode: Targeting voption)
    =
    let playerFactions =
      factions
      |> HashMap.tryFindV playerId
      |> ValueOption.defaultValue HashSet.empty

    let hoveredFactions =
      factions
      |> HashMap.tryFindV hoveredEntityId
      |> ValueOption.defaultValue HashSet.empty

    let relation = GameLogic.Faction.getRelation playerFactions hoveredFactions

    match targetingMode, relation with
    | ValueSome TargetEntity, GameLogic.Faction.Enemy -> Attack
    | ValueSome TargetEntity, _ -> Hand
    | ValueNone, GameLogic.Faction.Enemy -> Attack
    | ValueNone, _ -> Hand
    | _, _ -> Arrow

  let inline determineCursor
    (targetingMode: Targeting voption)
    (hoveredEntity: Guid<EntityId> voption)
    (factions: HashMap<Guid<EntityId>, HashSet<Faction>>)
    (playerId: Guid<EntityId>)
    : CursorType =
    match hoveredEntity with
    | ValueSome entityId ->
      determineCursorForEntity entityId factions playerId targetingMode
    | ValueNone ->
      match targetingMode with
      | ValueSome TargetEntity -> Targeting
      | ValueSome TargetPosition -> Targeting
      | ValueSome TargetDirection -> Targeting
      | _ -> Arrow



  let create
    (game: Game)
    (cameraService: CameraService)
    (cursorService: CursorService)
    (targetingService: TargetingService)
    (projections: ProjectionService)
    (world: World)
    (playerId: Guid<EntityId>)
    : GameComponent =

    { new GameComponent(game) with
        override _.Update _ =
          let mouseState = Mouse.GetState()

          let screenPos = Vector2(float32 mouseState.X, float32 mouseState.Y)

          match cameraService.GetCamera playerId with
          | ValueNone -> cursorService.SetCursor Arrow
          | ValueSome camera ->
            if not(camera.Viewport.Bounds.Contains screenPos) then
              cursorService.SetCursor Arrow
            else
              let entityScenarios = projections.EntityScenarios |> AMap.force
              let scenarios = world.Scenarios |> AMap.force
              let factions = world.Factions |> AMap.force

              match entityScenarios |> HashMap.tryFindV playerId with
              | ValueNone -> cursorService.SetCursor Arrow
              | ValueSome scenarioId ->
                let modelScale = Constants.Entity.ModelScale

                let hoveredEntity =
                  match cameraService.CreatePickRay(screenPos, playerId) with
                  | ValueNone -> ValueNone
                  | ValueSome ray ->
                    match scenarios |> HashMap.tryFindV scenarioId with
                    | ValueSome scenario when scenario.BlockMap.IsSome ->
                      let snapshot =
                        projections.ComputeMovement3DSnapshot scenarioId

                      let blockMap = scenario.BlockMap |> ValueOption.get

                      EntityPicker.pickEntityBlockMap3D
                        ray
                        Constants.BlockMap3DPixelsPerUnit.X
                        blockMap
                        modelScale
                        snapshot.Positions
                        snapshot.Rotations
                        playerId
                    | ValueSome scenario ->
                      match scenario.Map with
                      | ValueSome map ->
                        let pixelsPerUnit =
                          Vector2(float32 map.TileWidth, float32 map.TileHeight)

                        let squishFactor = pixelsPerUnit.X / pixelsPerUnit.Y

                        let snapshot =
                          projections.ComputeMovementSnapshot scenarioId

                        EntityPicker.pickEntity
                          ray
                          pixelsPerUnit
                          modelScale
                          squishFactor
                          snapshot.Positions
                          snapshot.Rotations
                          playerId
                      | ValueNone -> ValueNone
                    | ValueNone -> ValueNone

                let targetingMode = targetingService.TargetingMode |> AVal.force

                let cursor =
                  determineCursor targetingMode hoveredEntity factions playerId

                cursorService.SetCursor cursor
    }

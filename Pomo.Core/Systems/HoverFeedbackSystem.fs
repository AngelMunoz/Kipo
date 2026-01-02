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
  open Pomo.Core.Domain.BlockMap
  open Pomo.Core.Domain.Map

  let inline private tryPickEntityOnBlockMap3D
    ([<InlineIfLambda>] getPickBounds: string -> BoundingBox voption)
    (projections: ProjectionService)
    (scenarioId: Guid<ScenarioId>)
    (ray: Ray)
    (blockMap: BlockMapDefinition)
    (playerId: Guid<EntityId>)
    : Guid<EntityId> voption =
    let snapshot = projections.ComputeMovement3DSnapshot scenarioId

    EntityPicker.pickEntityBlockMap3D
      ray
      getPickBounds
      Constants.BlockMap3DPixelsPerUnit.X
      blockMap
      Constants.Entity.ModelScale
      snapshot.Positions
      snapshot.Rotations
      snapshot.ModelConfigIds
      playerId

  let inline private tryPickEntityOnTileMap
    (projections: ProjectionService)
    (scenarioId: Guid<ScenarioId>)
    (ray: Ray)
    (map: MapDefinition)
    (playerId: Guid<EntityId>)
    : Guid<EntityId> voption =
    let pixelsPerUnit = Vector2(float32 map.TileWidth, float32 map.TileHeight)

    let squishFactor = pixelsPerUnit.X / pixelsPerUnit.Y

    let snapshot = projections.ComputeMovementSnapshot scenarioId

    EntityPicker.pickEntity
      ray
      pixelsPerUnit
      Constants.Entity.ModelScale
      squishFactor
      snapshot.Positions
      snapshot.Rotations
      playerId

  let inline private tryPickHoveredEntity
    (cameraService: CameraService)
    (getPickBounds: string -> BoundingBox voption)
    (projections: ProjectionService)
    (scenarios: HashMap<Guid<ScenarioId>, Scenario>)
    (scenarioId: Guid<ScenarioId>)
    (screenPos: Vector2)
    (playerId: Guid<EntityId>)
    : Guid<EntityId> voption =
    cameraService.CreatePickRay(screenPos, playerId)
    |> ValueOption.bind(fun ray ->
      scenarios
      |> HashMap.tryFindV scenarioId
      |> ValueOption.bind(fun scenario ->
        match scenario.BlockMap, scenario.Map with
        | ValueSome blockMap, _ ->
          tryPickEntityOnBlockMap3D
            getPickBounds
            projections
            scenarioId
            ray
            blockMap
            playerId
        | ValueNone, ValueSome map ->
          tryPickEntityOnTileMap projections scenarioId ray map playerId
        | _ -> ValueNone))

  let private determineCursorForEntity
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
    (getPickBounds: string -> BoundingBox voption)
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

              let hoveredEntity =
                entityScenarios
                |> HashMap.tryFindV playerId
                |> ValueOption.bind(fun scenarioId ->
                  tryPickHoveredEntity
                    cameraService
                    getPickBounds
                    projections
                    scenarios
                    scenarioId
                    screenPos
                    playerId)

              let targetingMode = targetingService.TargetingMode |> AVal.force

              determineCursor targetingMode hoveredEntity factions playerId
              |> cursorService.SetCursor
    }

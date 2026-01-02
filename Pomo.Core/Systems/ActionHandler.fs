namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open FSharp.Control.Reactive
open Pomo.Core
open Pomo.Core.EventBus
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Action
open Pomo.Core.Systems.Targeting

module ActionHandler =
  open Microsoft.Xna.Framework.Input
  open Pomo.Core.Domain.Core
  open Pomo.Core.Domain.Camera
  open Pomo.Core.Environment

  open Pomo.Core.Rendering
  open Pomo.Core.Graphics
  open Pomo.Core.Domain.Entity
  open System.Collections.Generic
  open System

  [<Struct>]
  type private ClickContext = {
    MouseWorld: WorldPosition
    Positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>
    Rotations: IReadOnlyDictionary<Guid<EntityId>, float32>
    ModelConfigIds: IReadOnlyDictionary<Guid<EntityId>, string>
  }

  let inline private tryGetMouseState
    (world: World)
    (entityId: Guid<EntityId>)
    =
    world.RawInputStates
    |> AMap.tryFind entityId
    |> AVal.map(Option.map _.Mouse)
    |> AVal.force
    |> Option.defaultWith(fun () -> Mouse.GetState())

  let inline private tryGetMouseWorld
    (cameraService: CameraService)
    (entityId: Guid<EntityId>)
    (mouseState: MouseState)
    : WorldPosition voption =
    let rawMousePos =
      Vector2(float32 mouseState.Position.X, float32 mouseState.Position.Y)

    cameraService.ScreenToWorld(rawMousePos, entityId)

  let inline private buildClickContext
    (projections: Projections.ProjectionService)
    (scenarioId: Guid<ScenarioId>)
    (scenario: Scenario)
    (mouseWorld: WorldPosition)
    : ClickContext =

    if scenario.BlockMap.IsSome then
      let snap = projections.ComputeMovement3DSnapshot scenarioId

      {
        MouseWorld = mouseWorld
        Positions = snap.Positions
        Rotations = snap.Rotations
        ModelConfigIds = snap.ModelConfigIds
      }
    else
      let snap = projections.ComputeMovementSnapshot scenarioId

      {
        MouseWorld = mouseWorld
        Positions = snap.Positions
        Rotations = snap.Rotations
        ModelConfigIds = snap.ModelConfigIds
      }

  let inline private tryPickClickedEntity
    (cameraService: CameraService)
    (getPickBounds: string -> BoundingBox voption)
    (entityId: Guid<EntityId>)
    (scenario: Scenario)
    (rawMousePos: Vector2)
    (ctx: ClickContext)
    : Guid<EntityId> voption =
    cameraService.CreatePickRay(rawMousePos, entityId)
    |> ValueOption.bind(fun ray ->
      match scenario.BlockMap with
      | ValueSome blockMap ->
        EntityPicker.pickEntityBlockMap3D
          ray
          getPickBounds
          Constants.BlockMap3DPixelsPerUnit.X
          blockMap
          Constants.Entity.ModelScale
          ctx.Positions
          ctx.Rotations
          ctx.ModelConfigIds
          entityId
      | ValueNone -> ValueNone)

  let inline private publishMovement
    (eventBus: EventBus)
    (entityId: Guid<EntityId>)
    (mouseWorld: WorldPosition)
    =
    eventBus.Publish(
      GameEvent.Intent(
        IntentEvent.MovementTarget {
          EntityId = entityId
          Target = WorldPosition.toVector2 mouseWorld
        }
      )
    )

  let inline private publishAttack
    (eventBus: EventBus)
    (entityId: Guid<EntityId>)
    (targetId: Guid<EntityId>)
    =
    eventBus.Publish(
      GameEvent.Intent(
        IntentEvent.Attack {
          Attacker = entityId
          Target = targetId
        }
      )
    )

  let inline private publishTargetSelectionEntity
    (eventBus: EventBus)
    (entityId: Guid<EntityId>)
    (targetId: Guid<EntityId>)
    =
    eventBus.Publish(
      GameEvent.Intent(
        IntentEvent.TargetSelection {
          Selector = entityId
          Selection = SelectedEntity targetId
        }
      )
    )

  let inline private publishTargetSelectionPosition
    (eventBus: EventBus)
    (entityId: Guid<EntityId>)
    (mouseWorld: WorldPosition)
    =
    eventBus.Publish(
      GameEvent.Intent(
        IntentEvent.TargetSelection {
          Selector = entityId
          Selection = SelectedPosition(WorldPosition.toVector2 mouseWorld)
        }
      )
    )

  let inline private handlePrimaryClick
    (world: World)
    (eventBus: EventBus)
    (targetingService: TargetingService)
    (projections: Projections.ProjectionService)
    (cameraService: CameraService)
    (getPickBounds: string -> BoundingBox voption)
    (entityId: Guid<EntityId>)
    =
    let mouseState = tryGetMouseState world entityId

    let rawMousePos =
      Vector2(float32 mouseState.Position.X, float32 mouseState.Position.Y)

    tryGetMouseWorld cameraService entityId mouseState
    |> ValueOption.iter(fun mouseWorld ->
      let targetingMode = targetingService.TargetingMode |> AVal.force

      let entityScenarios = projections.EntityScenarios |> AMap.force
      let scenarios = world.Scenarios |> AMap.force

      let scenarioCtx =
        entityScenarios
        |> HashMap.tryFindV entityId
        |> ValueOption.bind(fun scenarioId ->
          scenarios
          |> HashMap.tryFindV scenarioId
          |> ValueOption.map(fun scenario ->
            struct (scenarioId,
                    scenario,
                    buildClickContext
                      projections
                      scenarioId
                      scenario
                      mouseWorld)))

      let picked =
        scenarioCtx
        |> ValueOption.bind(fun struct (_scenarioId, scenario, ctx) ->
          tryPickClickedEntity
            cameraService
            getPickBounds
            entityId
            scenario
            rawMousePos
            ctx)

      let mouseWorld =
        scenarioCtx
        |> ValueOption.map(fun struct (_scenarioId, _scenario, ctx) ->
          ctx.MouseWorld)
        |> ValueOption.defaultValue mouseWorld

      match targetingMode with
      | ValueNone ->
        match picked with
        | ValueSome targetId -> publishAttack eventBus entityId targetId
        | ValueNone -> publishMovement eventBus entityId mouseWorld
      | ValueSome Self -> ()
      | ValueSome TargetEntity ->
        picked
        |> ValueOption.iter(fun targetId ->
          publishTargetSelectionEntity eventBus entityId targetId)
      | ValueSome TargetPosition ->
        publishTargetSelectionPosition eventBus entityId mouseWorld
      | ValueSome TargetDirection ->
        publishTargetSelectionPosition eventBus entityId mouseWorld)


  let private handleActionSetChange
    (actionSetChangeState: ValueOption<struct (InputActionState * int)>)
    (publishChange: int -> unit)
    =
    match actionSetChangeState with
    | ValueSome(Pressed, value) -> publishChange value
    | _ -> ()

  let create
    (
      world: World,
      eventBus: EventBus,
      stateWrite: IStateWriteService,
      targetingService: TargetingService,
      projections: Projections.ProjectionService,
      cameraService: CameraService,
      getPickBounds: string -> BoundingBox voption,
      entityId: Guid<EntityId>
    ) =


    { new CoreEventListener with
        member _.StartListening() =
          eventBus.Observable
          |> Observable.choose(fun e ->
            match e with
            | GameEvent.State(Input(GameActionStatesChanged struct (changedEntityId,
                                                                    actionStates))) ->
              if changedEntityId = entityId then
                Some actionStates
              else
                None
            | _ -> None)
          |> Observable.subscribe
            (fun (actionStates: HashMap<GameAction, InputActionState>) ->
              let primaryActionState =
                actionStates |> HashMap.tryFindV PrimaryAction

              let actionSetChangeState =
                actionStates
                |> HashMap.tryFindV SetActionSet1
                |> ValueOption.map(fun v -> struct (v, 1))
                |> ValueOption.orElse(
                  actionStates
                  |> HashMap.tryFindV SetActionSet2
                  |> ValueOption.map(fun v -> struct (v, 2))
                )
                |> ValueOption.orElse(
                  actionStates
                  |> HashMap.tryFindV SetActionSet3
                  |> ValueOption.map(fun v -> struct (v, 3))
                )
                |> ValueOption.orElse(
                  actionStates
                  |> HashMap.tryFindV SetActionSet4
                  |> ValueOption.map(fun v -> struct (v, 4))
                )
                |> ValueOption.orElse(
                  actionStates
                  |> HashMap.tryFindV SetActionSet5
                  |> ValueOption.map(fun v -> struct (v, 5))
                )
                |> ValueOption.orElse(
                  actionStates
                  |> HashMap.tryFindV SetActionSet6
                  |> ValueOption.map(fun v -> struct (v, 6))
                )
                |> ValueOption.orElse(
                  actionStates
                  |> HashMap.tryFindV SetActionSet7
                  |> ValueOption.map(fun v -> struct (v, 7))
                )
                |> ValueOption.orElse(
                  actionStates
                  |> HashMap.tryFindV SetActionSet8
                  |> ValueOption.map(fun v -> struct (v, 8))
                )

              handleActionSetChange actionSetChangeState (fun value ->
                stateWrite.UpdateActiveActionSet(entityId, value)

                eventBus.Publish(
                  GameEvent.State(
                    Input(ActiveActionSetChanged struct (entityId, value))
                  )
                ))

              match primaryActionState with
              | ValueSome Pressed ->
                handlePrimaryClick
                  world
                  eventBus
                  targetingService
                  projections
                  cameraService
                  getPickBounds
                  entityId
              | ValueSome _
              | ValueNone -> ())
    }

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
  open Pomo.Core.Domain.Entity
  open System.Collections.Generic

  let private findHoveredEntity
    (world: World)
    (positions: IReadOnlyDictionary<Guid<EntityId>, WorldPosition>)
    (rotations: IReadOnlyDictionary<Guid<EntityId>, float32>)
    (cameraService: CameraService)
    (playerId: Guid<EntityId>)
    =
    let mouseStateOpt =
      world.RawInputStates
      |> AMap.tryFind playerId
      |> AVal.force
      |> Option.map _.Mouse

    match mouseStateOpt with
    | Some ms ->
      let rawPos = Vector2(float32 ms.Position.X, float32 ms.Position.Y)

      match cameraService.CreatePickRay(rawPos, playerId) with
      | ValueSome ray ->
        let entityScenarios = world.EntityScenario |> AMap.force
        let scenarios = world.Scenarios |> AMap.force

        let pixelsPerUnit =
          match entityScenarios |> HashMap.tryFindV playerId with
          | ValueSome scenarioId ->
            match scenarios |> HashMap.tryFindV scenarioId with
            | ValueSome s ->
              match s.Map with
              | ValueSome map ->
                Vector2(float32 map.TileWidth, float32 map.TileHeight)
              | ValueNone -> Vector2(64f, 64f) // BlockMap scenario
            | ValueNone -> Constants.DefaultPixelsPerUnit
          | ValueNone -> Constants.DefaultPixelsPerUnit

        let squishFactor = pixelsPerUnit.X / pixelsPerUnit.Y
        let modelScale = Constants.Entity.ModelScale

        EntityPicker.pickEntity
          ray
          pixelsPerUnit
          modelScale
          squishFactor
          positions
          rotations
          playerId
        |> ValueOption.map Some
        |> ValueOption.defaultValue None

      | ValueNone -> None
    | None -> None


  let private handleActionSetChange
    (actionSetChangeState: ValueOption<struct (InputActionState * int)>)
    (publishChange: int -> unit)
    =
    match actionSetChangeState with
    | ValueSome(Pressed, value) -> publishChange value
    | _ -> ()

  open Pomo.Core.Environment.Patterns

  let create
    (
      world: World,
      eventBus: EventBus,
      stateWrite: IStateWriteService,
      targetingService: TargetingService,
      projections: Projections.ProjectionService,
      cameraService: CameraService,
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
                let mouseState =
                  world.RawInputStates
                  |> AMap.tryFind entityId
                  |> AVal.map(Option.map _.Mouse)
                  |> AVal.force
                  |> Option.defaultWith(fun () -> Mouse.GetState())

                let mousePosition =
                  let rawMousePos =
                    Vector2(
                      float32 mouseState.Position.X,
                      float32 mouseState.Position.Y
                    )

                  match cameraService.ScreenToWorld(rawMousePos, entityId) with
                  | ValueSome worldPos -> worldPos
                  | ValueNone -> WorldPosition.fromVector2 rawMousePos // Fallback if no camera found (shouldn't happen for local player)

                let targetingMode =
                  targetingService.TargetingMode |> AVal.force

                // Get the entity under the cursor AT THIS MOMENT by forcing the projection.
                let entityScenarios = projections.EntityScenarios |> AMap.force

                let positions, rotations =
                  match entityScenarios |> HashMap.tryFindV entityId with
                  | ValueSome scenarioId ->
                    let snapshot =
                      projections.ComputeMovementSnapshot(scenarioId)

                    snapshot.Positions, snapshot.Rotations
                  | ValueNone -> Dictionary(), Dictionary()

                let clickedEntity =
                  findHoveredEntity
                    world
                    positions
                    rotations
                    cameraService
                    entityId


                match targetingMode with
                | ValueNone ->
                  // NOT targeting: This is a click to move or attack
                  match clickedEntity with
                  | Some clickedEntityId ->
                    // An entity was clicked, publish an attack intent
                    eventBus.Publish(
                      GameEvent.Intent(
                        IntentEvent.Attack {
                          Attacker = entityId
                          Target = clickedEntityId
                        }
                      )
                    )
                  | None ->
                    // Nothing was clicked, publish a movement command
                    eventBus.Publish(
                      GameEvent.Intent(
                        IntentEvent.MovementTarget {
                          EntityId = entityId
                          Target = WorldPosition.toVector2 mousePosition
                        }
                      )
                    )

                | ValueSome Self ->
                  // This case should be handled immediately on key press, not click.
                  ()

                | ValueSome TargetEntity ->
                  match clickedEntity with
                  | Some clickedEntityId ->
                    // TODO: Validate if it's an ally/enemy
                    let selection = SelectedEntity clickedEntityId

                    eventBus.Publish(
                      GameEvent.Intent(
                        IntentEvent.TargetSelection {
                          Selector = entityId
                          Selection = selection
                        }
                      )
                    )
                  | None ->
                    // Invalid target, do nothing for now
                    ()

                | ValueSome TargetPosition ->
                  let selection =
                    SelectedPosition(WorldPosition.toVector2 mousePosition)

                  eventBus.Publish(
                    GameEvent.Intent(
                      IntentEvent.TargetSelection {
                        Selector = entityId
                        Selection = selection
                      }
                    )
                  )
                | ValueSome TargetDirection ->
                  let selection =
                    SelectedPosition(WorldPosition.toVector2 mousePosition)

                  eventBus.Publish(
                    GameEvent.Intent(
                      IntentEvent.TargetSelection {
                        Selector = entityId
                        Selection = selection
                      }
                    )
                  )
              | ValueSome _
              | ValueNone -> ())
    }

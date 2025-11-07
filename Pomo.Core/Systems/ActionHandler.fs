namespace Pomo.Core.Domains

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.EventBus
open Pomo.Core.Domains.Targeting
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Action

module ActionHandler =
  open Microsoft.Xna.Framework.Input


  type ActionHandlerService = interface end

  // TODO: This is a placeholder. A real implementation would need to check
  // entity bounding boxes against the click position.
  let findEntityAtPosition
    (world: World)
    (position: Vector2)
    : Guid<EntityId> voption =
    ValueNone

  type ActionHandler
    (
      world: World,
      eventBus: EventBus,
      targetingService: TargetingService,
      entityId: Guid<EntityId>
    ) =


    member _.ListenToEvents() =
      eventBus
      |> Observable.filter(fun ev ->
        match ev with
        | GameActionStatesChanged struct (changedEntityId, _) ->
          changedEntityId = entityId
        | _ -> false)
      |> Observable.subscribe(fun ev ->
        match ev with
        | GameActionStatesChanged struct (changedEntityId, actionStates) ->
          let primaryActionState =
            actionStates |> HashMap.tryFindV PrimaryAction

          match primaryActionState with
          | ValueSome Pressed ->
            let mouseState =
              world.RawInputStates
              |> AMap.tryFind entityId
              |> AVal.map(Option.map _.Mouse)
              |> AVal.force
              |> Option.defaultWith(fun () -> Mouse.GetState())

            let mousePosition =
              Vector2(
                float32 mouseState.Position.X,
                float32 mouseState.Position.Y
              )

            let targetingMode = targetingService.TargetingMode |> AVal.force

            match targetingMode with
            | ValueNone ->
              // NOT targeting: This is a click to move or attack
              let clickedEntity = findEntityAtPosition world mousePosition

              match clickedEntity with
              | ValueSome clickedEntityId ->
                // An entity was clicked, publish an attack intent
                eventBus.Publish(AttackIntent(entityId, clickedEntityId))
              | ValueNone ->
                // Nothing was clicked, publish a movement command
                eventBus.Publish(SetMovementTarget(entityId, mousePosition))

            | ValueSome Self ->
              // This case should be handled immediately on key press, not click.
              ()

            | ValueSome SingleAlly
            | ValueSome SingleEnemy ->
              let clickedEntity = findEntityAtPosition world mousePosition

              match clickedEntity with
              | ValueSome clickedEntityId ->
                // TODO: Validate if it's an ally/enemy
                let selection = SelectedEntity clickedEntityId
                eventBus.Publish(TargetSelected(entityId, selection))
              | ValueNone ->
                // Invalid target, do nothing for now
                ()

            | ValueSome GroundPoint ->
              let selection = SelectedPosition mousePosition
              eventBus.Publish(TargetSelected(entityId, selection))
            | ValueSome(GroundArea area) ->
              match area with
              | Circle _
              | Rectangle _
              | Cone _
              | Square _ ->
                let selection = SelectedPosition mousePosition
                eventBus.Publish(TargetSelected(entityId, selection))

          | ValueSome _
          | ValueNone -> ()
        | _ -> ())

    interface ActionHandlerService

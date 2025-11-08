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


  type ActionHandlerService =
    abstract member StartListening: unit -> IDisposable

  let private createHoveredEntityProjection
    (world: World)
    (playerId: Guid<EntityId>)
    =
    adaptive {
      let! mousePositionAval =
        world.RawInputStates
        |> AMap.tryFind playerId
        |> AVal.map(
          Option.map(fun state ->
            let ms = state.Mouse
            Vector2(float32 ms.Position.X, float32 ms.Position.Y))
        )

      return!
        world.Positions
        |> AMap.fold
          (fun (acc: _ option) entityId entityPos ->
            if acc.IsSome then
              acc
            else
              mousePositionAval
              |> Option.bind(fun mousePos ->
                if entityId = playerId then
                  None
                else
                  let entitySize = Vector2(32.0f, 32.0f)

                  let entityBounds =
                    Microsoft.Xna.Framework.Rectangle(
                      int entityPos.X,
                      int entityPos.Y,
                      int entitySize.X,
                      int entitySize.Y
                    )

                  if entityBounds.Contains mousePos then
                    Some entityId
                  else
                    None))
          None
    }

  let create
    (
      world: World,
      eventBus: EventBus,
      targetingService: TargetingService,
      entityId: Guid<EntityId>
    ) =
    let hoveredEntityAval = createHoveredEntityProjection world entityId

    { new ActionHandlerService with
        member _.StartListening() =
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

                let targetingMode =
                  targetingService.TargetingMode |> AVal.force

                // Get the entity under the cursor AT THIS MOMENT by forcing the projection.
                let clickedEntity = hoveredEntityAval |> AVal.force

                match targetingMode with
                | ValueNone ->
                  // NOT targeting: This is a click to move or attack
                  match clickedEntity with
                  | Some clickedEntityId ->
                    // An entity was clicked, publish an attack intent
                    eventBus.Publish(AttackIntent(entityId, clickedEntityId))
                  | None ->
                    // Nothing was clicked, publish a movement command
                    eventBus.Publish(
                      SetMovementTarget(entityId, mousePosition)
                    )

                | ValueSome Self ->
                  // This case should be handled immediately on key press, not click.
                  ()

                | ValueSome SingleAlly
                | ValueSome SingleEnemy ->
                  match clickedEntity with
                  | Some clickedEntityId ->
                    // TODO: Validate if it's an ally/enemy
                    let selection = SelectedEntity clickedEntityId
                    eventBus.Publish(TargetSelected(entityId, selection))
                  | None ->
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
    }

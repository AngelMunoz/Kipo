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
          eventBus.GetObservableFor<StateChangeEvent>()
          |> Observable.choose(fun event ->
            match event with
            | StateChangeEvent.Input(InputEvents.GameActionStatesChanged(struct (changedEntityId,
                                                                                 actionStates))) ->
              if changedEntityId = entityId then
                Some actionStates
              else
                None
            | _ -> None)
          |> Observable.subscribe(fun actionStates ->
            let primaryActionState =
              actionStates |> HashMap.tryFindV GameAction.PrimaryAction

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

              // Get the entity under the cursor AT THIS MOMENT by forcing the projection.
              let clickedEntity = hoveredEntityAval |> AVal.force

              match targetingMode with
              | ValueNone ->
                // NOT targeting: This is a click to move or attack
                match clickedEntity with
                | Some clickedEntityId ->
                  // An entity was clicked, publish an attack intent
                  eventBus.Publish(
                    {
                      Attacker = entityId
                      Target = clickedEntityId
                    }
                    : SystemCommunications.AttackIntent
                  )
                | None ->
                  // Nothing was clicked, publish a movement command
                  eventBus.Publish(
                    {
                      EntityId = entityId
                      Target = mousePosition
                    }
                    : SystemCommunications.SetMovementTarget
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

                  eventBus.Publish(
                    {
                      Selector = entityId
                      Selection = selection
                    }
                    : SystemCommunications.TargetSelected
                  )
                | None ->
                  // Invalid target, do nothing for now
                  ()

              | ValueSome GroundPoint ->
                let selection = SelectedPosition mousePosition

                eventBus.Publish(
                  {
                    Selector = entityId
                    Selection = selection
                  }
                  : SystemCommunications.TargetSelected
                )
              | ValueSome(GroundArea area) ->
                match area with
                | Circle _
                | Rectangle _
                | Cone _
                | Square _ ->
                  let selection = SelectedPosition mousePosition

                  eventBus.Publish(
                    {
                      Selector = entityId
                      Selection = selection
                    }
                    : SystemCommunications.TargetSelected
                  )

            | ValueSome _
            | ValueNone -> ())
    }

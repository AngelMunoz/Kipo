namespace Pomo.Core.Systems

open System.Reactive.Disposables
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.EventBus
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.RawInput


module Targeting =
  open Pomo.Core.Domain.World
  open Pomo.Core.Domain.Skill
  open Pomo.Core.Stores
  open Pomo.Core.Domain.Action
  open Pomo.Core.Domain.Core

  let private SKILL_ACTIVATION_RANGE_BUFFER = 5.0f

  type TargetingService =
    inherit CoreEventListener
    abstract member TargetingMode: Targeting voption aval with get

  let skillByEntityAndAction
    (entityId: _ aval)
    (action: GameAction voption aval)
    (skillStore: SkillStore)
    (actionSets: amap<Guid<EntityId>, HashMap<GameAction, SlotProcessing>>)
    =
    adaptive {
      let! entityId = entityId
      and! action = action

      match entityId, action with
      | ValueNone, _
      | ValueSome _, ValueNone -> return ValueNone
      | ValueSome entityId, ValueSome action ->
        let! found = actionSets |> AMap.tryFind entityId

        match found with
        | None -> return ValueNone
        | Some actionSet ->
          return
            actionSet
            |> HashMap.tryFindV action
            |> ValueOption.bind(fun slotProcessing ->
              match slotProcessing with
              | Skill skillId -> ValueSome skillId
              | Item _ -> ValueNone)
            |> ValueOption.bind(fun skillId -> skillStore.tryFind skillId)
    }

  let inline detectEscape ([<InlineIfLambda>] onEscape) targetingMode rawInput =
    match targetingMode with
    | ValueSome _ ->
      let isEscapePressed =
        rawInput.Keyboard.IsKeyDown(Keys.Escape)
        && rawInput.PrevKeyboard.IsKeyUp(Keys.Escape)

      let isRightMouseClicked =
        rawInput.Mouse.RightButton = ButtonState.Pressed
        && rawInput.PrevMouse.RightButton = ButtonState.Released

      if isEscapePressed || isRightMouseClicked then
        onEscape()
    | ValueNone -> ()

  [<Struct>]
  type HandleSelectedTargetArgs = {
    eventBus: EventBus
    skillBeingTargeted: Skill voption aval
    currentAction:
      struct (cval<Guid<EntityId> voption> * cval<GameAction voption>)
    positions: amap<Guid<EntityId>, Vector2>
  }

  module private TargetingHandlers =

    let private getSkillActivationPositions
      (positions: HashMap<Guid<EntityId>, Vector2>)
      (casterId: Guid<EntityId>)
      (targetId: Guid<EntityId>)
      =

      match positions.TryFindV casterId, positions.TryFindV targetId with
      | ValueSome casterPos, ValueSome targetPos ->
        ValueSome(casterPos, targetPos)
      | _ -> ValueNone

    let handleTargetEntity
      (eventBus: EventBus)
      (positions: HashMap<Guid<EntityId>, Vector2>)
      (skill: ActiveSkill)
      (casterId: Guid<EntityId>)
      (targetId: Guid<EntityId>)
      =
      match getSkillActivationPositions positions casterId targetId with
      | ValueNone -> () // Or log an error that positions were not found
      | ValueSome(casterPos, targetPos) ->
        let distance = Vector2.Distance(casterPos, targetPos)
        let maxRange = skill.Range |> ValueOption.defaultValue 0f

        if distance > maxRange then
          let direction = Vector2.Normalize(targetPos - casterPos)

          let moveTarget =
            targetPos - direction * (maxRange - SKILL_ACTIVATION_RANGE_BUFFER)

          eventBus.Publish(
            {
              EntityId = casterId
              Target = moveTarget
            }
            : SystemCommunications.SetMovementTarget
          )

          eventBus.Publish(
            Combat(
              PendingSkillCastSet(
                casterId,
                skill.Id,
                SystemCommunications.TargetEntity targetId
              )
            )
          )
        else
          eventBus.Publish(
            {
              Caster = casterId
              SkillId = skill.Id
              Target = SystemCommunications.TargetEntity targetId
            }
            : SystemCommunications.AbilityIntent
          )

    let handleTargetDirection
      (eventBus: EventBus)
      (positions: HashMap<Guid<EntityId>, Vector2>)
      (skill: ActiveSkill)
      (casterId: Guid<EntityId>)
      (targetPos: Vector2)
      =

      match positions.TryFindV casterId with
      | ValueNone -> () // Caster position not found, do nothing.
      | ValueSome casterPos ->
        // For TargetDirection, we don't check range for movement,
        // as the skill originates from the caster.
        // We just fire the intent immediately.
        eventBus.Publish(
          {
            Caster = casterId
            SkillId = skill.Id
            Target = SystemCommunications.TargetDirection targetPos
          }
          : SystemCommunications.AbilityIntent
        )

    let handleTargetPosition
      (eventBus: EventBus)
      (positions: HashMap<Guid<EntityId>, Vector2>)
      (skill: ActiveSkill)
      (casterId: Guid<EntityId>)
      (targetPos: Vector2)
      =

      match positions.TryFindV casterId with
      | ValueNone -> () // Caster position not found, do nothing.
      | ValueSome casterPos ->
        let distance = Vector2.Distance(casterPos, targetPos)
        let maxRange = skill.Range |> ValueOption.defaultValue 0f

        if distance > maxRange then
          let direction = Vector2.Normalize(targetPos - casterPos)

          let moveTarget =
            targetPos - direction * (maxRange - SKILL_ACTIVATION_RANGE_BUFFER)

          eventBus.Publish(
            {
              EntityId = casterId
              Target = moveTarget
            }
            : SystemCommunications.SetMovementTarget
          )

          eventBus.Publish(
            StateChangeEvent.Combat(
              CombatEvents.PendingSkillCastSet(
                casterId,
                skill.Id,
                SystemCommunications.TargetPosition targetPos
              )
            )
          )
        else
          eventBus.Publish(
            {
              Caster = casterId
              SkillId = skill.Id
              Target = SystemCommunications.TargetPosition targetPos
            }
            : SystemCommunications.AbilityIntent
          )

  let handleTargetSelected
    (args: HandleSelectedTargetArgs)
    (currentSelection: struct (Guid<EntityId> * Selection))
    =
    let {
          eventBus = eventBus
          skillBeingTargeted = skillBeingTargeted
          currentAction = _entityId, _action
          positions = positions
        } =
      args

    let positions = positions |> AMap.force

    let struct (selector, selection) = currentSelection

    match _entityId.Value with
    | ValueSome casterId ->
      if casterId = selector then
        let skillOpt = skillBeingTargeted |> AVal.force

        match skillOpt with
        | ValueSome(Active activeSkill) ->
          match activeSkill.Targeting, selection with
          | TargetEntity, SelectedEntity targetId ->
            TargetingHandlers.handleTargetEntity
              eventBus
              positions
              activeSkill
              casterId
              targetId
          | TargetPosition, SelectedPosition targetPos ->
            TargetingHandlers.handleTargetPosition
              eventBus
              positions
              activeSkill
              casterId
              targetPos
          | TargetDirection, SelectedPosition targetPos ->
            TargetingHandlers.handleTargetDirection
              eventBus
              positions
              activeSkill
              casterId
              targetPos
          | _ -> () // Invalid selection for the targeting mode

          // Always clear targeting mode after a selection is made
          transact(fun () ->
            _action.Value <- ValueNone
            _entityId.Value <- ValueNone)
        | _ ->
          // Skill not active or passive, clear targeting mode
          transact(fun () ->
            _action.Value <- ValueNone
            _entityId.Value <- ValueNone)
      else
        () // Event selector was not the active caster
    | ValueNone -> () // No active caster

  let create
    (
      world: World,
      eventBus: EventBus,
      skillStore: SkillStore,
      projections: Projections.ProjectionService
    ) =
    let _entityId = cval ValueNone
    let _action = cval ValueNone

    let skillBeingTargeted =
      skillByEntityAndAction _entityId _action skillStore projections.ActionSets


    let targetingMode =
      skillBeingTargeted
      |> AVal.map(
        ValueOption.bind (function
          | Active skill -> ValueSome skill.Targeting
          | Passive _ -> ValueNone)
      )

    let handleSelected =
      handleTargetSelected {
        eventBus = eventBus
        skillBeingTargeted = skillBeingTargeted
        currentAction = struct (_entityId, _action)
        positions = projections.UpdatedPositions
      }



    { new TargetingService with
        member _.TargetingMode = targetingMode

        member _.StartListening() =
          let sub1 =
            eventBus.GetObservableFor<SystemCommunications.SlotActivated>()
            |> Observable.subscribe(fun event ->
              transact(fun () ->
                _action.Value <- ValueSome event.Slot
                _entityId.Value <- ValueSome event.CasterId))

          let sub2 =
            eventBus.GetObservableFor<SystemCommunications.TargetSelected>()
            |> Observable.subscribe(fun event ->
              handleSelected(event.Selector, event.Selection))

          let sub3 =
            eventBus.GetObservableFor<StateChangeEvent>()
            |> Observable.choose(fun event ->
              match event with
              | Input(RawStateChanged struct (_, rawInput)) -> Some rawInput
              | _ -> None)
            |> Observable.subscribe(fun rawInput ->
              let currentTargetingMode = targetingMode |> AVal.force

              let onEscape() =
                transact(fun () ->
                  _action.Value <- ValueNone
                  _entityId.Value <- ValueNone)

              rawInput |> detectEscape onEscape currentTargetingMode)

          new CompositeDisposable([ sub1; sub2; sub3 ])
    }

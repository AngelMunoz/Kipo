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

  let private SKILL_ACTIVATION_RANGE_BUFFER = 5.0f

  type TargetingService =
    inherit System.IDisposable
    abstract member TargetingMode: Targeting voption aval with get

  let skillByEntityAndAction
    (entityId: _ aval)
    (action: GameAction voption aval)
    (skillStore: SkillStore)
    (quickSlots: amap<_, HashMap<GameAction, _>>)
    =
    adaptive {
      let! entityId = entityId
      and! action = action

      match entityId, action with
      | ValueNone, _
      | ValueSome _, ValueNone -> return ValueNone
      | ValueSome entityId, ValueSome action ->
        let! found = quickSlots |> AMap.tryFind entityId

        match found with
        | None -> return ValueNone
        | Some quickSlots ->
          return
            quickSlots
            |> HashMap.tryFindV action
            |> ValueOption.map(fun skillId -> skillStore.tryFind skillId)
            |> ValueOption.flatten
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
    world: World
  }

  let private getSkillActivationPositions
    (world: World)
    (casterId: Guid<EntityId>)
    (targetId: Guid<EntityId>)
    =
    let positions = world.Positions |> AMap.force

    match positions.TryFindV casterId, positions.TryFindV targetId with
    | ValueSome casterPos, ValueSome targetPos ->
      ValueSome(casterPos, targetPos)
    | _ -> ValueNone

  let private checkRangeAndPublish
    (eventBus: EventBus)
    (world: World)
    (skill: ActiveSkill)
    (casterId: Guid<EntityId>)
    (targetId: Guid<EntityId>)
    =
    match getSkillActivationPositions world casterId targetId with
    | ValueNone -> () // Or log an error that positions were not found
    | ValueSome(casterPos, targetPos) ->
      let distance = Vector2.Distance(casterPos, targetPos)
      // Assuming the first element in Range is the max range
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
            CombatEvents.PendingSkillCastSet(casterId, skill.Id, targetId)
          )
        )
      else
        eventBus.Publish(
          {
            Caster = casterId
            SkillId = skill.Id
            Target = ValueSome targetId
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
          world = world
        } =
      args

    let struct (selector, selection) = currentSelection

    match _entityId.Value with
    | ValueSome casterId ->
      if casterId = selector then
        let targetIdOpt =
          match selection with
          | SelectedEntity entity -> Some entity
          | SelectedPosition _ -> None

        let skillOpt = skillBeingTargeted |> AVal.force

        match skillOpt, targetIdOpt with
        | ValueSome(Active activeSkill), Some targetId ->
          checkRangeAndPublish eventBus world activeSkill casterId targetId

          // Always clear targeting mode after a selection is made
          transact(fun () ->
            _action.Value <- ValueNone
            _entityId.Value <- ValueNone)
        | _ ->
          // Invalid selection, clear targeting mode
          transact(fun () ->
            _action.Value <- ValueNone
            _entityId.Value <- ValueNone)
      else
        () // Event selector was not the active caster
    | ValueNone -> () // No active caster

  let create(world: World, eventBus: EventBus, skillStore: SkillStore) =
    let _entityId = cval ValueNone
    let _action = cval ValueNone

    let skillBeingTargeted =
      skillByEntityAndAction _entityId _action skillStore world.QuickSlots

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
        world = world
      }

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
        | StateChangeEvent.Input(InputEvents.RawStateChanged(struct (_,
                                                                     rawInput))) ->
          Some rawInput
        | _ -> None)
      |> Observable.subscribe(fun rawInput ->
        let currentTargetingMode = targetingMode |> AVal.force

        let onEscape() =
          transact(fun () ->
            _action.Value <- ValueNone
            _entityId.Value <- ValueNone)

        rawInput |> detectEscape onEscape currentTargetingMode)

    let disposable = new CompositeDisposable([ sub1; sub2; sub3 ])

    { new TargetingService with
        member _.TargetingMode = targetingMode
        member _.Dispose() = disposable.Dispose()
    }

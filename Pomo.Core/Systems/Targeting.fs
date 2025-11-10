namespace Pomo.Core.Systems

open System.Reactive.Disposables
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
  }

  let handleTargetSelected
    (args: HandleSelectedTargetArgs)
    (currentSelection: struct (Guid<EntityId> * Selection))
    =
    let {
          eventBus = eventBus
          skillBeingTargeted = skillBeingTargeted
          currentAction = _entityId, _action
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
          eventBus.Publish(
            {
              Caster = selector
              SkillId = activeSkill.Id
              Target = ValueSome targetId
            }
            : SystemCommunications.AbilityIntent
          )

          transact(fun () ->
            _action.Value <- ValueNone
            _entityId.Value <- ValueNone)
        | ValueSome(Active _), None -> () // No target selected for an active skill
        | ValueSome(Passive _), _ -> () // A passive skill was somehow targeted
        | ValueNone, _ -> () // The skill could not be found
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

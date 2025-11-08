namespace Pomo.Core.Domains

open Microsoft.Xna.Framework.Input
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain.Units
open Pomo.Core.Domain.RawInput


module Targeting =
  open Pomo.Core.Domain.World
  open Pomo.Core.Domain.EventBus
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
            AbilityIntent(selector, activeSkill.Id, ValueSome targetId)
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

  let private handleEvent
    (handleTargetSelected: struct (Guid<EntityId> * Selection) -> unit)
    (_action: _ cval)
    (_entityId: _ cval)
    (targetingMode: _ aval)
    (event: WorldEvent)
    =
    match event with
    | SlotActivated(action, entityId) ->
      transact(fun () ->
        _action.Value <- ValueSome action
        _entityId.Value <- ValueSome entityId)
    | TargetSelected(selector, selection) ->
      handleTargetSelected(selector, selection)
    | RawInputStateChanged struct (_, rawInput) ->
      let currentTargetingMode = targetingMode |> AVal.force

      let onEscape() =
        transact(fun () ->
          _action.Value <- ValueNone
          _entityId.Value <- ValueNone)

      rawInput |> detectEscape onEscape currentTargetingMode
    | _ -> ()

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

    let injectedHandler =
      handleEvent handleSelected _action _entityId targetingMode

    let sub = eventBus |> Observable.subscribe injectedHandler

    { new TargetingService with
        member _.TargetingMode = targetingMode
        member _.Dispose() = sub.Dispose()

    }

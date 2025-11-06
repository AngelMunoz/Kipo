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

  let create(world: World, eventBus: EventBus, skillStore: SkillStore) =
    let _entityId = cval ValueNone
    let _action = cval ValueNone

    let targetingMode = adaptive {
      let! quickSlots =
        skillByEntityAndAction _entityId _action skillStore world.QuickSlots

      match quickSlots with
      | ValueNone
      | ValueSome(Passive _) -> return ValueNone
      | ValueSome(Active skillOpt) -> return ValueSome skillOpt.Targeting
    }

    eventBus
    |> Observable.add(fun event ->
      match event with
      | SlotActivated(action, entityId) ->
        transact(fun () ->
          _action.Value <- ValueSome action
          _entityId.Value <- ValueSome entityId)
      | RawInputStateChanged struct (_, rawInput) ->
        let targetingMode = targetingMode |> AVal.force

        let inline onEscape() =
          transact(fun () ->
            _action.Value <- ValueNone
            _entityId.Value <- ValueNone)

        rawInput |> detectEscape onEscape targetingMode
      | _ -> ())


    { new TargetingService with
        member _.TargetingMode = targetingMode
    }

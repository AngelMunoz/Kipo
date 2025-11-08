namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Stores
open Pomo.Core.Domain
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World

module AbilityActivation =

  type ValidationError =
    | NotEnoughResources
    | OnCooldown
    | SkillNotFound
    | CannotActivatePassiveSkill

  module private Validation =

    let private checkResources
      (world: World)
      (casterId: Guid<EntityId>)
      (skill: ActiveSkill)
      =
      adaptive {
        let! resources = world.Resources |> AMap.tryFind casterId

        match skill.Cost with
        | ValueNone -> return Ok()
        | ValueSome cost ->
          match resources with
          | None -> return Error NotEnoughResources // Should not happen for a live entity
          | Some res ->
            let requiredAmount = cost.Amount |> ValueOption.defaultValue 0

            let hasEnough =
              match cost.ResourceType with
              | Entity.ResourceType.HP -> res.HP >= requiredAmount
              | Entity.ResourceType.MP -> res.MP >= requiredAmount

            if hasEnough then
              return Ok()
            else
              return Error NotEnoughResources
      }

    let private checkCooldown
      (world: World)
      (gameTime: TimeSpan)
      (casterId: Guid<EntityId>)
      (skillId: int<SkillId>)
      =
      adaptive {
        let! abilityCooldowns = world.AbilityCooldowns |> AMap.tryFind casterId

        match abilityCooldowns with
        | None -> return Ok()
        | Some cdMap ->
          match cdMap.TryFindV skillId with
          | ValueNone -> return Ok()
          | ValueSome readyTime ->
            if gameTime >= readyTime then
              return Ok()
            else
              return Error OnCooldown
      }

    let validate
      (world: World)
      (gameTime: TimeSpan)
      (skillStore: SkillStore)
      (casterId: Guid<EntityId>)
      (skillId: int<SkillId>)
      =
      adaptive {
        match skillStore.tryFind skillId with
        | ValueNone -> return Error SkillNotFound
        | ValueSome(Passive _) -> return Error CannotActivatePassiveSkill
        | ValueSome(Active activeSkill) ->
          let! value = checkResources world casterId activeSkill

          match value with
          | Error e -> return Error e
          | Ok() ->
            let! value = checkCooldown world gameTime casterId skillId

            match value with
            | Error e -> return Error e
            | Ok() -> return Ok()
      }

  type AbilityActivationSystem(game: Game, playerId: Guid<EntityId>) as this =
    inherit GameSystem(game)

    let skillStore = this.Game.Services.GetService<SkillStore>()

    let actionStates =
      this.World.GameActionStates
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue HashMap.empty)
      |> AMap.ofAVal

    let pressedActions =
      actionStates
      |> AMap.choose(fun action states ->
        match states with
        | Pressed -> Some action
        | _ -> None)
      |> AMap.toASetValues

    let quickSlots =
      this.World.QuickSlots
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue HashMap.empty)

    override this.Update gameTime =
      let actions = pressedActions |> ASet.force
      let slots = quickSlots |> AVal.force

      for action in actions do
        match action with
        | UseSlot1
        | UseSlot2
        | UseSlot3
        | UseSlot4
        | UseSlot5
        | UseSlot6
        | UseSlot7
        | UseSlot8 ->
          match slots.TryFind action with
          | Some skillId ->
            let validationResult =
              Validation.validate
                this.World
                gameTime.TotalGameTime
                skillStore
                playerId
                skillId
              |> AVal.force

            match validationResult with
            | Ok() -> this.EventBus.Publish(SlotActivated(action, playerId))
            | Error msg ->
              let casterPos =
                this.World.Positions
                |> AMap.tryFind playerId
                |> AVal.force
                |> Option.defaultWith(fun () -> Vector2.Zero)

              this.EventBus.Publish(ShowNotification($"{msg}", casterPos))
          | None -> () // Slot is empty
        | _ -> ()

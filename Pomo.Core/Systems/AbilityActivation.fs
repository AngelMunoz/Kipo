namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Stores
open Pomo.Core.Domain
open Pomo.Core.EventBus
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World

module AbilityActivation =
  open Pomo.Core

  type ValidationError =
    | NotEnoughResources
    | OnCooldown
    | SkillNotFound
    | CannotActivatePassiveSkill
    | Stunned
    | Silenced

  module private Validation =

    let private checkStatusEffects
      (statuses: CombatStatus IndexList)
      (skill: ActiveSkill)
      : Result<unit, ValidationError> =
      if statuses |> IndexList.exists(fun _ s -> s.IsStunned) then
        Error Stunned
      else
        let isSilenced = statuses |> IndexList.exists(fun _ s -> s.IsSilenced)

        let isMpSkill =
          match skill.Cost with
          | ValueSome cost when cost.ResourceType = Entity.ResourceType.MP ->
            true
          | _ -> false

        if isSilenced && isMpSkill then Error Silenced else Ok()

    let private checkResources
      (resources: Entity.Resource option)
      (skill: ActiveSkill)
      : Result<unit, ValidationError> =
      match skill.Cost with
      | ValueNone -> Ok()
      | ValueSome cost ->
        match resources with
        | None -> Error NotEnoughResources // Should not happen for a live entity
        | Some res ->
          let requiredAmount = cost.Amount |> ValueOption.defaultValue 0

          let hasEnough =
            match cost.ResourceType with
            | Entity.ResourceType.HP -> res.HP >= requiredAmount
            | Entity.ResourceType.MP -> res.MP >= requiredAmount

          if hasEnough then Ok() else Error NotEnoughResources

    let private checkCooldown
      (cooldowns: HashMap<int<SkillId>, TimeSpan> option)
      (gameTime: TimeSpan)
      (skillId: int<SkillId>)
      : Result<unit, ValidationError> =
      match cooldowns with
      | None -> Ok()
      | Some cdMap ->
        match cdMap.TryFindV skillId with
        | ValueNone -> Ok()
        | ValueSome readyTime ->
          if gameTime >= readyTime then Ok() else Error OnCooldown

    let validate
      (skillStore: SkillStore)
      (statuses: CombatStatus IndexList)
      (resources: Entity.Resource option)
      (cooldowns: HashMap<int<SkillId>, TimeSpan> option)
      (gameTime: TimeSpan)
      (skillId: int<SkillId>)
      : Result<unit, ValidationError> =
      match skillStore.tryFind skillId with
      | ValueNone -> Error SkillNotFound
      | ValueSome(Passive _) -> Error CannotActivatePassiveSkill
      | ValueSome(Active activeSkill) ->
        checkStatusEffects statuses activeSkill
        |> Result.bind(fun () -> checkResources resources activeSkill)
        |> Result.bind(fun () -> checkCooldown cooldowns gameTime skillId)

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

    let playerCombatStatuses =
      Projections.CalculateCombatStatuses this.World
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue IndexList.empty)

    let playerResources = this.World.Resources |> AMap.tryFind playerId

    let playerCooldowns = this.World.AbilityCooldowns |> AMap.tryFind playerId

    let playerPosition =
      Projections.UpdatedPositions this.World
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue Vector2.Zero)

    override this.Update gameTime =
      let actions = pressedActions |> ASet.force
      let slots = quickSlots |> AVal.force
      let statuses = playerCombatStatuses |> AVal.force
      let resources = playerResources |> AVal.force
      let cooldowns = playerCooldowns |> AVal.force

      let publishNotification(msg: string) =
        let casterPos = playerPosition |> AVal.force

        this.EventBus.Publish<SystemCommunications.ShowNotification> {
          Message = msg
          Position = casterPos
        }

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
          match slots |> HashMap.tryFindV action with
          | ValueSome skillId ->
            let validationResult =
              Validation.validate
                skillStore
                statuses
                resources
                cooldowns
                gameTime.TotalGameTime
                skillId

            match validationResult with
            | Ok() ->
              match skillStore.tryFind skillId with
              | ValueSome(Active skill) ->
                match skill.Targeting with
                | Targeting.Self ->
                  this.EventBus.Publish(
                    {
                      Caster = playerId
                      SkillId = skill.Id
                      Target = ValueSome playerId
                    }
                    : SystemCommunications.AbilityIntent
                  )
                | _ ->

                  this.EventBus.Publish(
                    { Slot = action; CasterId = playerId }
                    : SystemCommunications.SlotActivated
                  )
              | _ -> () // Should not happen due to earlier validation
            | Error msg ->
              let notificationMsg =
                match msg with
                | NotEnoughResources -> "Not enough resources!"
                | OnCooldown -> "Ability on cooldown!"
                | SkillNotFound -> "Skill not found!"
                | CannotActivatePassiveSkill -> "Cannot activate passive skill!"
                | Stunned -> "Stunned!"
                | Silenced -> "Silenced!"

              publishNotification notificationMsg
          | ValueNone -> () // Slot is empty
        | _ -> ()

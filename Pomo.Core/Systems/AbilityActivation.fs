namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open FSharp.Control.Reactive

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
  open System.Reactive.Disposables

  [<Struct>]
  type ValidationContext =
    {
      SkillStore: SkillStore
      Statuses: CombatStatus IndexList
      Resources: Entity.Resource voption
      Cooldowns: HashMap<int<SkillId>, TimeSpan> voption
      GameTime: TimeSpan
      EntityId: Guid<EntityId>
    }

  let private SKILL_ACTIVATION_RANGE_BUFFER = 5.0f

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
      (resources: Entity.Resource voption)
      (skill: ActiveSkill)
      : Result<unit, ValidationError> =
      match skill.Cost with
      | ValueNone -> Ok()
      | ValueSome cost ->
        match resources with
        | ValueNone -> Error NotEnoughResources // Should not happen for a live entity
        | ValueSome res ->
          let requiredAmount = cost.Amount |> ValueOption.defaultValue 0

          let hasEnough =
            match cost.ResourceType with
            | Entity.ResourceType.HP -> res.HP >= requiredAmount
            | Entity.ResourceType.MP -> res.MP >= requiredAmount

          if hasEnough then Ok() else Error NotEnoughResources

    let private checkCooldown
      (cooldowns: HashMap<int<SkillId>, TimeSpan> voption)
      (gameTime: TimeSpan)
      (skillId: int<SkillId>)
      : Result<unit, ValidationError> =
      match cooldowns with
      | ValueNone -> Ok()
      | ValueSome cdMap ->
        match cdMap.TryFindV skillId with
        | ValueNone -> Ok()
        | ValueSome readyTime ->
          if gameTime >= readyTime then Ok() else Error OnCooldown

    let validate
      (context: ValidationContext)
      (skillId: int<SkillId>)
      : Result<unit, ValidationError> =
      match context.SkillStore.tryFind skillId with
      | ValueNone -> Error SkillNotFound
      | ValueSome(Passive _) -> Error CannotActivatePassiveSkill
      | ValueSome(Active activeSkill) ->
        checkStatusEffects context.Statuses activeSkill
        |> Result.bind(fun () -> checkResources context.Resources activeSkill)
        |> Result.bind(fun () ->
          checkCooldown context.Cooldowns context.GameTime skillId)

  module private Handlers =
    let private getPendingCastData
      (pendingCasts:
        HashMap<Guid<EntityId>, struct (int<SkillId> * Guid<EntityId>)>)
      (positions: HashMap<Guid<EntityId>, Vector2>)
      (skillStore: SkillStore)
      (entityId: Guid<EntityId>)
      =
      match pendingCasts.TryFindV entityId with
      | ValueSome(struct (skillId, targetId)) ->
        let casterPos = positions.TryFindV entityId
        let targetPos = positions.TryFindV targetId
        let skill = skillStore.tryFind skillId

        match casterPos, targetPos, skill with
        | ValueSome cPos, ValueSome tPos, ValueSome(Active s) ->
          ValueSome(cPos, tPos, s, targetId)
        | _ -> ValueNone
      | _ -> ValueNone

    let private validateAndCast
      (eventBus: EventBus)
      (validationContext: ValidationContext)
      (castData: Vector2 * Vector2 * ActiveSkill * Guid<EntityId>)
      =
      let (casterPos, targetPos, skill, targetId) = castData

      let distance = Vector2.Distance(casterPos, targetPos)
      let maxRange = skill.Range |> ValueOption.defaultValue 0f

      if distance <= maxRange + SKILL_ACTIVATION_RANGE_BUFFER then
        let validationResult = Validation.validate validationContext skill.Id

        match validationResult with
        | Ok() ->
          eventBus.Publish<SystemCommunications.AbilityIntent> {
            Caster = validationContext.EntityId
            SkillId = skill.Id
            Target = ValueSome targetId
          }
        | Error _ ->
          eventBus.Publish<SystemCommunications.ShowNotification> {
            Message = "Cannot cast skill"
            Position = casterPos
          }
      else
        eventBus.Publish<SystemCommunications.ShowNotification> {
          Message = "Target is out of range"
          Position = casterPos
        }

    let handleMovementStateChanged
      (world: World)
      (eventBus: EventBus)
      (skillStore: SkillStore)
      (struct (entityId, movementState))
      =
      if movementState = Idle then
        let pendingCasts = world.PendingSkillCast |> AMap.force
        let positions = world.Positions |> AMap.force

        match getPendingCastData pendingCasts positions skillStore entityId with
        | ValueSome castData ->
          let statuses =
            Projections.CalculateCombatStatuses world
            |> AMap.force
            |> HashMap.tryFindV entityId
            |> ValueOption.defaultValue IndexList.empty

          let resources =
            world.Resources |> AMap.force |> HashMap.tryFindV entityId

          let cooldowns =
            world.AbilityCooldowns |> AMap.force |> HashMap.tryFindV entityId

          let totalGameTime =
            world.Time |> AVal.map _.TotalGameTime |> AVal.force

          let validationContext =
            {
              SkillStore = skillStore
              Statuses = statuses
              Resources = resources
              Cooldowns = cooldowns
              GameTime = totalGameTime
              EntityId = entityId
            }

          validateAndCast eventBus validationContext castData

          // Always clear the pending cast after checking
          eventBus.Publish(
            StateChangeEvent.Combat(
              CombatEvents.PendingSkillCastCleared entityId
            )
          )
        | _ -> () // No pending cast or data missing
      else
        () // Not idle, do nothing

  type AbilityActivationSystem(game: Game, playerId: Guid<EntityId>) as this =
    inherit GameSystem(game)

    let skillStore = this.Game.Services.GetService<SkillStore>()
    let subscriptions = new CompositeDisposable()

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

    override this.Initialize() =
      base.Initialize()

      this.EventBus.GetObservableFor<StateChangeEvent>()
      |> Observable.choose(fun e ->
        match e with
        | StateChangeEvent.Physics(PhysicsEvents.MovementStateChanged data) ->
          Some data
        | _ -> None)
      |> Observable.subscribe(
        Handlers.handleMovementStateChanged this.World this.EventBus skillStore
      )
      |> subscriptions.Add


    override this.Dispose(disposing) =
      if disposing then
        subscriptions.Dispose()

      base.Dispose(disposing)

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
            let validationContext =
              {
                SkillStore = skillStore
                Statuses = statuses
                Resources = resources |> Option.toValueOption
                Cooldowns = cooldowns |> Option.toValueOption
                GameTime = gameTime.TotalGameTime
                EntityId = playerId
              }

            let validationResult =
              Validation.validate validationContext skillId

            match validationResult with
            | Ok() ->
              match skillStore.tryFind skillId with
              | ValueSome(Active skill) ->
                if skill.Intent = Offensive then
                  this.EventBus.Publish(
                    StateChangeEvent.Combat(
                      CombatEvents.InCombatTimerRefreshed playerId
                    )
                  )

                match skill.Targeting with
                | Self ->
                  this.EventBus.Publish<SystemCommunications.AbilityIntent> {
                    Caster = playerId
                    SkillId = skill.Id
                    Target = ValueSome playerId
                  }
                | _ ->

                  this.EventBus.Publish<SystemCommunications.SlotActivated> {
                    Slot = action
                    CasterId = playerId
                  }
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

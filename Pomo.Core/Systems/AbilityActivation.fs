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
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Systems.Systems

module AbilityActivation =
  open System.Reactive.Disposables
  open Pomo.Core.Domain.Spatial.Search

  [<Struct>]
  type AbilityActivationContext = {
    EventBus: EventBus
    SkillStore: SkillStore
    Rng: Random
    SearchContext: SearchContext voption
  }

  [<Struct>]
  type ValidationContext = {
    SkillStore: SkillStore
    Statuses: CombatStatus IndexList
    Resources: Entity.Resource voption
    Cooldowns: HashMap<int<SkillId>, TimeSpan> voption
    GameTime: TimeSpan
    EntityId: Guid<EntityId>
  }

  [<Struct>]
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

  [<Struct>]
  type SkillActivationParams = {
    CasterId: Guid<EntityId>
    CasterPos: Vector2
    Skill: ActiveSkill
    Target: SystemCommunications.SkillTarget
    CenterTargetPos: Vector2
  }

  module private IntentPublisher =
    let private publishSingleIntent
      (eventBus: EventBus)
      (casterId: Guid<EntityId>)
      (skillId: int<SkillId>)
      target
      =
      let intent: SystemCommunications.AbilityIntent = {
        Caster = casterId
        SkillId = skillId
        Target = target
      }

      eventBus.Publish(GameEvent.Intent(IntentEvent.Ability intent))

    let private publishMultiPointIntent
      (ctx: AbilityActivationContext)
      (args: SkillActivationParams)
      (radius: float32)
      (count: int)
      =
      for _ in 1..count do
        let angle = float(ctx.Rng.NextDouble()) * 2.0 * Math.PI
        let dist = float(ctx.Rng.NextDouble()) * float radius
        let offsetX = cos angle * dist
        let offsetY = sin angle * dist

        let pointTargetPos =
          args.CenterTargetPos + Vector2(float32 offsetX, float32 offsetY)

        publishSingleIntent
          ctx.EventBus
          args.CasterId
          args.Skill.Id
          (SystemCommunications.TargetPosition pointTargetPos)

    let private publishAdaptiveConeIntent
      (ctx: AbilityActivationContext)
      (args: SkillActivationParams)
      (_length: float32)
      (_maxTargets: int)
      =
      // AbilityActivation's role for AdaptiveCone is to confirm valid targeting
      // and then pass the target information to Combat.fs for execution.
      // Combat.fs will handle the dynamic aperture and target finding.
      publishSingleIntent
        ctx.EventBus
        args.CasterId
        args.Skill.Id
        (SystemCommunications.TargetPosition args.CenterTargetPos)

    let publish (ctx: AbilityActivationContext) (args: SkillActivationParams) =
      match args.Skill.Area with
      | Point
      | Circle _
      | Cone _
      | Line _ ->
        publishSingleIntent ctx.EventBus args.CasterId args.Skill.Id args.Target
      | AdaptiveCone(length, count) ->
        publishAdaptiveConeIntent ctx args length count
      | MultiPoint(radius, count) ->
        publishMultiPointIntent ctx args radius count

  [<Struct>]
  type PendingCastExecutionParams = {
    ValidationContext: ValidationContext
    Positions: HashMap<Guid<EntityId>, Vector2>
    Skill: ActiveSkill
    Target: SystemCommunications.SkillTarget
  }

  module private Handlers =
    open Pomo.Core.Projections

    let private getPendingCastContext
      (pendingCasts:
        HashMap<
          Guid<EntityId>,
          struct (int<SkillId> * SystemCommunications.SkillTarget)
         >)
      (skillStore: SkillStore)
      (entityId: Guid<EntityId>)
      =
      match pendingCasts.TryFindV entityId with
      | ValueSome struct (skillId, target) ->
        let skill = skillStore.tryFind skillId

        match skill with
        | ValueSome(Active s) -> ValueSome(s, target)
        | _ -> ValueNone
      | _ -> ValueNone

    let private handlePendingCast
      (ctx: AbilityActivationContext)
      (args: PendingCastExecutionParams)
      =
      let casterId = args.ValidationContext.EntityId

      let casterPos = args.Positions.TryFindV casterId

      match casterPos with
      | ValueNone -> () // Caster has no position, should not happen
      | ValueSome casterPos ->
        let validationResult =
          Validation.validate args.ValidationContext args.Skill.Id

        match validationResult with
        | Error msg ->
          let message =
            match msg with
            | NotEnoughResources -> "Not enough resources"
            | OnCooldown -> "Skill is on cooldown"
            | _ -> "Cannot cast skill"

          ctx.EventBus.Publish(
            GameEvent.Notification(
              NotificationEvent.ShowMessage {
                Message = message
                Position = casterPos
              }
            )
          )
        | Ok() ->
          // Validation passed, now check range and publish intent
          let centerTargetPos =
            match args.Target with
            | SystemCommunications.TargetEntity targetId ->
              args.Positions.TryFindV targetId
            | SystemCommunications.TargetPosition pos -> ValueSome pos
            | _ -> ValueNone // Should not happen for pending casts

          match centerTargetPos with
          | ValueNone -> () // Target has no position
          | ValueSome centerTargetPos ->
            let distance = Vector2.Distance(casterPos, centerTargetPos)
            let maxRange = args.Skill.Range |> ValueOption.defaultValue 0f

            if
              distance <= maxRange + Constants.Entity.SkillActivationRangeBuffer
            then
              IntentPublisher.publish ctx {
                CasterId = casterId
                CasterPos = casterPos
                Skill = args.Skill
                Target = args.Target
                CenterTargetPos = centerTargetPos
              }
            else
              // This should have been handled by TargetingSystem, but as a fallback
              ctx.EventBus.Publish(
                GameEvent.Notification(
                  NotificationEvent.ShowMessage {
                    Message = "Target is out of range"
                    Position = casterPos
                  }
                )
              )

    [<Struct>]
    type MovementStateChangeContext = {
      World: World
      AbilityActivationContext: AbilityActivationContext
      CombatStatuses: HashMap<Guid<EntityId>, CombatStatus IndexList>
      Projections: Pomo.Core.Projections.ProjectionService
    }

    let handleMovementStateChanged
      (changeCtx: MovementStateChangeContext)
      (entityMovement: struct (Guid<EntityId> * MovementState))
      =
      let struct (entityId, movementState) = entityMovement

      if movementState = Idle then
        let pendingCasts = changeCtx.World.PendingSkillCast |> AMap.force

        match
          getPendingCastContext
            pendingCasts
            changeCtx.AbilityActivationContext.SkillStore
            entityId
        with
        | ValueSome(skill, target) ->
          let statuses =
            changeCtx.CombatStatuses
            |> HashMap.tryFindV entityId
            |> ValueOption.defaultValue IndexList.empty

          let resources =
            changeCtx.World.Resources |> AMap.force |> HashMap.tryFindV entityId

          let cooldowns =
            changeCtx.World.AbilityCooldowns
            |> AMap.force
            |> HashMap.tryFindV entityId

          let totalGameTime =
            changeCtx.World.Time |> AVal.map _.TotalGameTime |> AVal.force

          let validationContext = {
            SkillStore = changeCtx.AbilityActivationContext.SkillStore
            Statuses = statuses
            Resources = resources
            Cooldowns = cooldowns
            GameTime = totalGameTime
            EntityId = entityId
          }

          let entityScenarios =
            changeCtx.Projections.EntityScenarios |> AMap.force

          let snapshot =
            match entityScenarios |> HashMap.tryFindV entityId with
            | ValueSome scenarioId ->
              changeCtx.Projections.ComputeMovementSnapshot(scenarioId)
            | ValueNone -> Pomo.Core.Projections.MovementSnapshot.Empty

          handlePendingCast changeCtx.AbilityActivationContext {
            ValidationContext = validationContext
            Positions = snapshot.Positions
            Skill = skill
            Target = target
          }

          // Always clear the pending cast after checking
          changeCtx.AbilityActivationContext.EventBus.Publish(
            GameEvent.State(
              StateChangeEvent.Combat(PendingSkillCastCleared entityId)
            )
          )
        | _ -> () // No pending cast or data missing
      else
        () // Not idle, do nothing

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type AbilityActivationSystem
    (game: Game, env: PomoEnvironment, playerId: Guid<EntityId>) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices
    let (Gameplay gameplay) = env.GameplayServices

    let skillStore = stores.SkillStore

    let activationContext = {
      EventBus = core.EventBus
      SkillStore = skillStore
      Rng = core.World.Rng
      SearchContext =
        ValueSome {
          GetNearbyEntities =
            fun v2 radius ->
              let entityScenarios =
                gameplay.Projections.EntityScenarios |> AMap.force

              let liveEntities = gameplay.Projections.LiveEntities |> ASet.force

              match entityScenarios |> HashMap.tryFindV playerId with
              | ValueSome scenarioId ->
                gameplay.Projections.GetNearbyEntitiesSnapshot(
                  gameplay.Projections.ComputeMovementSnapshot(scenarioId),
                  liveEntities,
                  v2,
                  radius
                )
              | ValueNone -> IndexList.empty
        }
    }

    let subscriptions = new CompositeDisposable()

    let actionStates =
      core.World.GameActionStates
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
      gameplay.Projections.ActionSets
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue HashMap.empty)

    let playerCombatStatuses =
      gameplay.Projections.CombatStatuses
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue IndexList.empty)

    let playerResources = core.World.Resources |> AMap.tryFind playerId

    let playerCooldowns = core.World.AbilityCooldowns |> AMap.tryFind playerId



    override this.Initialize() =
      base.Initialize()

      core.EventBus.Observable
      |> Observable.choose(fun e ->
        match e with
        | GameEvent.State(Physics(MovementStateChanged data)) -> Some data
        | _ -> None)
      |> Observable.subscribe(fun e ->
        Handlers.handleMovementStateChanged
          {
            World = core.World
            AbilityActivationContext = activationContext
            CombatStatuses = gameplay.Projections.CombatStatuses |> AMap.force
            Projections = gameplay.Projections
          }
          e)
      |> subscriptions.Add


    override _.Dispose disposing =
      if disposing then
        subscriptions.Dispose()

      base.Dispose disposing

    override this.Update gameTime =
      let actions = pressedActions |> ASet.force
      let slots = quickSlots |> AVal.force
      let statuses = playerCombatStatuses |> AVal.force
      let resources = playerResources |> AVal.force
      let cooldowns = playerCooldowns |> AVal.force
      let entityScenarios = gameplay.Projections.EntityScenarios |> AMap.force

      let snapshot =
        match entityScenarios |> HashMap.tryFindV playerId with
        | ValueSome scenarioId ->
          gameplay.Projections.ComputeMovementSnapshot(scenarioId)
        | ValueNone -> Pomo.Core.Projections.MovementSnapshot.Empty

      let publishNotification(msg: string) =
        let casterPos =
          snapshot.Positions
          |> HashMap.tryFind playerId
          |> Option.defaultValue Vector2.Zero

        core.EventBus.Publish(
          GameEvent.Notification(
            NotificationEvent.ShowMessage {
              Message = msg
              Position = casterPos
            }
          )
        )

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
          | ValueSome(SlotProcessing.Skill skillId) ->
            let validationContext = {
              SkillStore = skillStore
              Statuses = statuses
              Resources = resources |> Option.toValueOption
              Cooldowns = cooldowns |> Option.toValueOption
              GameTime = gameTime.TotalGameTime
              EntityId = playerId
            }

            let validationResult = Validation.validate validationContext skillId

            match validationResult with
            | Ok() ->
              match skillStore.tryFind skillId with
              | ValueSome(Active skill) ->
                if skill.Intent = Offensive then
                  core.EventBus.Publish(
                    GameEvent.State(
                      StateChangeEvent.Combat(InCombatTimerRefreshed playerId)
                    )
                  )

                match skill.Targeting with
                | Self ->
                  core.EventBus.Publish(
                    GameEvent.Intent(
                      IntentEvent.Ability {
                        Caster = playerId
                        SkillId = skill.Id
                        Target = SystemCommunications.TargetSelf
                      }
                    )
                  )
                | _ ->

                  core.EventBus.Publish(
                    GameEvent.Intent(
                      IntentEvent.SlotActivated {
                        Slot = action
                        CasterId = playerId
                      }
                    )
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
          | ValueSome(Item itemInstanceId) ->
            match core.World.ItemInstances.TryGetValue itemInstanceId with
            | true, itemInstance ->
              match itemInstance.UsesLeft with
              | ValueSome 0 -> publishNotification "Item has no uses left!"
              | ValueSome _ ->
                core.EventBus.Publish(
                  GameEvent.ItemIntent(
                    ItemIntentEvent.Use {
                      EntityId = playerId
                      ItemInstanceId = itemInstanceId
                    }
                  )
                )
              | ValueNone -> ()
            | false, _ -> ()
          | ValueNone -> () // Slot is empty
        | _ -> ()

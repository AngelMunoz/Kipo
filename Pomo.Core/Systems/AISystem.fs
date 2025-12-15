namespace Pomo.Core.Systems

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open Pomo.Core.Domain
open Pomo.Core.Domain.AI
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Stores
open Pomo.Core.EventBus

open Pomo.Core.Domain.Spatial
open Systems

module SkillSelection =
  /// Check if a skill is on cooldown
  let isSkillReady
    (skillId: int<SkillId>)
    (cooldowns: HashMap<int<SkillId>, TimeSpan> voption)
    (currentTime: TimeSpan)
    =
    match cooldowns with
    | ValueNone -> true
    | ValueSome cds ->
      match cds |> HashMap.tryFindV skillId with
      | ValueNone -> true
      | ValueSome cooldownEnd -> currentTime >= cooldownEnd

  /// Get the range of a skill
  let getSkillRange(skill: Skill.Skill) =
    match skill with
    | Skill.Active active -> active.Range |> ValueOption.defaultValue 64.0f
    | Skill.Passive _ -> 0.0f

  /// Check if target is within skill range
  let isTargetInRange
    (casterPos: Vector2)
    (targetPos: Vector2)
    (skillRange: float32)
    =
    let distance = Vector2.Distance(casterPos, targetPos)
    distance <= skillRange

  /// Select the best skill to use given the context
  let selectSkill
    (skillIds: int<SkillId>[])
    (skillStore: SkillStore)
    (cooldowns: HashMap<int<SkillId>, TimeSpan> voption)
    (currentTime: TimeSpan)
    (casterPos: Vector2)
    (targetPos: Vector2)
    =
    skillIds
    |> Array.choose(fun skillId ->
      match skillStore.tryFind skillId with
      | ValueNone -> None
      | ValueSome skill ->
        let range = getSkillRange skill
        let inRange = isTargetInRange casterPos targetPos range
        let ready = isSkillReady skillId cooldowns currentTime
        if inRange && ready then Some(skillId, skill) else None)
    |> Array.tryHead

  /// Create an AbilityIntent for the AI to cast a skill
  let createAbilityIntent
    (casterId: Guid<EntityId>)
    (skillId: int<SkillId>)
    (skill: Skill.Skill)
    (targetEntityId: Guid<EntityId> voption)
    (targetPos: Vector2)
    : SystemCommunications.AbilityIntent =
    let target =
      match skill with
      | Skill.Active active ->
        match active.Targeting with
        | Skill.Targeting.Self -> SystemCommunications.SkillTarget.TargetSelf
        | Skill.Targeting.TargetEntity ->
          match targetEntityId with
          | ValueSome entityId ->
            SystemCommunications.SkillTarget.TargetEntity entityId
          | ValueNone ->
            SystemCommunications.SkillTarget.TargetPosition targetPos
        | Skill.Targeting.TargetPosition ->
          SystemCommunications.SkillTarget.TargetPosition targetPos
        | Skill.Targeting.TargetDirection ->
          SystemCommunications.SkillTarget.TargetDirection targetPos
      | Skill.Passive _ -> SystemCommunications.SkillTarget.TargetSelf

    {
      Caster = casterId
      SkillId = skillId
      Target = target
    }


module Perception =
  let inline distance (p1: Vector2) (p2: Vector2) = Vector2.Distance(p1, p2)

  let inline isHostileFaction
    (controllerFactions: Faction HashSet)
    (targetFactions: Faction HashSet)
    =
    let isEnemy = controllerFactions.Contains Enemy
    let isAlly = controllerFactions.Contains Ally
    let targetIsPlayer = targetFactions.Contains Player
    let targetIsAlly = targetFactions.Contains Ally
    let targetIsEnemy = targetFactions.Contains Enemy
    isEnemy && (targetIsPlayer || targetIsAlly) || isAlly && targetIsEnemy

  let gatherVisualCues
    (controllerPos: Vector2)
    (controllerFactions: Faction HashSet)
    (config: PerceptionConfig)
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (factions: HashMap<Guid<EntityId>, Faction HashSet>)
    (currentTick: TimeSpan)
    (nearbyEntities: IndexList<Guid<EntityId>>)
    =
    nearbyEntities
    |> IndexList.choose(fun entityId ->
      match positions |> HashMap.tryFindV entityId with
      | ValueSome pos ->
        match factions |> HashMap.tryFindV entityId with
        | ValueSome targetFactions ->
          let dist = distance controllerPos pos

          if
            dist <= config.visualRange
            && isHostileFaction controllerFactions targetFactions
          then
            let strength =
              if dist < config.visualRange * 0.3f then Overwhelming
              elif dist < config.visualRange * 0.6f then Strong
              elif dist < config.visualRange * 0.8f then Moderate
              else Weak

            Some {
              cueType = Visual
              strength = strength
              sourceEntityId = ValueSome entityId
              position = pos
              timestamp = currentTick
            }
          else
            None
        | _ -> None
      | ValueNone -> None)

  let decayMemories
    (memories: HashMap<Guid<EntityId>, MemoryEntry>)
    (currentTick: TimeSpan)
    (memoryDuration: TimeSpan)
    : HashMap<Guid<EntityId>, MemoryEntry> =
    memories
    |> HashMap.filter(fun _ entry ->
      currentTick - entry.lastSeenTick < memoryDuration)

  let gatherCues
    (controller: AIController)
    (archetype: AIArchetype)
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (factions: HashMap<Guid<EntityId>, Faction HashSet>)
    (spatialGrid: HashMap<GridCell, IndexList<Guid<EntityId>>>)
    (controllerEntityPos: Vector2)
    (controllerEntityFactions: Faction HashSet)
    (currentTick: TimeSpan)
    =
    let cells =
      Spatial.getCellsInRadius
        Constants.Collision.GridCellSize
        controllerEntityPos
        archetype.perceptionConfig.visualRange

    let nearbyEntities =
      cells
      |> IndexList.collect(fun cell ->
        match spatialGrid |> HashMap.tryFindV cell with
        | ValueSome list -> list
        | ValueNone -> IndexList.empty)

    let visualCues =
      gatherVisualCues
        controllerEntityPos
        controllerEntityFactions
        archetype.perceptionConfig
        positions
        factions
        currentTick
        nearbyEntities

    let decayedMemories =
      decayMemories
        controller.memories
        currentTick
        archetype.perceptionConfig.memoryDuration

    let updatedMemories =
      visualCues
      |> IndexList.fold
        (fun (mem: HashMap<Guid<EntityId>, MemoryEntry>) (cue: PerceptionCue) ->
          match cue.sourceEntityId with
          | ValueSome entityId ->
            let confidence =
              match cue.strength with
              | Weak -> 0.25f
              | Moderate -> 0.5f
              | Strong -> 0.75f
              | Overwhelming -> 1.0f

            let entry = {
              entityId = entityId
              lastSeenTick = currentTick
              lastKnownPosition = cue.position
              confidence = confidence
            }

            mem.Add(entityId, entry)
          | ValueNone -> mem)
        decayedMemories

    let memoryCues =
      updatedMemories
      |> HashMap.map(fun entityId memoryEntry ->
        let strength =
          if memoryEntry.confidence >= 1.0f then Overwhelming
          elif memoryEntry.confidence >= 0.75f then Strong
          elif memoryEntry.confidence >= 0.5f then Moderate
          else Weak

        {
          cueType = Memory
          strength = strength
          sourceEntityId = ValueSome entityId
          position = memoryEntry.lastKnownPosition
          timestamp = memoryEntry.lastSeenTick
        })
      |> HashMap.toValueArray

    let allCues = Array.concat [ visualCues.AsArray; memoryCues ]

    struct (allCues, updatedMemories)

module Decision =

  // Helper to find abilities - simplified for now as we don't have full AbilityStore access in the same way
  // We'll assume we can get available skills from the world or passed in services

  let matchCueToPriority (cue: PerceptionCue) (priorities: CuePriority[]) =
    priorities
    |> Array.tryFind(fun p ->
      p.cueType = cue.cueType && cue.strength >= p.minStrength)

  let selectBestCue (cues: PerceptionCue[]) (priorities: CuePriority[]) =
    cues
    |> Array.choose(fun cue ->
      matchCueToPriority cue priorities
      |> Option.map(fun priority -> struct (cue, priority)))
    |> Array.sortBy(fun struct (_, priority) -> priority.priority)
    |> Array.tryHead

  // Simplified command generation
  let generateCommand
    (cue: PerceptionCue)
    (priority: CuePriority)
    (controller: AIController)
    (skillStore: SkillStore) // Using SkillStore instead of AbilityStore
    =
    match priority.response with
    | Investigate ->
      ValueSome(
        {
          EntityId = controller.controlledEntityId
          Target = cue.position
        }
        : SystemCommunications.SetMovementTarget
      )
    | Engage ->
      // For now, just move to target. We'll add ability usage later.
      ValueSome(
        {
          EntityId = controller.controlledEntityId
          Target = cue.position
        }
        : SystemCommunications.SetMovementTarget
      )
    | Evade ->
      // Move back to spawn
      ValueSome(
        {
          EntityId = controller.controlledEntityId
          Target = controller.spawnPosition
        }
        : SystemCommunications.SetMovementTarget
      )
    | Flee -> ValueNone
    | Ignore -> ValueNone

module AISystemLogic =

  let selectNextWaypoint
    (behaviorType: BehaviorType)
    (controller: AIController)
    (currentPos: Vector2)
    (waypoints: Vector2[])
    =
    match behaviorType with
    | Patrol ->
      let currentIdx = controller.waypointIndex % waypoints.Length
      let targetWaypoint = waypoints[currentIdx]

      let dist = Vector2.Distance(currentPos, targetWaypoint)
      let hasReached = dist < Constants.AI.WaypointReachedThreshold // Threshold

      if hasReached then
        let nextIdx = (controller.waypointIndex + 1) % waypoints.Length
        struct (waypoints[nextIdx], nextIdx)
      else
        struct (targetWaypoint, currentIdx)

    | Aggressive ->
      if Array.isEmpty waypoints then
        struct (controller.spawnPosition, controller.waypointIndex)
      else
        let targetWaypoint = waypoints |> Array.randomChoice
        struct (targetWaypoint, controller.waypointIndex)
    | Defensive
    | Supporter ->
      let targetWaypoint =
        waypoints
        |> Array.minBy(fun wp -> Vector2.Distance(controller.spawnPosition, wp))

      struct (targetWaypoint, controller.waypointIndex)

    | Ambusher ->
      let targetWaypoint =
        if controller.waypointIndex = 0 then
          controller.spawnPosition
        else
          waypoints |> Array.randomChoice

      struct (targetWaypoint, controller.waypointIndex)
    | Turret -> struct (controller.spawnPosition, controller.waypointIndex)
    | Passive ->
      let targetWaypoint = waypoints |> Array.randomChoice
      struct (targetWaypoint, controller.waypointIndex)

  let private determineState (response: ResponseType) (cue: PerceptionCue) =
    match response with
    | Engage ->
      match cue.sourceEntityId with
      | ValueSome _ -> Chasing
      | ValueNone -> Investigating
    | Investigate -> Investigating
    | Evade
    | Flee ->
      match cue.sourceEntityId with
      | ValueSome _ -> Fleeing
      | ValueNone -> Fleeing
    | Ignore -> AIState.Idle

  let private handleBestCue
    (cue: PerceptionCue)
    (priority: CuePriority)
    (controller: AIController)
    (controllerPos: Vector2)
    (skillStore: SkillStore)
    (cooldowns: HashMap<int<SkillId>, TimeSpan> voption)
    (currentTick: TimeSpan)
    =
    let movementCmd =
      Decision.generateCommand cue priority controller skillStore

    let state = determineState priority.response cue

    // Try to cast a skill when engaging or investigating (if in range)
    let abilityIntent =
      match priority.response with
      | Engage
      | Investigate ->
        match
          SkillSelection.selectSkill
            controller.skills
            skillStore
            cooldowns
            currentTick
            controllerPos
            cue.position
        with
        | Some(skillId, skill) ->
          Some(
            SkillSelection.createAbilityIntent
              controller.controlledEntityId
              skillId
              skill
              cue.sourceEntityId
              cue.position
          )
        | None -> None
      | _ -> None

    struct (movementCmd, abilityIntent, true, controller.waypointIndex, state)


  let private getNavigateToSpawnCommand(controller: AIController) =
    ValueSome(
      {
        SystemCommunications.SetMovementTarget.EntityId =
          controller.controlledEntityId
        Target = controller.spawnPosition
      }
      : SystemCommunications.SetMovementTarget
    )

  let private handlePatrol
    (controller: AIController)
    (currentPos: Vector2)
    (waypoints: Vector2[])
    =
    let struct (targetWaypoint, nextIdx) =
      selectNextWaypoint Patrol controller currentPos waypoints

    let cmd =
      ValueSome(
        {
          SystemCommunications.SetMovementTarget.EntityId =
            controller.controlledEntityId
          Target = targetWaypoint
        }
        : SystemCommunications.SetMovementTarget
      )

    struct (cmd, None, true, nextIdx, Patrolling)

  let private handleAggressive
    (controller: AIController)
    (waypoints: Vector2[])
    =
    let targetWaypoint = waypoints |> Array.randomChoice

    let cmd =
      ValueSome(
        {
          SystemCommunications.SetMovementTarget.EntityId =
            controller.controlledEntityId
          Target = targetWaypoint
        }
        : SystemCommunications.SetMovementTarget
      )

    struct (cmd, None, true, controller.waypointIndex, Patrolling)

  let private handleNoCue
    (controller: AIController)
    (archetype: AIArchetype)
    (currentPos: Vector2)
    =
    let navigateSpawn = getNavigateToSpawnCommand controller

    match controller.absoluteWaypoints with
    | ValueNone
    | ValueSome [||] ->
      // No waypoints behavior
      match archetype.behaviorType with
      | Patrol ->
        struct (ValueNone, None, true, controller.waypointIndex, AIState.Idle)
      | Aggressive
      | Defensive
      | Supporter
      | Ambusher
      | Turret
      | Passive ->
        struct (navigateSpawn,
                None,
                true,
                controller.waypointIndex,
                AIState.Idle)

    | ValueSome waypoints ->
      // Waypoints available behavior
      match archetype.behaviorType with
      | Patrol -> handlePatrol controller currentPos waypoints
      | Aggressive -> handleAggressive controller waypoints
      | Defensive
      | Supporter
      | Ambusher
      | Passive ->
        struct (navigateSpawn,
                None,
                true,
                controller.waypointIndex,
                AIState.Idle)
      | Turret ->
        struct (ValueNone, None, true, controller.waypointIndex, AIState.Idle)

  let processAndGenerateCommands
    (controller: AIController)
    (archetype: AIArchetype)
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (factions: HashMap<Guid<EntityId>, Faction HashSet>)
    (spatialGrid: HashMap<GridCell, IndexList<Guid<EntityId>>>)
    (skillStore: SkillStore)
    (cooldowns: HashMap<Guid<EntityId>, HashMap<int<SkillId>, TimeSpan>>)
    (currentTick: TimeSpan)
    =
    let controlledPosition =
      positions |> HashMap.tryFindV controller.controlledEntityId

    let controlledFactions =
      factions |> HashMap.tryFindV controller.controlledEntityId

    match controlledPosition, controlledFactions with
    | ValueSome pos, ValueSome facs ->
      let struct (cues, updatedMemories) =
        Perception.gatherCues
          controller
          archetype
          positions
          factions
          spatialGrid
          pos
          facs
          currentTick

      let timeSinceLastDecision = currentTick - controller.lastDecisionTime

      let entityCooldowns =
        cooldowns |> HashMap.tryFindV controller.controlledEntityId

      let struct (command, abilityIntent, shouldUpdateTime, newWaypointIndex,
                  newState) =
        if timeSinceLastDecision >= archetype.decisionInterval then
          let bestCue = Decision.selectBestCue cues archetype.cuePriorities

          match bestCue with
          | Some struct (cue, priority) ->
            handleBestCue
              cue
              priority
              controller
              pos
              skillStore
              entityCooldowns
              currentTick
          | None -> handleNoCue controller archetype pos
        else
          struct (ValueNone,
                  None,
                  false,
                  controller.waypointIndex,
                  controller.currentState)

      let updatedController = {
        controller with
            memories = updatedMemories
            waypointIndex = newWaypointIndex
            currentState = newState
            lastDecisionTime =
              if shouldUpdateTime then
                currentTick
              else
                controller.lastDecisionTime
      }

      struct (updatedController, command, abilityIntent)

    | _ -> struct (controller, ValueNone, None)


open Pomo.Core.Environment
open Pomo.Core.Environment.Patterns

type AISystem(game: Game, env: PomoEnvironment) =
  inherit GameSystem(game)

  let (Core core) = env.CoreServices
  let (Stores stores) = env.StoreServices
  let (Gameplay gameplay) = env.GameplayServices

  let archetypeStore = stores.AIArchetypeStore
  let skillStore = stores.SkillStore

  let fallbackArchetype = {
    id = %0
    name = "Fallback"
    behaviorType = Aggressive
    perceptionConfig = {
      visualRange = 150.0f
      fov = 360.0f
      memoryDuration = TimeSpan.FromSeconds 5.0
    }
    cuePriorities = [||]
    decisionInterval = TimeSpan.FromSeconds 0.5
    baseStats = {
      Power = 1
      Magic = 1
      Sense = 1
      Charm = 1
    }
  }

  override val Kind = SystemKind.AI with get

  override _.Update _ =
    // Snapshot all necessary data
    let controllers = core.World.AIControllers |> AMap.force
    let entityScenarios = core.World.EntityScenario |> AMap.force
    let factions = core.World.Factions |> AMap.force
    let cooldowns = core.World.AbilityCooldowns |> AMap.force
    let currentTick = (core.World.Time |> AVal.force).TotalGameTime

    // Group controllers by scenario
    let controllersByScenario =
      controllers
      |> HashMap.toSeq
      |> Seq.choose(fun (id, ctrl) ->
        match entityScenarios.TryFindV ctrl.controlledEntityId with
        | ValueSome sId -> Some(sId, (id, ctrl))
        | ValueNone -> None)
      |> Seq.groupBy fst

    for (scenarioId, group) in controllersByScenario do
      let snapshot = gameplay.Projections.ComputeMovementSnapshot(scenarioId)
      let positions = snapshot.Positions
      let spatialGrid = snapshot.SpatialGrid

      for (_, (controllerId, controller)) in group do

        let archetype =
          archetypeStore.tryFind controller.archetypeId
          |> ValueOption.defaultValue fallbackArchetype

        let struct (updatedController, command, abilityIntent) =
          AISystemLogic.processAndGenerateCommands
            controller
            archetype
            positions
            factions
            spatialGrid
            skillStore
            cooldowns
            currentTick

        match command with
        | ValueSome cmd -> core.EventBus.Publish cmd
        | ValueNone -> ()

        match abilityIntent with
        | Some intent -> core.EventBus.Publish intent
        | None -> ()

        if updatedController <> controller then
          core.EventBus.Publish(
            StateChangeEvent.AI(
              ControllerUpdated struct (controllerId, updatedController)
            )
          )

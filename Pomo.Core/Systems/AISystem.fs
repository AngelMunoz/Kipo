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

/// Shared context and result types for AI system
module AIContext =

  /// Snapshot of world state for AI decision making
  [<Struct>]
  type WorldSnapshot = {
    Positions: HashMap<Guid<EntityId>, Vector2>
    Velocities: HashMap<Guid<EntityId>, Vector2>
    Factions: HashMap<Guid<EntityId>, Faction HashSet>
    SpatialGrid: HashMap<GridCell, IndexList<Guid<EntityId>>>
  }

  /// Context for a single entity being processed
  [<Struct>]
  type EntityContext = {
    EntityId: Guid<EntityId>
    Position: Vector2
    Velocity: Vector2
    Factions: Faction HashSet
  }

  /// Context for perception/decision making
  [<Struct>]
  type PerceptionContext = {
    Controller: AIController
    Archetype: AIArchetype
    Entity: EntityContext
    CurrentTick: TimeSpan
  }

  /// Result of AI decision making
  [<Struct>]
  type DecisionResult = {
    Command: SystemCommunications.SetMovementTarget voption
    AbilityIntent: SystemCommunications.AbilityIntent voption
    ShouldUpdateTime: bool
    WaypointIndex: int
    NewState: AIState
  }

  let inline failureResult
    (state: AIState)
    (waypointIndex: int)
    : DecisionResult =
    {
      Command = ValueNone
      AbilityIntent = ValueNone
      ShouldUpdateTime = true
      WaypointIndex = waypointIndex
      NewState = state
    }

  let inline idleResult(waypointIndex: int) : DecisionResult =
    failureResult AIState.Idle waypointIndex

  let inline commandResult
    (cmd: SystemCommunications.SetMovementTarget)
    (state: AIState)
    (waypointIndex: int)
    : DecisionResult =
    {
      Command = ValueSome cmd
      AbilityIntent = ValueNone
      ShouldUpdateTime = true
      WaypointIndex = waypointIndex
      NewState = state
    }

  let inline abilityResult
    (intent: SystemCommunications.AbilityIntent)
    (state: AIState)
    (waypointIndex: int)
    : DecisionResult =
    {
      Command = ValueNone
      AbilityIntent = ValueSome intent
      ShouldUpdateTime = true
      WaypointIndex = waypointIndex
      NewState = state
    }

open AIContext

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
  let inline getSkillRange(skill: Skill.Skill) =
    match skill with
    | Skill.Active active -> active.Range |> ValueOption.defaultValue 64.0f
    | Skill.Passive _ -> 0.0f

  /// Check if target is within skill range
  let inline isTargetInRange
    (casterPos: Vector2)
    (targetPos: Vector2)
    (skillRange: float32)
    =
    let distance = Vector2.Distance(casterPos, targetPos)
    distance <= skillRange

  /// Select the best skill to use given the context
  let inline selectSkill
    (skillIds: int<SkillId>[])
    (skillStore: SkillStore)
    (cooldowns: HashMap<int<SkillId>, TimeSpan> voption)
    (currentTime: TimeSpan)
    (casterPos: Vector2)
    (targetPos: Vector2)
    : struct (int<SkillId> * Skill.Skill) voption =
    skillIds
    |> Array.choose(fun skillId ->
      match skillStore.tryFind skillId with
      | ValueNone -> None
      | ValueSome skill ->
        let range = getSkillRange skill
        let inRange = isTargetInRange casterPos targetPos range
        let ready = isSkillReady skillId cooldowns currentTime

        if inRange && ready then
          Some(struct (skillId, skill))
        else
          None)
    |> Array.tryHead
    |> ValueOption.ofOption

  /// Create an AbilityIntent for the AI to cast a skill
  let inline createAbilityIntent
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

  let isInFieldOfView
    (facingDir: Vector2)
    (controllerPos: Vector2)
    (targetPos: Vector2)
    (fovDegrees: float32)
    =
    if fovDegrees >= 360.0f then
      true
    elif facingDir.LengthSquared() < 0.001f then
      true
    else
      let toTarget = Vector2.Normalize(targetPos - controllerPos)
      let facing = Vector2.Normalize(facingDir)
      let dot = Vector2.Dot(facing, toTarget)
      let clampedDot = max -1.0f (min 1.0f dot)
      let angleBetween = acos(clampedDot) * (180.0f / float32 System.Math.PI)
      let halfFov = fovDegrees / 2.0f
      angleBetween <= halfFov

  let gatherVisualCues
    (ctx: PerceptionContext)
    (world: WorldSnapshot)
    (nearbyEntities: IndexList<Guid<EntityId>>)
    =
    nearbyEntities
    |> IndexList.choose(fun entityId ->
      match world.Positions |> HashMap.tryFindV entityId with
      | ValueSome pos ->
        match world.Factions |> HashMap.tryFindV entityId with
        | ValueSome targetFactions ->
          let dist = distance ctx.Entity.Position pos
          let inRange = dist <= ctx.Archetype.perceptionConfig.visualRange

          let inFov =
            isInFieldOfView
              ctx.Entity.Velocity
              ctx.Entity.Position
              pos
              ctx.Archetype.perceptionConfig.fov

          let isHostile = isHostileFaction ctx.Entity.Factions targetFactions

          if inRange && inFov && isHostile then
            let strength =
              if dist < ctx.Archetype.perceptionConfig.visualRange * 0.3f then
                Overwhelming
              elif
                dist < ctx.Archetype.perceptionConfig.visualRange * 0.6f
              then
                Strong
              elif
                dist < ctx.Archetype.perceptionConfig.visualRange * 0.8f
              then
                Moderate
              else
                Weak

            Some {
              cueType = Visual
              strength = strength
              sourceEntityId = ValueSome entityId
              position = pos
              timestamp = ctx.CurrentTick
            }
          else
            None
        | ValueNone -> None
      | ValueNone -> None)

  let decayMemories
    (memories: HashMap<Guid<EntityId>, MemoryEntry>)
    (currentTick: TimeSpan)
    (memoryDuration: TimeSpan)
    (controllerPos: Vector2)
    (spawnPos: Vector2)
    (leashDistance: float32)
    =
    memories
    |> HashMap.chooseV(fun _ entry ->
      let age = currentTick - entry.lastSeenTick
      let distToSpawn = Vector2.Distance(controllerPos, spawnPos)
      let isLeashed = distToSpawn <= leashDistance

      if age < memoryDuration && isLeashed then
        // Decay confidence over time
        let decayFactor =
          1.0f
          - (float32 age.TotalSeconds / float32 memoryDuration.TotalSeconds)

        let newConfidence = entry.confidence * decayFactor

        if newConfidence > 0.1f then
          ValueSome {
            entry with
                confidence = newConfidence
          }
        else
          ValueNone
      else
        ValueNone)

  let gatherCues (ctx: PerceptionContext) (world: WorldSnapshot) =
    let cells =
      Spatial.getCellsInRadius
        Constants.Collision.GridCellSize
        ctx.Entity.Position
        ctx.Archetype.perceptionConfig.visualRange

    let nearbyEntities =
      cells
      |> IndexList.collect(fun cell ->
        match world.SpatialGrid |> HashMap.tryFindV cell with
        | ValueSome list -> list
        | ValueNone -> IndexList.empty)

    let visualCues = gatherVisualCues ctx world nearbyEntities

    let decayedMemories =
      decayMemories
        ctx.Controller.memories
        ctx.CurrentTick
        ctx.Archetype.perceptionConfig.memoryDuration
        ctx.Entity.Position
        ctx.Controller.spawnPosition
        ctx.Archetype.perceptionConfig.leashDistance

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
              lastSeenTick = ctx.CurrentTick
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

module WaypointNavigation =
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
      let hasReached = dist < Constants.AI.WaypointReachedThreshold

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

/// Behavior tree execution module for data-driven AI decisions
module private BehaviorTreeExecution =

  /// Context passed to condition/action handlers during tree evaluation
  type ExecutionContext = {
    controller: AIController
    archetype: AIArchetype
    entityPos: Vector2
    targetPos: Vector2 voption
    targetId: Guid<EntityId> voption
    targetDistance: float32 voption
    skillStore: SkillStore
    cooldowns: HashMap<int<SkillId>, TimeSpan> voption
    currentTime: TimeSpan
    bestCue: PerceptionCue voption
    cueResponse: ResponseType voption
  }

  /// Result of action execution
  type ActionResult = {
    nodeResult: NodeResult
    command: SystemCommunications.SetMovementTarget voption
    abilityIntent: SystemCommunications.AbilityIntent voption
    newState: AIState
    waypointIndex: int voption
  }

  // --- Condition Handlers ---

  let inline getParam key (parms: HashMap<string, string>) defaultVal =
    parms
    |> HashMap.tryFindV key
    |> ValueOption.map float32
    |> ValueOption.defaultValue defaultVal

  let evaluateCondition
    (name: string)
    (parms: HashMap<string, string>)
    (ctx: ExecutionContext)
    : NodeResult =
    match name with
    | "HasTarget" -> if ctx.targetId.IsSome then Success else Failure
    | "TargetInRange" ->
      match ctx.targetDistance with
      | ValueSome dist ->
        let range =
          getParam "Range" parms ctx.archetype.perceptionConfig.visualRange

        if dist <= range then Success else Failure
      | ValueNone -> Failure
    | "TargetInMeleeRange" ->
      match ctx.targetDistance with
      | ValueSome dist -> if dist <= 48.0f then Success else Failure
      | ValueNone -> Failure
    | "TargetTooClose" ->
      match ctx.targetDistance with
      | ValueSome dist ->
        let minDist = getParam "MinDistance" parms 48.0f
        if dist < minDist then Success else Failure
      | ValueNone -> Failure
    | "SelfHealthBelow" ->
      // TODO: Need health access - for now return Failure
      Failure
    | "TargetHealthBelow" ->
      // TODO: Need target health access - for now return Failure
      Failure
    | "BeyondLeash" ->
      let dist = Vector2.Distance(ctx.entityPos, ctx.controller.spawnPosition)

      if dist > ctx.archetype.perceptionConfig.leashDistance then
        Success
      else
        Failure
    | "SkillReady" ->
      let hasReady =
        ctx.controller.skills
        |> Array.exists(fun skillId ->
          SkillSelection.isSkillReady skillId ctx.cooldowns ctx.currentTime)

      if hasReady then Success else Failure
    | "HasCue" -> if ctx.bestCue.IsSome then Success else Failure
    | "CueResponseIs" ->
      match ctx.cueResponse with
      | ValueSome response ->
        let expectedStr =
          parms |> HashMap.tryFindV "Response" |> ValueOption.defaultValue ""

        let expected =
          match expectedStr with
          | "Engage" -> ValueSome Engage
          | "Investigate" -> ValueSome Investigate
          | "Evade" -> ValueSome Evade
          | "Flee" -> ValueSome Flee
          | "Ignore" -> ValueSome Ignore
          | _ -> ValueNone

        match expected with
        | ValueSome exp when exp = response -> Success
        | _ -> Failure
      | ValueNone -> Failure
    | _ -> Failure

  // --- Action Handlers ---

  let executeAction
    (name: string)
    (parms: HashMap<string, string>)
    (ctx: ExecutionContext)
    : ActionResult =
    match name with
    | "ChaseTarget" ->
      match ctx.targetPos with
      | ValueSome targetPos ->
        let command: SystemCommunications.SetMovementTarget = {
          EntityId = ctx.controller.controlledEntityId
          Target = targetPos
        }

        {
          nodeResult = Running
          command = ValueSome command
          abilityIntent = ValueNone
          newState = Chasing
          waypointIndex = ValueNone
        }
      | ValueNone ->
          {
            nodeResult = Failure
            command = ValueNone
            abilityIntent = ValueNone
            newState = ctx.controller.currentState
            waypointIndex = ValueNone
          }

    | "UseRangedAttack"
    | "UseMeleeAttack"
    | "UseHeal"
    | "UseDebuff"
    | "UseBuff" ->
      match ctx.targetPos, ctx.targetId with
      | ValueSome targetPos, targetIdOpt ->
        match
          SkillSelection.selectSkill
            ctx.controller.skills
            ctx.skillStore
            ctx.cooldowns
            ctx.currentTime
            ctx.entityPos
            targetPos
        with
        | ValueSome struct (skillId, skill) ->
          let intent =
            SkillSelection.createAbilityIntent
              ctx.controller.controlledEntityId
              skillId
              skill
              targetIdOpt
              targetPos

          {
            nodeResult = Success
            command = ValueNone
            abilityIntent = ValueSome intent
            newState = Attacking
            waypointIndex = ValueNone
          }
        | ValueNone ->
          let command: SystemCommunications.SetMovementTarget = {
            EntityId = ctx.controller.controlledEntityId
            Target = targetPos
          }

          {
            nodeResult = Running
            command = ValueSome command
            abilityIntent = ValueNone
            newState = Chasing
            waypointIndex = ValueNone
          }
      | _ ->
          {
            nodeResult = Failure
            command = ValueNone
            abilityIntent = ValueNone
            newState = ctx.controller.currentState
            waypointIndex = ValueNone
          }

    | "Patrol" ->
      match ctx.controller.absoluteWaypoints with
      | ValueSome waypoints when waypoints.Length > 0 ->
        let struct (targetWaypoint, nextIdx) =
          WaypointNavigation.selectNextWaypoint
            ctx.archetype.behaviorType
            ctx.controller
            ctx.entityPos
            waypoints

        let command: SystemCommunications.SetMovementTarget = {
          EntityId = ctx.controller.controlledEntityId
          Target = targetWaypoint
        }

        {
          nodeResult = Running
          command = ValueSome command
          abilityIntent = ValueNone
          newState = Patrolling
          waypointIndex = ValueSome nextIdx
        }
      | _ ->
          {
            nodeResult = Success
            command = ValueNone
            abilityIntent = ValueNone
            newState = AIState.Idle
            waypointIndex = ValueNone
          }

    | "ReturnToSpawn" ->
      let command: SystemCommunications.SetMovementTarget = {
        EntityId = ctx.controller.controlledEntityId
        Target = ctx.controller.spawnPosition
      }

      {
        nodeResult = Running
        command = ValueSome command
        abilityIntent = ValueNone
        newState = Patrolling
        waypointIndex = ValueNone
      }

    | "Retreat" ->
      match ctx.targetPos with
      | ValueSome targetPos ->
        let direction = ctx.entityPos - targetPos

        let normalizedDir =
          if direction.LengthSquared() > 0.0f then
            Vector2.Normalize(direction)
          else
            Vector2.UnitY

        let retreatPos = ctx.entityPos + normalizedDir * 100.0f

        let command: SystemCommunications.SetMovementTarget = {
          EntityId = ctx.controller.controlledEntityId
          Target = retreatPos
        }

        {
          nodeResult = Running
          command = ValueSome command
          abilityIntent = ValueNone
          newState = Chasing
          waypointIndex = ValueNone
        }
      | ValueNone ->
          {
            nodeResult = Failure
            command = ValueNone
            abilityIntent = ValueNone
            newState = ctx.controller.currentState
            waypointIndex = ValueNone
          }

    | "Idle" -> {
        nodeResult = Success
        command = ValueNone
        abilityIntent = ValueNone
        newState = AIState.Idle
        waypointIndex = ValueNone
      }

    | _ ->
        {
          nodeResult = Failure
          command = ValueNone
          abilityIntent = ValueNone
          newState = ctx.controller.currentState
          waypointIndex = ValueNone
        }

  // --- Tree Evaluator ---

  type StackFrame =
    | SelectorFrame of children: BehaviorNode[] * index: int
    | SequenceFrame of children: BehaviorNode[] * index: int
    | InverterFrame

  [<TailCall>]
  let rec processNode
    (node: BehaviorNode)
    (stack: StackFrame list)
    (ctx: ExecutionContext)
    : ActionResult =

    // Default result prototype for new results
    let defaultResult = {
      nodeResult = Failure
      command = ValueNone
      abilityIntent = ValueNone
      newState = ctx.controller.currentState
      waypointIndex = ValueNone
    }

    match node with
    | Selector children ->
      if children.Length = 0 then
        let res = {
          defaultResult with
              nodeResult = Failure
        }

        processResult res stack ctx
      else
        processNode children.[0] (SelectorFrame(children, 0) :: stack) ctx

    | Sequence children ->
      if children.Length = 0 then
        let res = {
          defaultResult with
              nodeResult = Success
        }

        processResult res stack ctx
      else
        processNode children.[0] (SequenceFrame(children, 0) :: stack) ctx

    | Condition(name, parms) ->
      let nodeRes = evaluateCondition name parms ctx

      let res = {
        defaultResult with
            nodeResult = nodeRes
      }

      processResult res stack ctx

    | Action(name, parms) ->
      let res = executeAction name parms ctx
      processResult res stack ctx

    | Inverter child -> processNode child (InverterFrame :: stack) ctx

  and [<TailCall>] processResult
    (res: ActionResult)
    (stack: StackFrame list)
    (ctx: ExecutionContext)
    : ActionResult =
    match stack with
    | [] -> res // Stack empty, return final result

    | SelectorFrame(children, index) :: rest ->
      if res.nodeResult = Success then
        processResult res rest ctx
      else if res.nodeResult = Running then
        processResult res rest ctx
      else
        // Failure: Try next child
        let nextIdx = index + 1

        if nextIdx < children.Length then
          processNode
            children.[nextIdx]
            (SelectorFrame(children, nextIdx) :: rest)
            ctx
        else
          processResult res rest ctx

    | SequenceFrame(children, index) :: rest ->
      if res.nodeResult = Failure then
        processResult res rest ctx
      else if res.nodeResult = Running then
        processResult res rest ctx
      else
        // Success: Try next child
        let nextIdx = index + 1

        if nextIdx < children.Length then
          processNode
            children.[nextIdx]
            (SequenceFrame(children, nextIdx) :: rest)
            ctx
        else
          processResult res rest ctx

    | InverterFrame :: rest ->
      let invertedStatus =
        match res.nodeResult with
        | Success -> Failure
        | Failure -> Success
        | Running -> Running

      let invertedRes = { res with nodeResult = invertedStatus }

      processResult invertedRes rest ctx

  let inline evaluate
    (rootNode: BehaviorNode)
    (ctx: ExecutionContext)
    : ActionResult =
    processNode rootNode [] ctx

module Decision =

  // Helper to find abilities - simplified for now as we don't have full AbilityStore access in the same way
  // We'll assume we can get available skills from the world or passed in services

  let matchCueToPriority (cue: PerceptionCue) (priorities: CuePriority[]) =
    priorities
    |> Array.tryFind(fun p ->
      p.cueType = cue.cueType && cue.strength >= p.minStrength)

  let selectBestCue
    (cues: PerceptionCue[])
    (priorities: CuePriority[])
    : struct (PerceptionCue * CuePriority) voption =
    cues
    |> Array.choose(fun cue ->
      matchCueToPriority cue priorities
      |> Option.map(fun priority -> struct (cue, priority)))
    |> Array.sortBy(fun struct (_, priority) -> priority.priority)
    |> Array.tryHead
    |> ValueOption.ofOption

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
    (ctx: PerceptionContext)
    (skillStore: SkillStore)
    (cooldowns: HashMap<int<SkillId>, TimeSpan> voption)
    =
    let movementCmd =
      Decision.generateCommand cue priority ctx.Controller skillStore

    let state = determineState priority.response cue

    // Try to cast a skill when engaging or investigating (if in range)
    let abilityIntent =
      match priority.response with
      | Engage
      | Investigate ->
        match
          SkillSelection.selectSkill
            ctx.Controller.skills
            skillStore
            cooldowns
            ctx.CurrentTick
            ctx.Entity.Position
            cue.position
        with
        | ValueSome struct (skillId, skill) ->
          ValueSome(
            SkillSelection.createAbilityIntent
              ctx.Controller.controlledEntityId
              skillId
              skill
              cue.sourceEntityId
              cue.position
          )
        | ValueNone -> ValueNone
      | _ -> ValueNone

    struct (movementCmd,
            abilityIntent,
            true,
            ctx.Controller.waypointIndex,
            state)


  let private getNavigateToSpawnCommand(controller: AIController) =
    ValueSome(
      {
        SystemCommunications.SetMovementTarget.EntityId =
          controller.controlledEntityId
        Target = controller.spawnPosition
      }
      : SystemCommunications.SetMovementTarget
    )

  let private handleNoCue
    (controller: AIController)
    (archetype: AIArchetype)
    (currentPos: Vector2)
    =
    let navigateSpawn = getNavigateToSpawnCommand controller

    match controller.absoluteWaypoints with
    | ValueSome waypoints when not(Array.isEmpty waypoints) ->
      // Standardize movement logic using WaypointNavigation
      let struct (targetWaypoint, nextIdx) =
        WaypointNavigation.selectNextWaypoint
          archetype.behaviorType
          controller
          currentPos
          waypoints

      let cmd =
        ValueSome(
          {
            SystemCommunications.SetMovementTarget.EntityId =
              controller.controlledEntityId
            Target = targetWaypoint
          }
          : SystemCommunications.SetMovementTarget
        )

      let desiredState =
        match archetype.behaviorType with
        | Turret
        | Passive -> AIState.Idle
        | _ -> Patrolling

      struct (cmd, ValueNone, true, nextIdx, desiredState)

    | _ ->
      // No waypoints or empty list
      match archetype.behaviorType with
      | Patrol ->
        // Patrol with no waypoints = Idle
        struct (ValueNone,
                ValueNone,
                true,
                controller.waypointIndex,
                AIState.Idle)
      | _ ->
        // Others return to spawn
        struct (navigateSpawn,
                ValueNone,
                true,
                controller.waypointIndex,
                AIState.Idle)

  /// Evaluate a behavior tree and return result in the standard tuple format
  let evaluateWithBehaviorTree
    (tree: DecisionTree voption)
    (ctx: PerceptionContext)
    (world: WorldSnapshot)
    (cues: PerceptionCue[])
    (updatedMemories: HashMap<Guid<EntityId>, MemoryEntry>)
    (skillStore: SkillStore)
    (cooldowns: HashMap<int<SkillId>, TimeSpan> voption)
    =
    match tree with
    | ValueNone ->
      // No tree, use default idle behavior
      struct (ValueNone,
              ValueNone,
              true,
              ctx.Controller.waypointIndex,
              ctx.Controller.currentState)
    | ValueSome behaviorTree ->
      // Find closest target from memories, but use CURRENT position if available
      let targetOpt =
        updatedMemories
        |> HashMap.toSeq
        |> Seq.tryHead
        |> Option.map(fun (id, _entry) ->
          // Prefer real-time position over stale memory position
          let currentPos = world.Positions |> HashMap.tryFindV id

          match currentPos with
          | ValueSome pos -> struct (id, pos)
          | ValueNone -> struct (id, _entry.lastKnownPosition))

      let targetPos, targetId, targetDist =
        match targetOpt with
        | Some struct (id, pos) ->
          let dist = Vector2.Distance(ctx.Entity.Position, pos)
          (ValueSome pos, ValueSome id, ValueSome dist)
        | None -> (ValueNone, ValueNone, ValueNone)

      // Compute best cue using archetype's cuePriorities
      let bestCueOpt = Decision.selectBestCue cues ctx.Archetype.cuePriorities

      let bestCue, cueResponse =
        match bestCueOpt with
        | ValueSome struct (cue, priority) ->
          (ValueSome cue, ValueSome priority.response)
        | ValueNone -> (ValueNone, ValueNone)

      let execCtx: BehaviorTreeExecution.ExecutionContext = {
        controller = ctx.Controller
        archetype = ctx.Archetype
        entityPos = ctx.Entity.Position
        targetPos = targetPos
        targetId = targetId
        targetDistance = targetDist
        skillStore = skillStore
        cooldowns = cooldowns
        currentTime = ctx.CurrentTick
        bestCue = bestCue
        cueResponse = cueResponse
      }

      let result = BehaviorTreeExecution.evaluate behaviorTree.Root execCtx

      let newWaypointIdx =
        result.waypointIndex
        |> ValueOption.defaultValue ctx.Controller.waypointIndex

      struct (result.command,
              result.abilityIntent,
              true,
              newWaypointIdx,
              result.newState)

  let processAndGenerateCommands
    (ctx: PerceptionContext)
    (world: WorldSnapshot)
    (skillStore: SkillStore)
    (decisionTreeStore: DecisionTreeStore)
    (cooldowns: HashMap<Guid<EntityId>, HashMap<int<SkillId>, TimeSpan>>)
    =
    let controller = ctx.Controller
    let archetype = ctx.Archetype
    let pos = ctx.Entity.Position

    let struct (cues, updatedMemories) = Perception.gatherCues ctx world

    let timeSinceLastDecision = ctx.CurrentTick - controller.lastDecisionTime

    let entityCooldowns =
      cooldowns |> HashMap.tryFindV controller.controlledEntityId

    let struct (command, abilityIntent, shouldUpdateTime, newWaypointIndex,
                newState) =
      if timeSinceLastDecision >= archetype.decisionInterval then
        // Try to use behavior tree if available
        let treeOpt = decisionTreeStore.tryFind controller.decisionTree

        match treeOpt with
        | ValueSome tree ->
          evaluateWithBehaviorTree
            (ValueSome tree)
            ctx
            world
            cues
            updatedMemories
            skillStore
            entityCooldowns
        | ValueNone ->
          // Fallback to cue-based decision
          let bestCue = Decision.selectBestCue cues archetype.cuePriorities

          match bestCue with
          | ValueSome struct (cue, priority) ->
            handleBestCue cue priority ctx skillStore entityCooldowns
          | ValueNone -> handleNoCue controller archetype pos
      else
        struct (ValueNone,
                ValueNone,
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
              ctx.CurrentTick
            else
              controller.lastDecisionTime
    }

    struct (updatedController, command, abilityIntent)


open Pomo.Core.Environment
open Pomo.Core.Environment.Patterns

type AISystem(game: Game, env: PomoEnvironment) =
  inherit GameSystem(game)

  let (Core core) = env.CoreServices
  let (Stores stores) = env.StoreServices
  let (Gameplay gameplay) = env.GameplayServices

  let archetypeStore = stores.AIArchetypeStore
  let skillStore = stores.SkillStore
  let decisionTreeStore = stores.DecisionTreeStore

  let fallbackArchetype = {
    id = %0
    name = "Fallback"
    behaviorType = Aggressive
    perceptionConfig = {
      visualRange = 150.0f
      fov = 360.0f
      memoryDuration = TimeSpan.FromSeconds 5.0
      leashDistance = 300.0f
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
    let velocities = core.World.Velocities |> AMap.force
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

      let world: AIContext.WorldSnapshot = {
        Positions = positions
        Velocities = velocities
        Factions = factions
        SpatialGrid = spatialGrid
      }

      for (_, (controllerId, controller)) in group do

        let archetype =
          archetypeStore.tryFind controller.archetypeId
          |> ValueOption.defaultValue fallbackArchetype

        let posOpt = positions |> HashMap.tryFindV controller.controlledEntityId
        let facOpt = factions |> HashMap.tryFindV controller.controlledEntityId

        match posOpt, facOpt with
        | ValueSome pos, ValueSome facs ->
          let vel =
            velocities
            |> HashMap.tryFindV controller.controlledEntityId
            |> ValueOption.defaultValue Vector2.Zero

          let entityCtx: AIContext.EntityContext = {
            EntityId = controller.controlledEntityId
            Position = pos
            Velocity = vel
            Factions = facs
          }

          let ctx: AIContext.PerceptionContext = {
            Controller = controller
            Archetype = archetype
            Entity = entityCtx
            CurrentTick = currentTick
          }

          let struct (updatedController, command, abilityIntent) =
            AISystemLogic.processAndGenerateCommands
              ctx
              world
              skillStore
              decisionTreeStore
              cooldowns

          match command with
          | ValueSome cmd -> core.EventBus.Publish cmd
          | ValueNone -> ()

          match abilityIntent with
          | ValueSome intent -> core.EventBus.Publish intent
          | ValueNone -> ()

          if updatedController <> controller then
            core.EventBus.Publish(
              StateChangeEvent.AI(
                ControllerUpdated struct (controllerId, updatedController)
              )
            )
        | _ -> ()

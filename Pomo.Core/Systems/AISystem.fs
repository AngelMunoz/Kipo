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

  /// Context for target-related data
  [<Struct>]
  type TargetContext = {
    Position: Vector2 voption
    EntityId: Guid<EntityId> voption
    Distance: float32 voption
  }

  /// Context for ability-related dependencies
  [<Struct>]
  type AbilityContext = {
    SkillStore: SkillStore
    Cooldowns: HashMap<int<SkillId>, TimeSpan> voption
    KnownSkills: int<SkillId>[]
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

  /// Unified result of AI decision making
  [<Struct>]
  type AIOutput = {
    Command: SystemCommunications.SetMovementTarget voption
    Ability: SystemCommunications.AbilityIntent voption
    NewState: AIState
    WaypointIndex: int voption
    ShouldUpdateTime: bool
  }

  let inline noOutput (state: AIState) (waypointIdx: int) = {
    Command = ValueNone
    Ability = ValueNone
    NewState = state
    WaypointIndex = ValueSome waypointIdx
    ShouldUpdateTime = false
  }

  let inline failureResult (state: AIState) (waypointIdx: int) = {
    Command = ValueNone
    Ability = ValueNone
    NewState = state
    WaypointIndex = ValueSome waypointIdx
    ShouldUpdateTime = true
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
    (abilityCtx: AbilityContext)
    (currentTime: TimeSpan)
    (casterPos: Vector2)
    (targetPos: Vector2)
    : struct (int<SkillId> * Skill.Skill) voption =
    let skills = abilityCtx.KnownSkills

    let mutable res: struct (int<SkillId> * Skill.Skill) voption = ValueNone
    let mutable i = 0

    while i < skills.Length && res.IsNone do
      let skillId = skills.[i]

      if isSkillReady skillId abilityCtx.Cooldowns currentTime then
        match abilityCtx.SkillStore.tryFind skillId with
        | ValueSome skill ->
          let range = getSkillRange skill

          if isTargetInRange casterPos targetPos range then
            res <- ValueSome(struct (skillId, skill))
        | ValueNone -> ()

      i <- i + 1

    res

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
    (nearbyEntities: Guid<EntityId> seq)
    =
    let cues = ResizeArray<PerceptionCue>()

    for entityId in nearbyEntities do
      match world.Positions |> HashMap.tryFindV entityId with
      | ValueSome pos ->
        match world.Factions |> HashMap.tryFindV entityId with
        | ValueSome targetFactions ->
          let isHostile = isHostileFaction ctx.Entity.Factions targetFactions

          if isHostile then
            let dist = distance ctx.Entity.Position pos

            if dist <= ctx.Archetype.perceptionConfig.visualRange then
              let inFov =
                isInFieldOfView
                  ctx.Entity.Velocity
                  ctx.Entity.Position
                  pos
                  ctx.Archetype.perceptionConfig.fov

              if inFov then
                let strength =
                  if
                    dist < ctx.Archetype.perceptionConfig.visualRange * 0.3f
                  then
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

                cues.Add {
                  cueType = Visual
                  strength = strength
                  sourceEntityId = ValueSome entityId
                  position = pos
                  timestamp = ctx.CurrentTick
                }
        | ValueNone -> ()
      | ValueNone -> ()

    cues.ToArray()

  let decayMemories(ctx: PerceptionContext) =
    ctx.Controller.memories
    |> HashMap.chooseV(fun _ entry ->
      let age = ctx.CurrentTick - entry.lastSeenTick

      let distToSpawn =
        Vector2.Distance(ctx.Entity.Position, ctx.Controller.spawnPosition)

      let isLeashed =
        distToSpawn <= ctx.Archetype.perceptionConfig.leashDistance

      if age < ctx.Archetype.perceptionConfig.memoryDuration && isLeashed then
        let decayFactor =
          1.0f
          - (float32 age.TotalSeconds
             / float32
                 ctx.Archetype.perceptionConfig.memoryDuration.TotalSeconds)

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

    let seen = System.Collections.Generic.HashSet<Guid<EntityId>>()

    let nearbyEntities = seq {
      for cell in cells do
        match world.SpatialGrid |> HashMap.tryFindV cell with
        | ValueSome list ->
          for entityId in list do
            if seen.Add entityId then
              yield entityId
        | ValueNone -> ()
    }

    let visualCues = gatherVisualCues ctx world nearbyEntities

    let decayedMemories = decayMemories ctx

    let updatedMemories =
      let mutable mem = decayedMemories

      for cue in visualCues do
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

          mem <- mem.Add(entityId, entry)
        | ValueNone -> ()

      mem

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

    let allCues = Array.concat [ visualCues; memoryCues ]

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
  [<Struct>]
  type TreeExecutionContext = {
    Perception: PerceptionContext
    Target: TargetContext
    Ability: AbilityContext
    BestCue: PerceptionCue voption
    Response: ResponseType voption
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
    (ctx: TreeExecutionContext)
    : NodeResult =
    match name with
    | "HasTarget" -> if ctx.Target.EntityId.IsSome then Success else Failure
    | "TargetInRange" ->
      match ctx.Target.Distance with
      | ValueSome dist ->
        let range =
          getParam
            "Range"
            parms
            ctx.Perception.Archetype.perceptionConfig.visualRange

        if dist <= range then Success else Failure
      | ValueNone -> Failure
    | "TargetInMeleeRange" ->
      match ctx.Target.Distance with
      | ValueSome dist -> if dist <= 48.0f then Success else Failure
      | ValueNone -> Failure
    | "TargetTooClose" ->
      match ctx.Target.Distance with
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
      let dist =
        Vector2.Distance(
          ctx.Perception.Entity.Position,
          ctx.Perception.Controller.spawnPosition
        )

      if dist > ctx.Perception.Archetype.perceptionConfig.leashDistance then
        Success
      else
        Failure
    | "SkillReady" ->
      let hasReady =
        ctx.Ability.KnownSkills
        |> Array.exists(fun skillId ->
          SkillSelection.isSkillReady
            skillId
            ctx.Ability.Cooldowns
            ctx.Perception.CurrentTick)

      if hasReady then Success else Failure
    | "HasCue" -> if ctx.BestCue.IsSome then Success else Failure
    | "CueResponseIs" ->
      match ctx.Response with
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
    (_parms: HashMap<string, string>) // Cleaned unused parameter
    (ctx: TreeExecutionContext)
    : struct (NodeResult * AIOutput) =

    let defaultFail =
      struct (Failure,
              failureResult
                ctx.Perception.Controller.currentState
                ctx.Perception.Controller.waypointIndex)

    let defaultSuccess =
      struct (Success,
              noOutput
                ctx.Perception.Controller.currentState
                ctx.Perception.Controller.waypointIndex)

    match name with
    | "ChaseTarget" ->
      match ctx.Target.Position with
      | ValueSome targetPos ->
        let command: SystemCommunications.SetMovementTarget = {
          EntityId = ctx.Perception.Controller.controlledEntityId
          Target = targetPos
        }

        let output = {
          Command = ValueSome command
          Ability = ValueNone
          NewState = Chasing
          WaypointIndex = ValueNone
          ShouldUpdateTime = true
        }

        struct (Running, output)
      | ValueNone -> defaultFail

    | "UseRangedAttack"
    | "UseMeleeAttack"
    | "UseHeal"
    | "UseDebuff"
    | "UseBuff" ->
      match ctx.Target.Position, ctx.Target.EntityId with
      | ValueSome targetPos, targetIdOpt ->
        match
          SkillSelection.selectSkill
            ctx.Ability
            ctx.Perception.CurrentTick
            ctx.Perception.Entity.Position
            targetPos
        with
        | ValueSome struct (skillId, skill) ->
          let intent =
            SkillSelection.createAbilityIntent
              ctx.Perception.Controller.controlledEntityId
              skillId
              skill
              targetIdOpt
              targetPos // Passing Target Pos

          let output = {
            Command = ValueNone
            Ability = ValueSome intent
            NewState = Attacking
            WaypointIndex = ValueNone
            ShouldUpdateTime = true
          }

          struct (Success, output)
        | ValueNone ->
          // Fallback: move to target
          let command: SystemCommunications.SetMovementTarget = {
            EntityId = ctx.Perception.Controller.controlledEntityId
            Target = targetPos
          }

          let output = {
            Command = ValueSome command
            Ability = ValueNone
            NewState = Chasing
            WaypointIndex = ValueNone
            ShouldUpdateTime = true
          }

          struct (Running, output)
      | _ -> defaultFail

    | "Patrol" ->
      match ctx.Perception.Controller.absoluteWaypoints with
      | ValueSome waypoints when waypoints.Length > 0 ->
        let struct (targetWaypoint, nextIdx) =
          WaypointNavigation.selectNextWaypoint
            ctx.Perception.Archetype.behaviorType
            ctx.Perception.Controller
            ctx.Perception.Entity.Position
            waypoints

        let command: SystemCommunications.SetMovementTarget = {
          EntityId = ctx.Perception.Controller.controlledEntityId
          Target = targetWaypoint
        }

        let output = {
          Command = ValueSome command
          Ability = ValueNone
          NewState = Patrolling
          WaypointIndex = ValueSome nextIdx
          ShouldUpdateTime = true
        }

        struct (Running, output)
      | _ -> defaultSuccess

    | "ReturnToSpawn" ->
      let command: SystemCommunications.SetMovementTarget = {
        EntityId = ctx.Perception.Controller.controlledEntityId
        Target = ctx.Perception.Controller.spawnPosition
      }

      let output = {
        Command = ValueSome command
        Ability = ValueNone
        NewState = Patrolling
        WaypointIndex = ValueNone
        ShouldUpdateTime = true
      }

      struct (Running, output)

    | "Retreat" ->
      match ctx.Target.Position with
      | ValueSome targetPos ->
        let direction = ctx.Perception.Entity.Position - targetPos

        let normalizedDir =
          if direction.LengthSquared() > 0.0f then
            Vector2.Normalize(direction)
          else
            Vector2.UnitY

        let retreatPos = ctx.Perception.Entity.Position + normalizedDir * 100.0f

        let command: SystemCommunications.SetMovementTarget = {
          EntityId = ctx.Perception.Controller.controlledEntityId
          Target = retreatPos
        }

        let output = {
          Command = ValueSome command
          Ability = ValueNone
          NewState = Chasing
          WaypointIndex = ValueNone
          ShouldUpdateTime = true
        }

        struct (Running, output)
      | ValueNone -> defaultFail

    | "Idle" -> defaultSuccess

    | _ -> defaultFail

  // --- Tree Evaluator ---

  [<Struct>]
  type StackFrame =
    | SelectorFrame of struct (BehaviorNode[] * int)
    | SequenceFrame of struct (BehaviorNode[] * int)
    | InverterFrame

  module private Evaluator =
    let inline failureOutput(ctx: TreeExecutionContext) =
      failureResult
        ctx.Perception.Controller.currentState
        ctx.Perception.Controller.waypointIndex

    let inline successOutput(ctx: TreeExecutionContext) =
      noOutput
        ctx.Perception.Controller.currentState
        ctx.Perception.Controller.waypointIndex

    let inline setFailure
      (nodeRes: byref<NodeResult>)
      (output: byref<AIOutput>)
      (descend: byref<bool>)
      (failureOut: AIOutput)
      =
      nodeRes <- Failure
      output <- failureOut
      descend <- false

    let inline setSuccess
      (nodeRes: byref<NodeResult>)
      (output: byref<AIOutput>)
      (descend: byref<bool>)
      (successOut: AIOutput)
      =
      nodeRes <- Success
      output <- successOut
      descend <- false


    let inline goSelector
      (node: byref<BehaviorNode>)
      (stack: System.Collections.Generic.Stack<StackFrame>)
      (children: BehaviorNode[])
      =
      stack.Push(SelectorFrame(children, 0))
      node <- children.[0]

    let inline goSequence
      (node: byref<BehaviorNode>)
      (stack: System.Collections.Generic.Stack<StackFrame>)
      (children: BehaviorNode[])
      =
      stack.Push(SequenceFrame(children, 0))
      node <- children.[0]

    let inline goInverter
      (node: byref<BehaviorNode>)
      (stack: System.Collections.Generic.Stack<StackFrame>)
      (child: BehaviorNode)
      =
      stack.Push InverterFrame
      node <- child

    let inline invertResult res =
      match res with
      | Success -> Failure
      | Failure -> Success
      | Running -> Running

    let inline backtrackToNextChild
      (stack: System.Collections.Generic.Stack<StackFrame>)
      (nodeRes: byref<NodeResult>)
      (node: byref<BehaviorNode>)
      (descend: byref<bool>)
      (running: byref<bool>)
      =
      let mutable searching = true

      while searching && running && not descend do
        if stack.Count = 0 then
          running <- false
          searching <- false
        else
          match stack.Pop() with
          | InverterFrame -> nodeRes <- invertResult nodeRes

          | SelectorFrame(children, index) ->
            if nodeRes = Success || nodeRes = Running then
              ()
            else
              let nextIdx = index + 1

              if nextIdx < children.Length then
                stack.Push(SelectorFrame(children, nextIdx))
                node <- children.[nextIdx]
                descend <- true
                searching <- false

          | SequenceFrame(children, index) ->
            if nodeRes = Failure || nodeRes = Running then
              ()
            else
              let nextIdx = index + 1

              if nextIdx < children.Length then
                stack.Push(SequenceFrame(children, nextIdx))
                node <- children.[nextIdx]
                descend <- true
                searching <- false

  let evaluate
    (rootNode: BehaviorNode)
    (ctx: TreeExecutionContext)
    : struct (NodeResult * AIOutput) =
    let stack = System.Collections.Generic.Stack<StackFrame>(16)

    let failureOut = Evaluator.failureOutput ctx
    let successOut = Evaluator.successOutput ctx

    let mutable node = rootNode
    let mutable nodeRes = Failure
    let mutable output = failureOut

    let mutable running = true
    let mutable descend = true


    while running do
      while descend do
        match node with
        | Selector children ->
          if children.Length = 0 then
            Evaluator.setFailure &nodeRes &output &descend failureOut
          else
            Evaluator.goSelector &node stack children

        | Sequence children ->
          if children.Length = 0 then
            Evaluator.setSuccess &nodeRes &output &descend successOut
          else
            Evaluator.goSequence &node stack children

        | Condition(name, parms) ->
          let res = evaluateCondition name parms ctx
          nodeRes <- res
          output <- if res = Success then successOut else failureOut
          descend <- false

        | Action(name, parms) ->
          let struct (res, out) = executeAction name parms ctx
          nodeRes <- res
          output <- out
          descend <- false
        | Inverter child -> Evaluator.goInverter &node stack child

      descend <- false
      Evaluator.backtrackToNextChild stack &nodeRes &node &descend &running

    struct (nodeRes, output)

module Decision =

  // Helper to find abilities - simplified for now as we don't have full AbilityStore access in the same way
  // We'll assume we can get available skills from the world or passed in services

  let inline matchCueToPriority
    (cue: PerceptionCue)
    (priorities: CuePriority[])
    =
    priorities
    |> Array.tryFind(fun p ->
      p.cueType = cue.cueType && cue.strength >= p.minStrength)

  let selectBestCue
    (cues: PerceptionCue[])
    (priorities: CuePriority[])
    : struct (PerceptionCue * CuePriority) voption =
    let mutable bestResult = ValueNone

    // Scan all cues
    for i = 0 to cues.Length - 1 do
      let cue = cues.[i]
      // Find matching priority for this cue
      let mutable matchingPriority = ValueNone

      for j = 0 to priorities.Length - 1 do
        let p = priorities.[j]

        if
          matchingPriority.IsNone
          && p.cueType = cue.cueType
          && cue.strength >= p.minStrength
        then
          matchingPriority <- ValueSome p

      // If we found a priority match, see if it's better than our current best
      match matchingPriority with
      | ValueSome mp ->
        match bestResult with
        | ValueNone -> bestResult <- ValueSome struct (cue, mp)
        | ValueSome struct (_, currentBestP) ->
          if mp.priority < currentBestP.priority then
            bestResult <- ValueSome struct (cue, mp)
      | ValueNone -> ()

    bestResult

  // Simplified command generation
  let generateCommand
    (cue: PerceptionCue)
    (priority: CuePriority)
    (controller: AIController)
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
    (abilityCtx: AbilityContext)
    =
    let movementCmd = Decision.generateCommand cue priority ctx.Controller

    let state = determineState priority.response cue

    // Try to cast a skill when engaging or investigating (if in range)
    let abilityIntent =
      match priority.response with
      | Engage
      | Investigate ->
        match
          SkillSelection.selectSkill
            abilityCtx
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

    {
      Command = movementCmd
      Ability = abilityIntent
      NewState = state
      WaypointIndex = ValueSome ctx.Controller.waypointIndex
      ShouldUpdateTime = true
    }

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
    : AIOutput =
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

      {
        Command = cmd
        Ability = ValueNone
        NewState = desiredState
        WaypointIndex = ValueSome nextIdx
        ShouldUpdateTime = true
      }

    | _ ->
      // No waypoints or empty list
      match archetype.behaviorType with
      | Patrol ->
        // Patrol with no waypoints = Idle
        noOutput controller.currentState controller.waypointIndex
      | _ ->
          // Others return to spawn
          {
            Command = navigateSpawn
            Ability = ValueNone
            NewState = AIState.Idle
            WaypointIndex = ValueSome controller.waypointIndex
            ShouldUpdateTime = true
          }

  /// Evaluate a behavior tree and return result in the standard tuple format
  let evaluateWithBehaviorTree
    (tree: DecisionTree voption)
    (ctx: PerceptionContext)
    (world: WorldSnapshot)
    (cues: PerceptionCue[])
    (updatedMemories: HashMap<Guid<EntityId>, MemoryEntry>)
    (abilityCtx: AbilityContext)
    : AIOutput =
    match tree with
    | ValueNone ->
      // No tree, use default idle behavior
      noOutput ctx.Controller.currentState ctx.Controller.waypointIndex
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

      let execCtx: BehaviorTreeExecution.TreeExecutionContext = {
        Perception = ctx
        Target = {
          Position = targetPos
          EntityId = targetId
          Distance = targetDist
        }
        Ability = abilityCtx
        BestCue = bestCue
        Response = cueResponse
      }

      let struct (_, output) =
        BehaviorTreeExecution.evaluate behaviorTree.Root execCtx

      // Ensure we preserve waypoint index if not set
      let newWaypointIdx =
        output.WaypointIndex
        |> ValueOption.defaultValue ctx.Controller.waypointIndex

      {
        output with
            WaypointIndex = ValueSome newWaypointIdx
      }

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
    let timeSinceLastDecision = ctx.CurrentTick - controller.lastDecisionTime

    let entityCooldowns =
      cooldowns |> HashMap.tryFindV controller.controlledEntityId

    let abilityCtx: AbilityContext = {
      SkillStore = skillStore
      Cooldowns = entityCooldowns
      KnownSkills = controller.skills
    }

    if timeSinceLastDecision < archetype.decisionInterval then
      struct (controller, ValueNone, ValueNone)
    else
      let struct (cues, updatedMemories) = Perception.gatherCues ctx world

      let result =
        let treeOpt = decisionTreeStore.tryFind controller.decisionTree

        match treeOpt with
        | ValueSome tree ->
          evaluateWithBehaviorTree
            (ValueSome tree)
            ctx
            world
            cues
            updatedMemories
            abilityCtx
        | ValueNone ->
          let bestCue = Decision.selectBestCue cues archetype.cuePriorities

          match bestCue with
          | ValueSome struct (cue, priority) ->
            handleBestCue cue priority ctx abilityCtx
          | ValueNone -> handleNoCue controller archetype pos

      let updatedController = {
        controller with
            memories = updatedMemories
            waypointIndex =
              result.WaypointIndex
              |> ValueOption.defaultValue controller.waypointIndex
            currentState = result.NewState
            lastDecisionTime =
              if result.ShouldUpdateTime then
                ctx.CurrentTick
              else
                controller.lastDecisionTime
      }

      struct (updatedController, result.Command, result.Ability)


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
      let mutable worldOpt: AIContext.WorldSnapshot voption = ValueNone

      for (_, (controllerId, controller)) in group do

        let archetype =
          archetypeStore.tryFind controller.archetypeId
          |> ValueOption.defaultValue fallbackArchetype

        let timeSinceLastDecision = currentTick - controller.lastDecisionTime

        if timeSinceLastDecision >= archetype.decisionInterval then

          let world =
            match worldOpt with
            | ValueSome w -> w
            | ValueNone ->
              let snapshot =
                gameplay.Projections.ComputeMovementSnapshot(scenarioId)

              let positions = snapshot.Positions
              let spatialGrid = snapshot.SpatialGrid

              let w: AIContext.WorldSnapshot = {
                Positions = positions
                Velocities = velocities
                Factions = factions
                SpatialGrid = spatialGrid
              }

              worldOpt <- ValueSome w
              w

          let posOpt =
            world.Positions |> HashMap.tryFindV controller.controlledEntityId

          let facOpt =
            factions |> HashMap.tryFindV controller.controlledEntityId

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

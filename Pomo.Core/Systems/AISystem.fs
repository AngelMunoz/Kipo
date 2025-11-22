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
    (positions: amap<Guid<EntityId>, Vector2>)
    (factions: amap<Guid<EntityId>, Faction HashSet>)
    (currentTick: TimeSpan)
    =
    adaptive {
      let cues =
        positions
        |> AMap.chooseA(fun entityId pos -> adaptive {
          let! factions = factions |> AMap.tryFind entityId

          match factions with
          | Some targetFactions ->
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

              return
                Some {
                  cueType = Visual
                  strength = strength
                  sourceEntityId = ValueSome entityId
                  position = pos
                  timestamp = currentTick
                }
            else
              return None
          | _ -> return None
        })

      let! gatheredCues =
        cues
        |> AMap.fold
          (fun (acc: ResizeArray<_>) _ cue ->
            acc.Add cue
            acc)
          (ResizeArray())

      return gatheredCues.ToArray()
    }

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
    (world: World)
    (controllerEntityPos: Vector2)
    (controllerEntityFactions: Faction HashSet)
    (currentTick: TimeSpan aval)
    =
    adaptive {
      let! currentTick = currentTick

      let! visualCues =
        gatherVisualCues
          controllerEntityPos
          controllerEntityFactions
          archetype.perceptionConfig
          world.Positions
          world.Factions
          currentTick

      let decayedMemories =
        decayMemories
          controller.memories
          currentTick
          archetype.perceptionConfig.memoryDuration

      let updatedMemories =
        visualCues
        |> Array.fold
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
        |> HashMap.toArray
        |> Array.map(fun (entityId, memoryEntry) ->
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

      let allCues = Array.concat [ visualCues; memoryCues ]

      return struct (allCues, updatedMemories)
    }

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
      let hasReached = dist < Core.Constants.AI.WaypointReachedThreshold // Threshold

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
    (skillStore: SkillStore)
    =
    let cmd = Decision.generateCommand cue priority controller skillStore
    let state = determineState priority.response cue
    struct (cmd, true, controller.waypointIndex, state)

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

    struct (cmd, true, nextIdx, Patrolling)

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

    struct (cmd, true, controller.waypointIndex, Patrolling)

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
        struct (ValueNone, true, controller.waypointIndex, AIState.Idle)
      | Aggressive
      | Defensive
      | Supporter
      | Ambusher
      | Turret
      | Passive ->
        struct (navigateSpawn, true, controller.waypointIndex, AIState.Idle)

    | ValueSome waypoints ->
      // Waypoints available behavior
      match archetype.behaviorType with
      | Patrol -> handlePatrol controller currentPos waypoints
      | Aggressive -> handleAggressive controller waypoints
      | Defensive
      | Supporter
      | Ambusher
      | Passive ->
        struct (navigateSpawn, true, controller.waypointIndex, AIState.Idle)
      | Turret ->
        struct (ValueNone, true, controller.waypointIndex, AIState.Idle)

  let processAndGenerateCommands
    (controller: AIController)
    (archetype: AIArchetype)
    (world: World)
    (skillStore: SkillStore)
    (currentTickAval: TimeSpan aval)
    =
    adaptive {
      let positions = world.Positions
      let factions = world.Factions
      let! currentTick = currentTickAval

      let! controlledPosition =
        positions |> AMap.tryFind controller.controlledEntityId

      let! controlledFactions =
        factions |> AMap.tryFind controller.controlledEntityId

      match controlledPosition, controlledFactions with
      | Some pos, Some facs ->
        let! struct (cues, updatedMemories) =
          Perception.gatherCues
            controller
            archetype
            world
            pos
            facs
            currentTickAval

        let timeSinceLastDecision = currentTick - controller.lastDecisionTime

        let struct (command, shouldUpdateTime, newWaypointIndex, newState) =
          if timeSinceLastDecision >= archetype.decisionInterval then
            let bestCue = Decision.selectBestCue cues archetype.cuePriorities

            match bestCue with
            | Some struct (cue, priority) ->
              handleBestCue cue priority controller skillStore
            | None -> handleNoCue controller archetype pos
          else
            struct (ValueNone,
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

        return struct (updatedController, command)

      | _ -> return struct (controller, ValueNone)
    }


type AISystem
  (
    game: Game,
    world: World.World,
    eventBus: EventBus,
    skillStore: SkillStore,
    archetypeStore: AIArchetypeStore
  ) =
  inherit GameComponent(game)

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

  let adaptiveLogic =
    world.AIControllers
    |> AMap.mapA(fun _ controller ->
      let archetype =
        archetypeStore.tryFind controller.archetypeId
        |> ValueOption.defaultValue fallbackArchetype

      AISystemLogic.processAndGenerateCommands
        controller
        archetype
        world
        skillStore
        (world.Time |> AVal.map(fun t -> t.TotalGameTime)))

  override this.Update(gameTime) =
    let results = adaptiveLogic |> AMap.force

    for id, struct (updatedController, command) in results do
      match command with
      | ValueSome cmd ->
        // cmd is inferred as SystemCommunications.SetMovementTarget
        eventBus.Publish cmd
      | ValueNone -> ()

      // Only publish if state changed or significant update occurred
      // Optimization: Check for equality before publishing
      let currentControllers = world.AIControllers |> AMap.force

      if updatedController <> currentControllers[id] then
        eventBus.Publish(AI(ControllerUpdated struct (id, updatedController)))

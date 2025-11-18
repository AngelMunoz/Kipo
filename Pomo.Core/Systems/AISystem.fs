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
    let isEnemy = controllerFactions.Contains Faction.Enemy
    let isAlly = controllerFactions.Contains Faction.Ally
    let targetIsPlayer = targetFactions.Contains Faction.Player
    let targetIsAlly = targetFactions.Contains Faction.Ally
    let targetIsEnemy = targetFactions.Contains Faction.Enemy
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
      let! positions = positions.Content
      let! factions = factions.Content

      let cues =
        positions
        |> HashMap.choose(fun entityId pos ->
          match HashMap.tryFind entityId factions with
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

              Some {
                cueType = Visual
                strength = strength
                sourceEntityId = ValueSome entityId
                position = pos
                timestamp = currentTick
              }
            else
              None
          | _ -> None)

      let gatheredCues =
        cues
        |> HashMap.fold
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
      let hasReached = dist < 64.0f // Threshold

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


  let processAndGenerateCommands
    (controller: AIController)
    (archetype: AIArchetype)
    (world: World)
    (skillStore: SkillStore)
    (currentTick: TimeSpan aval)
    =
    adaptive {
      let! positions = world.Positions.Content
      let! factions = world.Factions.Content

      match
        HashMap.tryFind controller.controlledEntityId positions,
        HashMap.tryFind controller.controlledEntityId factions
      with
      | Some pos, Some facs ->
        let! struct (cues, updatedMemories) =
          Perception.gatherCues controller archetype world pos facs currentTick

        let! currentTick = currentTick
        let timeSinceLastDecision = currentTick - controller.lastDecisionTime

        let struct (command, shouldUpdateTime, newWaypointIndex) =
          if timeSinceLastDecision >= archetype.decisionInterval then
            let bestCue = Decision.selectBestCue cues archetype.cuePriorities

            match bestCue with
            | Some struct (cue, priority) ->
              let cmd =
                Decision.generateCommand cue priority controller skillStore

              struct (cmd, true, controller.waypointIndex)
            | None ->
              let navigateSpawn =
                ValueSome(
                  {
                    SystemCommunications.SetMovementTarget.EntityId =
                      controller.controlledEntityId
                    Target = controller.spawnPosition
                  }
                  : SystemCommunications.SetMovementTarget
                )

              match controller.absoluteWaypoints with
              | ValueNone
              | ValueSome [||] ->
                match archetype.behaviorType with
                | Patrol -> struct (ValueNone, true, controller.waypointIndex)
                | Aggressive
                | Defensive
                | Supporter
                | Ambusher
                | Turret
                | Passive ->
                  struct (navigateSpawn, true, controller.waypointIndex)
              | ValueSome waypoints ->
                match archetype.behaviorType with
                | Patrol ->
                  let struct (targetWaypoint, nextIdx) =
                    selectNextWaypoint
                      archetype.behaviorType
                      controller
                      pos
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

                  struct (cmd, true, nextIdx)
                | Aggressive ->
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

                  struct (cmd, true, controller.waypointIndex)
                | Defensive
                | Supporter
                | Ambusher
                | Passive ->
                  struct (navigateSpawn, true, controller.waypointIndex)
                | Turret -> struct (ValueNone, true, controller.waypointIndex)
          else
            struct (ValueNone, false, controller.waypointIndex)

        let updatedController = {
          controller with
              memories = updatedMemories
              waypointIndex = newWaypointIndex
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
  (game: Game, world: World, eventBus: EventBus, skillStore: SkillStore) =
  inherit GameComponent(game)

  let defaultArchetype = {
    id = %1
    name = "Basic Enemy"
    behaviorType = Aggressive
    perceptionConfig = {
      visualRange = 150.0f
      fov = 360.0f
      memoryDuration = TimeSpan.FromSeconds(5.0)
    }
    cuePriorities = [|
      {
        cueType = Visual
        minStrength = Weak
        priority = 10
        response = Engage
      }
    |]
    decisionInterval = TimeSpan.FromSeconds(0.5)
  }

  let adaptiveLogic =
    world.AIControllers
    |> AMap.mapA(fun _ controller ->
      AISystemLogic.processAndGenerateCommands
        controller
        defaultArchetype
        world
        skillStore
        (world.Time |> AVal.map(fun t -> t.TotalGameTime)))

  override this.Update(gameTime) =
    let results = adaptiveLogic |> AMap.force

    // For now, I will just publish the commands.
    // Updating the AIController state (memories, etc.) is tricky without an event.
    // I'll add a TODO to handle state persistence properly via events.

    for id, struct (updatedController, command) in results do
      match command with
      | ValueSome cmd ->
        // cmd is inferred as SystemCommunications.SetMovementTarget
        eventBus.Publish cmd
      | ValueNone -> ()

      eventBus.Publish(
        StateChangeEvent.AI(
          AIStateChange.ControllerUpdated struct (id, updatedController)
        )
      )

namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Action
open Pomo.Core.Systems.Systems

module PlayerMovement =

  module Projections =
    let PlayerVelocity
      (world: World)
      (playerId: Guid<EntityId>)
      (speed: aval<float32>)
      =
      let actions =
        world.GameActionStates
        |> AMap.tryFind playerId
        |> AVal.map(Option.defaultValue HashMap.empty)

      AVal.map2
        (fun actions speed ->
          let moveDir =
            actions
            |> HashMap.fold
              (fun acc action _ ->
                match action with
                | MoveUp -> acc - Vector2.UnitY
                | MoveDown -> acc + Vector2.UnitY
                | MoveLeft -> acc - Vector2.UnitX
                | MoveRight -> acc + Vector2.UnitX
                | _ -> acc)
              Vector2.Zero

          if moveDir.LengthSquared() > 0.0f then
            let normalized = Vector2.Normalize moveDir
            // Ground movement: XZ velocity with Y=0
            Vector3(normalized.X * speed, 0.0f, normalized.Y * speed)
          else
            Vector3.Zero)
        actions
        speed

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns
  open Pomo.Core.Projections
  open System.Collections.Generic

  type PlayerMovementSystem
    (game: Game, env: PomoEnvironment, playerId: Guid<EntityId>) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let stateWrite = core.StateWrite

    let playerCombatStatuses =
      gameplay.Projections.CombatStatuses
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue IndexList.empty)

    let movementSpeed =
      gameplay.Projections.DerivedStats
      |> AMap.tryFind playerId
      |> AVal.map(fun statsOpt ->
        statsOpt |> Option.map(_.MS >> float32) |> Option.defaultValue 100.0f)

    let velocity = Projections.PlayerVelocity core.World playerId movementSpeed

    let movementState = core.World.MovementStates |> AMap.tryFind playerId

    let mutable lastVelocity = Vector3.Zero

    override _.Initialize() = base.Initialize()

    override this.Update _ =
      let entityScenarios = gameplay.Projections.EntityScenarios |> AMap.force

      let snapshot =
        match entityScenarios |> HashMap.tryFindV playerId with
        | ValueSome scenarioId ->
          gameplay.Projections.ComputeMovement3DSnapshot(scenarioId)
        | ValueNone -> Movement3DSnapshot.Empty

      let currentVelocity = velocity |> AVal.force
      let statuses = playerCombatStatuses |> AVal.force

      let isStunned = statuses |> IndexList.exists(fun _ s -> s.IsStunned)
      let isRooted = statuses |> IndexList.exists(fun _ s -> s.IsRooted)

      if isStunned || isRooted then
        if lastVelocity <> Vector3.Zero then
          lastVelocity <-
            MovementLogic3D.notifyVelocityChange3D
              playerId
              Vector3.Zero
              lastVelocity
              stateWrite
      else
        let movementState = movementState |> AVal.force

        let position =
          snapshot.Positions
          |> Dictionary.tryFindV playerId
          |> ValueOption.defaultValue WorldPosition.zero

        let movementSpeed = movementSpeed |> AVal.force

        match movementState with
        | Some(MovingTo destination) ->
          match
            MovementLogic3D.handleMovingTo3D position destination movementSpeed
          with
          | MovementLogic3D.Arrived3D ->
            MovementLogic3D.notifyArrived3D playerId stateWrite core.EventBus
            lastVelocity <- Vector3.Zero
          | MovementLogic3D.Moving3D finalVelocity ->
            lastVelocity <-
              MovementLogic3D.notifyVelocityChange3D
                playerId
                finalVelocity
                lastVelocity
                stateWrite
          | _ -> ()

        | Some(MovingAlongPath path) ->
          match
            MovementLogic3D.handleMovingAlongPath3D position path movementSpeed
          with
          | MovementLogic3D.Arrived3D ->
            MovementLogic3D.notifyArrived3D playerId stateWrite core.EventBus
            lastVelocity <- Vector3.Zero
          | MovementLogic3D.WaypointReached3D remainingPath ->
            MovementLogic3D.notifyWaypointReached3D
              playerId
              remainingPath
              stateWrite
              core.EventBus
          | MovementLogic3D.Moving3D finalVelocity ->
            lastVelocity <-
              MovementLogic3D.notifyVelocityChange3D
                playerId
                finalVelocity
                lastVelocity
                stateWrite

        | Some Idle ->
          lastVelocity <-
            MovementLogic3D.notifyVelocityChange3D
              playerId
              currentVelocity
              lastVelocity
              stateWrite
        | None -> ()

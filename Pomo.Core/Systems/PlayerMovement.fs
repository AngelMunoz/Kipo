namespace Pomo.Core.Domains

open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain.Units
open Pomo.Core.Domain
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.EventBus
open Pomo.Core.Domain.Action

module PlayerMovement =

  module Projections =
    let PlayerVelocity
      (world: World)
      (playerId: Guid<EntityId>)
      (speed: float32)
      =
      let actionStates =
        world.GameActionStates
        |> AMap.tryFind playerId
        |> AVal.map(Option.defaultValue HashMap.empty)
        |> AMap.ofAVal

      actionStates
      |> AMap.fold
        (fun acc action _ ->
          let mutable move = Vector2.Zero

          match action with
          | MoveUp -> move <- move - Vector2.UnitY
          | MoveDown -> move <- move + Vector2.UnitY
          | MoveLeft -> move <- move - Vector2.UnitX
          | MoveRight -> move <- move + Vector2.UnitX
          | _ -> ()

          if move.LengthSquared() > 0.0f then
            acc + Vector2.Normalize(move) * speed
          else
            Vector2.Zero)
        Vector2.Zero

  type PlayerMovementSystem(game: Game, playerId: Guid<EntityId>) as this =
    inherit GameSystem(game)

    let speed = 100.0f

    // Use the projection
    let velocity = Projections.PlayerVelocity this.World playerId speed

    // This is a simple, non-adaptive way to track the last published value.
    // A mutable variable scoped to the system is acceptable for this kind of internal bookkeeping.
    let mutable lastVelocity = Vector2.Zero

    override this.Update _ =
      let currentVelocity = velocity |> AVal.force

      if currentVelocity <> lastVelocity then
        this.EventBus.Publish(
          VelocityChanged struct (playerId, currentVelocity)
        )

        lastVelocity <- currentVelocity

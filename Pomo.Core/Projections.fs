namespace Pomo.Core

open Microsoft.Xna.Framework
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World

module Projections =
  let Movements(world: World) =
    (world.Velocities, world.Positions)
    ||> AMap.choose2V(fun id velocity position ->
      match velocity, position with
      | ValueSome vel, ValueSome pos ->
        ValueSome struct (vel, pos)
      | _ -> ValueNone)

  let UpdatedPositions(world: World) =
    Movements world
    |> AMap.mapA(fun _ struct (velocity, position) -> adaptive {
      let! time = world.DeltaTime
      let displacement = velocity * float32 time.TotalSeconds
      return position + displacement
    })

  let PositionByPlayer (world: World) playerId =
    world.Positions |> AMap.tryFind playerId

namespace Pomo.Core

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

  let PositionByPlayer (world: World) playerId =
    world.Positions |> AMap.tryFind playerId

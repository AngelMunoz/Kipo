namespace Pomo.Core

open System
open FSharp.UMX
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain
open Pomo.Core.Domain.World

module Projections =

  let UpdatedPositions(world: World) =
    (world.Velocities, world.Positions)
    ||> AMap.choose2V(fun _ velocity position ->
      match velocity, position with
      | ValueSome vel, ValueSome pos -> ValueSome struct (vel, pos)
      | _ -> ValueNone)
    |> AMap.mapA(fun _ struct (velocity, position) -> adaptive {
      let! time = world.DeltaTime
      let displacement = velocity * float32 time.TotalSeconds
      return position + displacement
    })

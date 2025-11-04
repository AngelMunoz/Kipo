namespace Pomo.Core.Domains

open System
open Microsoft.Xna.Framework

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.EventBus


module Movement =

  type MovementSystem(game: Game) as this =
    inherit GameSystem(game)

    let movements =
      Projections.Movements this.World
      |> AMap.mapA(fun _ struct (velocity, position) -> adaptive {
        let! time = this.World.DeltaTime
        let displacement = velocity * float32 time.TotalSeconds
        return position + displacement
      })

    override val Kind = Movement with get

    override this.Update gameTime =
      let movements = movements |> AMap.force

      for id, movement in movements do
        this.EventBus.Publish(PositionChanged struct (id, movement))

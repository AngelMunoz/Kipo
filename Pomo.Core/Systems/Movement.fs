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

    let updatedPositions = Projections.UpdatedPositions this.World

    override val Kind = Movement with get

    override this.Update gameTime =
      let movements = updatedPositions |> AMap.force

      for id, newPosition in movements do
        this.EventBus.Publish(PositionChanged struct (id, newPosition))

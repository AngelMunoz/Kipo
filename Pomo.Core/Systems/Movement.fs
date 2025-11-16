namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain.Events
open Pomo.Core.Systems


module Movement =

  type MovementSystem(game: Game) =
    inherit GameSystem(game)

    override val Kind = Movement with get

    override this.Update _ =
      let movements = this.Projections.UpdatedPositions |> AMap.force

      for id, newPosition in movements do
        this.EventBus.Publish(Physics(PositionChanged struct (id, newPosition)))

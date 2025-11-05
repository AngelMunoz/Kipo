module Pomo.Core.Domains.Combat

open Pomo.Core.Domain.World
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Systems

module Systems =

  type CombatSystem(game: Microsoft.Xna.Framework.Game) as this =
    inherit GameSystem(game)

    override _.Kind = SystemKind.Combat // Or a new SystemKind.Combat

    override this.Update(gameTime) =
      // TODO:
      // - Listen for AbilityIntent events
      // - Validate attack (range, faction)
      // - Calculate damage using formulas and stats
      // - Publish DamageDealt or other resulting events
      ()

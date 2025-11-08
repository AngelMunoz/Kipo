module Pomo.Core.Domains.Attributes

open Pomo.Core.Domain.World
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Systems

module Systems =
  type StatSystem(game: Microsoft.Xna.Framework.Game) as this =
    inherit GameSystem(game)

    override _.Kind = SystemKind.Game // Or a new SystemKind.Attributes

    override this.Update(gameTime) =
      // TODO:
      // - Listen for events that trigger stat recalculation (e.g., EffectApplied)
      // - Recalculate DerivedStats from BaseStats and ActiveEffects
      // - Publish StatsChanged event if they have changed
      ()

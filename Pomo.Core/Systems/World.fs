namespace Pomo.Core.Systems

module Systems =
  open Microsoft.Xna.Framework
  open Pomo.Core.Domain.World
  open Pomo.Core
  open Pomo.Core.Projections


  [<Struct>]
  type SystemKind =
    | Game
    | Movement
    | RawInput
    | InputMapping
    | Combat
    | Effects
    | ResourceManager
    | Collision
    | AI

  type GameSystem(game: Game) =
    inherit GameComponent(game)

    abstract Kind: SystemKind
    default _.Kind = Game

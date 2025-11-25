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

    member val World = game.Services.GetService<World>() with get

    member val Projections =
      game.Services.GetService<ProjectionService>() with get

    member val EventBus = game.Services.GetService<EventBus.EventBus>() with get

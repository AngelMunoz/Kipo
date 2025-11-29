namespace Pomo.Core.Domain.Scenes

[<Struct>]
type Scene =
  | MainMenu
  | Gameplay of mapKey: string * targetSpawn: string voption

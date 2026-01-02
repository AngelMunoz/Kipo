namespace Pomo.Core.Domain.Scenes

open Pomo.Core.Domain.BlockMap

[<Struct>]
type Scene =
  | MainMenu
  | Gameplay of mapKey: string * targetSpawn: string voption
  | MapEditor of editorMapKey: string voption
  | BlockMapPlaytest of blockMap: BlockMapDefinition

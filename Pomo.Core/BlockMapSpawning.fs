namespace Pomo.Core

open Pomo.Core.Domain.Core
open Pomo.Core.Domain.BlockMap

/// Spawning utilities for BlockMap-based scenarios
module BlockMapSpawning =

  /// Find first spawn point matching predicate
  let inline private tryFindSpawn
    ([<InlineIfLambda>] predicate: MapObject -> bool)
    (map: BlockMapDefinition)
    : MapObject voption =
    let mutable result = ValueNone
    let mutable i = 0
    let objects = map.Objects

    while i < objects.Length && result.IsNone do
      let obj = objects[i]

      match obj.Data with
      | MapObjectData.Spawn _ when predicate obj -> result <- ValueSome obj
      | _ -> ()

      i <- i + 1

    result

  /// Find player spawn position (first with IsPlayerSpawn=true, or map center)
  let findPlayerSpawnPosition(map: BlockMapDefinition) : WorldPosition =
    tryFindSpawn
      (fun obj ->
        match obj.Data with
        | MapObjectData.Spawn props -> props.IsPlayerSpawn
        | _ -> false)
      map
    |> ValueOption.map _.Position
    |> ValueOption.defaultValue {
      X = float32 map.Width * CellSize / 2f
      Y = CellSize
      Z = float32 map.Depth * CellSize / 2f
    }

  /// Collect all spawn objects into provided list (avoids allocation)
  let collectSpawnPoints
    (map: BlockMapDefinition)
    (output: ResizeArray<MapObject>)
    =
    output.Clear()

    for obj in map.Objects do
      match obj.Data with
      | MapObjectData.Spawn _ -> output.Add(obj)
      | _ -> ()

  /// Get spawn points by faction (iterates directly)
  let inline collectSpawnsByFaction
    (map: BlockMapDefinition)
    (faction: int)
    (output: ResizeArray<MapObject>)
    =
    output.Clear()

    for obj in map.Objects do
      match obj.Data with
      | MapObjectData.Spawn props when props.Faction = ValueSome faction ->
        output.Add(obj)
      | _ -> ()

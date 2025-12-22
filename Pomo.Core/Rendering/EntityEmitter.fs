namespace Pomo.Core.Rendering

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.Data.Adaptive
open Pomo.Core.Graphics

module EntityEmitter =

  /// Emits MeshCommands from pre-resolved entities.
  /// Pure function - parallelizable with Array.Parallel.collect
  let emit
    (getModelByAsset: string -> Model voption)
    (entities: ResolvedEntity[])
    : MeshCommand[] =
    entities
    |> Array.Parallel.collect(fun entity ->
      entity.Nodes
      |> Array.choose(fun node ->
        getModelByAsset node.ModelAsset
        |> ValueOption.map(fun model -> {
          Model = model
          WorldMatrix = node.WorldMatrix
        })
        |> function
          | ValueSome cmd -> Some cmd
          | ValueNone -> None))

namespace Pomo.Core.Algorithms

open Pomo.Core
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.Core.Constants
open FSharp.UMX
open Pomo.Core.Domain.Units

module BlockMap =

  [<Literal>]
  let private VariantKeyCollisionEnabled = "Collision=On"

  let inline private tryGetArchetype
    (map: BlockMapDefinition)
    (archetypeId: int<BlockTypeId>)
    : BlockType voption =
    map.Palette |> Dictionary.tryFindV archetypeId

  let inline private tryFindVariantId
    (palette: System.Collections.Generic.Dictionary<int<BlockTypeId>, BlockType>)
    (archetypeId: int<BlockTypeId>)
    (variantKey: string)
    : int<BlockTypeId> voption =
    let mutable found = ValueNone
    let mutable e = palette.Values.GetEnumerator()

    while e.MoveNext() && found.IsNone do
      let bt = e.Current

      if bt.ArchetypeId = archetypeId then
        match bt.VariantKey with
        | ValueSome k when k = variantKey -> found <- ValueSome bt.Id
        | _ -> ()

    found

  let inline private nextBlockTypeId
    (palette: System.Collections.Generic.Dictionary<int<BlockTypeId>, BlockType>)
    : int<BlockTypeId> =
    let mutable maxId = 0
    let mutable e = palette.Keys.GetEnumerator()

    while e.MoveNext() do
      let k = e.Current |> UMX.untag

      if k > maxId then
        maxId <- k

    maxId + 1 |> UMX.tag<BlockTypeId>

  let getOrCreateVariantId
    (map: BlockMapDefinition)
    (archetypeId: int<BlockTypeId>)
    (collisionEnabled: bool)
    : int<BlockTypeId> voption =
    if not collisionEnabled then
      ValueSome archetypeId
    else
      tryGetArchetype map archetypeId
      |> ValueOption.bind(fun archetype ->
        tryFindVariantId map.Palette archetypeId VariantKeyCollisionEnabled
        |> ValueOption.orElseWith(fun () ->
          let newId = nextBlockTypeId map.Palette

          let variant: BlockType = {
            archetype with
                Id = newId
                ArchetypeId = archetypeId
                VariantKey = ValueSome VariantKeyCollisionEnabled
                CollisionType = Box
          }

          map.Palette.Add(newId, variant)
          ValueSome newId))

  let setArchetypeEffect
    (map: BlockMapDefinition)
    (archetypeId: int<BlockTypeId>)
    (effect: Effect voption)
    : unit =
    tryGetArchetype map archetypeId
    |> ValueOption.iter(fun archetype ->
      let collisionVariantId =
        tryFindVariantId map.Palette archetypeId VariantKeyCollisionEnabled

      let updatedArchetype: BlockType = { archetype with Effect = effect }
      map.Palette[archetypeId] <- updatedArchetype

      collisionVariantId
      |> ValueOption.iter(fun variantId ->
        let updatedVariant: BlockType = {
          updatedArchetype with
              Id = variantId
              ArchetypeId = archetypeId
              VariantKey = ValueSome VariantKeyCollisionEnabled
              CollisionType = Box
        }

        map.Palette[variantId] <- updatedVariant))

  let inline createEmpty
    (key: string)
    (width: int)
    (height: int)
    (depth: int)
    : BlockMapDefinition =
    {
      Version = 1
      Key = key
      MapKey = ValueSome key
      Width = width
      Height = height
      Depth = depth
      Palette = System.Collections.Generic.Dictionary()
      Blocks = System.Collections.Generic.Dictionary()
      SpawnCell = ValueNone
      Settings = {
        EngagementRules = EngagementRules.PvE
        MaxEnemyEntities = 50
        StartingLayer = 0
      }
      Objects = []
    }

  let getBlockEffect
    (cell: GridCell3D)
    (map: BlockMapDefinition)
    : Effect voption =
    map.Blocks
    |> Dictionary.tryFindV cell
    |> ValueOption.bind(fun block ->
      map.Palette
      |> Dictionary.tryFindV block.BlockTypeId
      |> ValueOption.bind(fun blockType -> blockType.Effect))

  let inline cellToWorldPosition(cell: GridCell3D) : WorldPosition = {
    X = float32 cell.X * BlockMap.CellSize + BlockMap.CellSize / 2.0f
    Y = float32 cell.Y * BlockMap.CellSize + BlockMap.CellSize / 2.0f
    Z = float32 cell.Z * BlockMap.CellSize + BlockMap.CellSize / 2.0f
  }

  let inline worldPositionToCell(pos: WorldPosition) : GridCell3D = {
    X = int(pos.X / BlockMap.CellSize)
    Y = int(pos.Y / BlockMap.CellSize)
    Z = int(pos.Z / BlockMap.CellSize)
  }

  let inline tryGetBlock
    (map: BlockMapDefinition)
    (cell: GridCell3D)
    : PlacedBlock voption =
    map.Blocks |> Dictionary.tryFindV cell

  let inline isInBounds (map: BlockMapDefinition) (cell: GridCell3D) : bool =
    cell.X >= 0
    && cell.X < map.Width
    && cell.Y >= 0
    && cell.Y < map.Height
    && cell.Z >= 0
    && cell.Z < map.Depth

  let inline getBlockType
    (map: BlockMapDefinition)
    (block: PlacedBlock)
    : BlockType voption =
    map.Palette |> Dictionary.tryFindV block.BlockTypeId

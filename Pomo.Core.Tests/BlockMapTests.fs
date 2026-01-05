namespace Pomo.Core.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Spatial
open Pomo.Core.Domain.Skill
open Pomo.Core.Algorithms

module BlockMapTestHelpers =
  let createArchetype (id: int) (name: string) : BlockType = {
    Id = %id
    ArchetypeId = %id
    VariantKey = ValueNone
    Name = name
    Model = $"Tiles/{name.ToLowerInvariant()}"
    Category = "Terrain"
    CollisionType = NoCollision
    Effect = ValueNone
  }

  let createTestEffect(name: string) : Effect = {
    Name = name
    Kind = Buff
    DamageSource = Physical
    Stacking = NoStack
    Duration = Duration.Instant
    Visuals = Pomo.Core.Domain.Core.VisualManifest.empty
    Modifiers = [||]
  }

[<TestClass>]
type BlockMapTests() =

  [<TestMethod>]
  member _.TestSerializationRoundtrip() =
    // 1. Create a dummy block map
    let map = Pomo.Core.Algorithms.BlockMap.createEmpty "test_map" 10 5 10

    // 2. Add a palette item
    let blockId = %1

    let blockType = {
      Id = blockId
      ArchetypeId = blockId
      VariantKey = ValueNone
      Name = "Grass Block"
      Model = "Tiles/kaykit_blocks/grass"
      Category = "Terrain"
      CollisionType = Box
      Effect = ValueNone
    }

    map.Palette.Add(blockId, blockType)

    // 3. Add a placed block
    let cell: GridCell3D = { X = 1; Y = 0; Z = 2 }

    let placed = {
      Cell = cell
      BlockTypeId = blockId
      Rotation = ValueSome(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.57f))
    }

    map.Blocks.Add(cell, placed)

    // 4. Serialize to JSON
    let json = (Serialization.encodeBlockMapDefinition map).ToJsonString()

    // 5. Deserialize back
    match
      JDeck.Decoding.fromString(json, Serialization.blockMapDefinitionDecoder)
    with
    | Ok deserialized ->
      // 6. Verify contents
      Assert.AreEqual(map.Key, deserialized.Key, "Key mismatch")
      Assert.AreEqual(map.Width, deserialized.Width, "Width mismatch")
      Assert.AreEqual(map.Height, deserialized.Height, "Height mismatch")
      Assert.AreEqual(map.Depth, deserialized.Depth, "Depth mismatch")

      Assert.AreEqual(1, deserialized.Palette.Count, "Palette count mismatch")

      Assert.IsTrue(
        deserialized.Palette.ContainsKey(blockId),
        "Palette missing blockId"
      )

      Assert.AreEqual(
        blockType.Name,
        deserialized.Palette.[blockId].Name,
        "BlockType Name mismatch"
      )

      Assert.AreEqual(1, deserialized.Blocks.Count, "Blocks count mismatch")

      Assert.IsTrue(
        deserialized.Blocks.ContainsKey(cell),
        "Blocks missing cell"
      )

      let dPlaced = deserialized.Blocks.[cell]

      Assert.AreEqual(
        placed.BlockTypeId,
        dPlaced.BlockTypeId,
        "Placed BlockTypeId mismatch"
      )

      match placed.Rotation, dPlaced.Rotation with
      | ValueSome r1, ValueSome r2 ->
        Assert.AreEqual(r1.X, r2.X, 0.0001f, "Rotation X mismatch")
        Assert.AreEqual(r1.Y, r2.Y, 0.0001f, "Rotation Y mismatch")
        Assert.AreEqual(r1.Z, r2.Z, 0.0001f, "Rotation Z mismatch")
        Assert.AreEqual(r1.W, r2.W, 0.0001f, "Rotation W mismatch")
      | _ -> Assert.Fail("Rotation missing in roundtrip")

    | Error err -> Assert.Fail($"Deserialization failed: {err.message}")

  [<TestMethod>]
  member _.TestDeserializationFromJsonString() =
    let json =
      """
{
  "Key": "json_test",
  "Width": 10,
  "Height": 5,
  "Depth": 10,
  "Palette": {
    "1": {
      "Id": 1,
      "Name": "Grass Block",
      "Model": "Tiles/grass",
      "Category": "Terrain",
      "CollisionType": { "Type": "Box" }
    },
    "2": {
      "Id": 2,
      "Name": "Stone Block",
      "Model": "Tiles/stone",
      "Category": "Terrain",
      "CollisionType": { "Type": "Box" }
    }
  },
  "Blocks": [
    {
      "Cell": { "X": 1, "Y": 0, "Z": 2 },
      "BlockTypeId": 1,
      "Rotation": { "X": 0, "Y": 0.707, "Z": 0, "W": 0.707 }
    }
  ]
}
"""

    match
      JDeck.Decoding.fromString(json, Serialization.blockMapDefinitionDecoder)
    with
    | Ok map ->
      Assert.AreEqual("json_test", map.Key)
      Assert.AreEqual(10, map.Width)
      Assert.AreEqual(2, map.Palette.Count)
      Assert.AreEqual("Grass Block", map.Palette.[%1].Name)
      Assert.AreEqual(%2, map.Palette.[%2].ArchetypeId)
      Assert.AreEqual(1, map.Blocks.Count)

      let cell = { X = 1; Y = 0; Z = 2 }
      Assert.IsTrue(map.Blocks.ContainsKey(cell))
      let placed = map.Blocks.[cell]
      Assert.AreEqual(%1, placed.BlockTypeId)

      match placed.Rotation with
      | ValueSome r ->
        Assert.AreEqual(0.707f, r.Y, 0.001f)
        Assert.AreEqual(0.707f, r.W, 0.001f)
      | ValueNone -> Assert.Fail("Rotation should be present")

    | Error err -> Assert.Fail($"Deserialization failed: {err.message}")

[<TestClass>]
type ArchetypeVariantTests() =

  [<TestMethod>]
  member _.``getOrCreateVariantId returns archetype id when collision disabled``
    ()
    =
    let map = BlockMap.createEmpty "test" 10 5 10
    let archetype = BlockMapTestHelpers.createArchetype 1 "Grass"
    map.Palette.Add(archetype.Id, archetype)

    let result = BlockMap.getOrCreateVariantId map archetype.Id false

    Assert.AreEqual(ValueSome archetype.Id, result)
    Assert.AreEqual(1, map.Palette.Count)

  [<TestMethod>]
  member _.``getOrCreateVariantId creates variant when collision enabled``() =
    let map = BlockMap.createEmpty "test" 10 5 10
    let archetype = BlockMapTestHelpers.createArchetype 1 "Grass"
    map.Palette.Add(archetype.Id, archetype)

    let result = BlockMap.getOrCreateVariantId map archetype.Id true

    match result with
    | ValueSome variantId ->
      Assert.AreNotEqual(archetype.Id, variantId)
      Assert.AreEqual(2, map.Palette.Count)

      let variant = map.Palette.[variantId]
      Assert.AreEqual(archetype.Id, variant.ArchetypeId)
      Assert.AreEqual(ValueSome "Collision=On", variant.VariantKey)
      Assert.AreEqual(Box, variant.CollisionType)
    | ValueNone -> Assert.Fail("Expected variant to be created")

  [<TestMethod>]
  member _.``getOrCreateVariantId is idempotent``() =
    let map = BlockMap.createEmpty "test" 10 5 10
    let archetype = BlockMapTestHelpers.createArchetype 1 "Grass"
    map.Palette.Add(archetype.Id, archetype)

    let result1 = BlockMap.getOrCreateVariantId map archetype.Id true
    let result2 = BlockMap.getOrCreateVariantId map archetype.Id true

    Assert.AreEqual(result1, result2)
    Assert.AreEqual(2, map.Palette.Count)

  [<TestMethod>]
  member _.``getOrCreateVariantId returns none for missing archetype``() =
    let map = BlockMap.createEmpty "test" 10 5 10
    let missingId: int<BlockTypeId> = %99

    let result = BlockMap.getOrCreateVariantId map missingId true

    Assert.AreEqual(ValueNone, result)

  [<TestMethod>]
  member _.``setArchetypeEffect updates archetype effect``() =
    let map = BlockMap.createEmpty "test" 10 5 10
    let archetype = BlockMapTestHelpers.createArchetype 1 "Lava"
    map.Palette.Add(archetype.Id, archetype)

    let effect = BlockMapTestHelpers.createTestEffect "Burn"
    BlockMap.setArchetypeEffect map archetype.Id (ValueSome effect)

    let updated = map.Palette.[archetype.Id]
    Assert.IsTrue(updated.Effect.IsSome)
    Assert.AreEqual("Burn", updated.Effect.Value.Name)

  [<TestMethod>]
  member _.``setArchetypeEffect propagates to collision variant``() =
    let map = BlockMap.createEmpty "test" 10 5 10
    let archetype = BlockMapTestHelpers.createArchetype 1 "Lava"
    map.Palette.Add(archetype.Id, archetype)

    let variantId = BlockMap.getOrCreateVariantId map archetype.Id true
    let effect = BlockMapTestHelpers.createTestEffect "Burn"
    BlockMap.setArchetypeEffect map archetype.Id (ValueSome effect)

    match variantId with
    | ValueSome vid ->
      let variant = map.Palette.[vid]
      Assert.IsTrue(variant.Effect.IsSome)
      Assert.AreEqual("Burn", variant.Effect.Value.Name)
      Assert.AreEqual(Box, variant.CollisionType)
    | ValueNone -> Assert.Fail("Variant should exist")

  [<TestMethod>]
  member _.``setArchetypeEffect can clear effect``() =
    let map = BlockMap.createEmpty "test" 10 5 10
    let archetype = BlockMapTestHelpers.createArchetype 1 "Lava"
    map.Palette.Add(archetype.Id, archetype)

    let effect = BlockMapTestHelpers.createTestEffect "Burn"
    BlockMap.setArchetypeEffect map archetype.Id (ValueSome effect)
    BlockMap.setArchetypeEffect map archetype.Id ValueNone

    let updated = map.Palette.[archetype.Id]
    Assert.AreEqual(ValueNone, updated.Effect)

  [<TestMethod>]
  member _.``normalizeLoadedMap fixes inconsistent archetype ids``() =
    let map = BlockMap.createEmpty "test" 10 5 10

    let inconsistentBlock: BlockType = {
      Id = %5
      ArchetypeId = %1
      VariantKey = ValueNone
      Name = "Broken"
      Model = "Tiles/broken"
      Category = "Terrain"
      CollisionType = NoCollision
      Effect = ValueNone
    }

    map.Palette.Add(inconsistentBlock.Id, inconsistentBlock)

    let normalized = BlockMap.normalizeLoadedMap map
    let fixedBlock = normalized.Palette.[%5]

    Assert.AreEqual(%5, fixedBlock.ArchetypeId)

  [<TestMethod>]
  member _.``normalizeLoadedMap preserves valid variants``() =
    let map = BlockMap.createEmpty "test" 10 5 10
    let archetype = BlockMapTestHelpers.createArchetype 1 "Grass"
    map.Palette.Add(archetype.Id, archetype)

    let variantId = BlockMap.getOrCreateVariantId map archetype.Id true

    let normalized = BlockMap.normalizeLoadedMap map

    match variantId with
    | ValueSome vid ->
      let variant = normalized.Palette.[vid]
      Assert.AreEqual(archetype.Id, variant.ArchetypeId)
      Assert.AreEqual(ValueSome "Collision=On", variant.VariantKey)
    | ValueNone -> Assert.Fail("Variant should exist")

  [<TestMethod>]
  member _.``normalizeLoadedMap is idempotent``() =
    let map = BlockMap.createEmpty "test" 10 5 10

    let inconsistentBlock: BlockType = {
      Id = %5
      ArchetypeId = %1
      VariantKey = ValueNone
      Name = "Broken"
      Model = "Tiles/broken"
      Category = "Terrain"
      CollisionType = NoCollision
      Effect = ValueNone
    }

    map.Palette.Add(inconsistentBlock.Id, inconsistentBlock)

    let normalized1 = BlockMap.normalizeLoadedMap map
    let normalized2 = BlockMap.normalizeLoadedMap normalized1

    let block1 = normalized1.Palette.[%5]
    let block2 = normalized2.Palette.[%5]

    Assert.AreEqual(block1.ArchetypeId, block2.ArchetypeId)

  [<TestMethod>]
  member _.``getOrCreateEffectVariant reuses existing variant regardless of key order``
    ()
    =
    let map = BlockMap.createEmpty "test" 10 5 10
    let archetype = BlockMapTestHelpers.createArchetype 1 "Lava"
    map.Palette.Add(archetype.Id, archetype)

    let collisionVariantId =
      match BlockMap.getOrCreateVariantId map archetype.Id true with
      | ValueSome id -> id
      | ValueNone -> failwith "Expected collision variant"

    let effect = BlockMapTestHelpers.createTestEffect "Burn"
    let effectVariantId: int<BlockTypeId> = %99

    let preexistingEffectVariant: BlockType = {
      map.Palette.[collisionVariantId] with
          Id = effectVariantId
          ArchetypeId = archetype.Id
          VariantKey = ValueSome "Effect=Burn;Collision=On"
          CollisionType = Box
          Effect = ValueSome effect
    }

    map.Palette.Add(effectVariantId, preexistingEffectVariant)

    let result =
      BlockMap.getOrCreateEffectVariant
        map
        collisionVariantId
        (ValueSome effect)

    Assert.AreEqual(ValueSome effectVariantId, result)
    Assert.AreEqual(3, map.Palette.Count)

  [<TestMethod>]
  member _.``getOrCreateEffectVariant clearing effect preserves collision variant``
    ()
    =
    let map = BlockMap.createEmpty "test" 10 5 10
    let archetype = BlockMapTestHelpers.createArchetype 1 "Lava"
    map.Palette.Add(archetype.Id, archetype)

    let collisionVariantId =
      match BlockMap.getOrCreateVariantId map archetype.Id true with
      | ValueSome id -> id
      | ValueNone -> failwith "Expected collision variant"

    let effect = BlockMapTestHelpers.createTestEffect "Burn"

    let effectVariantId =
      match
        BlockMap.getOrCreateEffectVariant
          map
          collisionVariantId
          (ValueSome effect)
      with
      | ValueSome id -> id
      | ValueNone -> failwith "Expected effect variant"

    let cleared =
      BlockMap.getOrCreateEffectVariant map effectVariantId ValueNone

    Assert.AreEqual(ValueSome collisionVariantId, cleared)

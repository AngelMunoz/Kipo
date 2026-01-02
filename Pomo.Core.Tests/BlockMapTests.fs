namespace Pomo.Core.Tests

open Microsoft.VisualStudio.TestTools.UnitTesting
open Microsoft.Xna.Framework
open FSharp.UMX
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.BlockMap
open Pomo.Core.Domain.Spatial

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
      Assert.AreEqual(1, map.Palette.Count)
      Assert.AreEqual("Grass Block", map.Palette.[%1].Name)
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

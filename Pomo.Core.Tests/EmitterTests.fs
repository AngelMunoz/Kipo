module EmitterTests

open System
open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Animation
open Pomo.Core.Domain.Projectile
open Pomo.Core.Domain.Spatial
open Pomo.Core.Graphics
open Pomo.Core.Rendering
open Pomo.Core.Stores
open Pomo.Core.Projections

/// Creates a fake Model for testing (MonoGame Model requires GraphicsDevice, so we use null)
let fakeModel() : Model = Unchecked.defaultof<Model>

/// Creates a fake Texture2D for testing
let fakeTexture() : Texture2D = Unchecked.defaultof<Texture2D>

/// Standard PPU
let stdPpu = Vector2(32.0f, 32.0f)

/// Creates a RenderCore for testing with True3D space
let createRenderCore() : RenderCore = {
  PixelsPerUnit = stdPpu
  Space = True3D
  ToRenderPos = fun pos -> Vector3(pos.X / 32.0f, pos.Y / 32.0f, pos.Z / 32.0f)
  ToRenderParticlePos = fun particlePos -> particlePos / 32.0f
}


[<TestClass>]
type EntityEmitterTests() =

  [<TestMethod>]
  member _.``emit returns empty array for empty entities``() =
    let getModel _ = ValueNone
    let result = EntityEmitter.emit getModel Array.empty
    Assert.AreEqual(0, result.Length)

  [<TestMethod>]
  member _.``emit returns commands for entities with valid models``() =
    let model = fakeModel()

    let getModel(asset: string) =
      if asset = "TestAsset" then
        ValueSome { Model = model; HasNormals = false }
      else
        ValueNone

    let entities = [|
      {
        EntityId = %Guid.NewGuid()
        Nodes = [|
          {
            ModelAsset = "TestAsset"
            WorldMatrix = Matrix.Identity
          }
        |]
      }
    |]

    let result = EntityEmitter.emit getModel entities
    Assert.AreEqual(1, result.Length)

  [<TestMethod>]
  member _.``emit filters out nodes with missing models``() =
    let model = fakeModel()

    let getModel(asset: string) =
      if asset = "Valid" then
        ValueSome { Model = model; HasNormals = false }
      else
        ValueNone

    let entities = [|
      {
        EntityId = %Guid.NewGuid()
        Nodes = [|
          {
            ModelAsset = "Valid"
            WorldMatrix = Matrix.Identity
          }
          {
            ModelAsset = "Missing"
            WorldMatrix = Matrix.Identity
          }
          {
            ModelAsset = "Valid"
            WorldMatrix = Matrix.Identity
          }
        |]
      }
    |]

    let result = EntityEmitter.emit getModel entities
    Assert.AreEqual(2, result.Length)

  [<TestMethod>]
  member _.``emit handles multiple entities``() =
    let model = fakeModel()

    let getModel _ =
      ValueSome { Model = model; HasNormals = false }

    let entities: ResolvedEntity[] = [|
      {
        EntityId = %Guid.NewGuid()
        Nodes = [|
          {
            ModelAsset = "A"
            WorldMatrix = Matrix.Identity
          }
        |]
      }
      {
        EntityId = %Guid.NewGuid()
        Nodes = [|
          {
            ModelAsset = "B"
            WorldMatrix = Matrix.Identity
          }
        |]
      }
      {
        EntityId = %Guid.NewGuid()
        Nodes = [|
          {
            ModelAsset = "C"
            WorldMatrix = Matrix.Identity
          }
        |]
      }
    |]

    let result = EntityEmitter.emit getModel entities
    Assert.AreEqual(3, result.Length)


[<TestClass>]
type PoseResolverTests() =

  /// Creates minimal EntityRenderData for testing
  let createEntityRenderData() : EntityRenderData =
    let modelStore =
      { new ModelStore with
          member _.find id = failwith "not found"

          member _.tryFind id =
            if id = "TestConfig" then
              ValueSome {
                Rig =
                  HashMap.ofList [
                    "Root",
                    {
                      Parent = ValueNone
                      Pivot = Vector3.Zero
                      Offset = Vector3.Zero
                      ModelAsset = "RootModel"
                    }
                  ]
                AnimationBindings = HashMap.empty
                FacingOffset = 0.0f
                PickBoundsOverride = ValueNone
              }
            else
              ValueNone

          member _.all() = Seq.empty
      }

    {
      ModelStore = modelStore
      GetLoadedModelByAsset =
        fun _ ->
          ValueSome {
            Model = fakeModel()
            HasNormals = false
          }
      EntityPoses = readOnlyDict []
      LiveProjectiles = HashMap.empty
      ModelScale = 1.0f
    }

  /// Creates a MovementSnapshot with entities at given positions
  let createSnapshot
    (entities: (Guid<EntityId> * Vector2 * string) list)
    : MovementSnapshot =
    {
      Positions =
        entities
        |> List.map(fun (id, pos, _) ->
          id, ({ X = pos.X; Y = 0.0f; Z = pos.Y }: WorldPosition))
        |> Dictionary.ofSeq
      SpatialGrid = Dictionary.empty()
      Rotations = Dictionary.empty()
      ModelConfigIds =
        entities |> List.map(fun (id, _, cfg) -> id, cfg) |> Dictionary.ofSeq
    }

  [<TestMethod>]
  member _.``resolveAll returns empty for empty snapshot``() =
    let core = createRenderCore()
    let data = createEntityRenderData()
    let snapshot = MovementSnapshot.Empty
    let pool = Dictionary<string, Matrix>()

    let result = PoseResolver.resolveAll core data snapshot pool
    Assert.AreEqual(0, result.Length)

  [<TestMethod>]
  member _.``resolveAll creates ResolvedEntity for valid entities``() =
    let core = createRenderCore()
    let data = createEntityRenderData()
    let entityId: Guid<EntityId> = %Guid.NewGuid()

    let snapshot =
      createSnapshot [ (entityId, Vector2(100.0f, 200.0f), "TestConfig") ]

    let pool = Dictionary<string, Matrix>()

    let result = PoseResolver.resolveAll core data snapshot pool
    Assert.AreEqual(1, result.Length)
    Assert.AreEqual(entityId, result.[0].EntityId)

  [<TestMethod>]
  member _.``resolveAll includes all rig nodes``() =
    let modelStore =
      { new ModelStore with
          member _.find id = failwith "not found"

          member _.tryFind id =
            if id = "MultiNode" then
              ValueSome {
                Rig =
                  HashMap.ofList [
                    "Root",
                    {
                      Parent = ValueNone
                      Pivot = Vector3.Zero
                      Offset = Vector3.Zero
                      ModelAsset = "RootModel"
                    }
                    "Arm",
                    {
                      Parent = ValueSome "Root"
                      Pivot = Vector3.Zero
                      Offset = Vector3(1.0f, 0.0f, 0.0f)
                      ModelAsset = "ArmModel"
                    }
                    "Hand",
                    {
                      Parent = ValueSome "Arm"
                      Pivot = Vector3.Zero
                      Offset = Vector3(0.5f, 0.0f, 0.0f)
                      ModelAsset = "HandModel"
                    }
                  ]
                AnimationBindings = HashMap.empty
                FacingOffset = 0.0f
                PickBoundsOverride = ValueNone
              }
            else
              ValueNone

          member _.all() = Seq.empty
      }

    let core = createRenderCore()

    let data = {
      ModelStore = modelStore
      GetLoadedModelByAsset =
        fun _ ->
          ValueSome {
            Model = fakeModel()
            HasNormals = false
          }
      EntityPoses = readOnlyDict []
      LiveProjectiles = HashMap.empty
      ModelScale = 1.0f
    }

    let entityId: Guid<EntityId> = %Guid.NewGuid()
    let snapshot = createSnapshot [ (entityId, Vector2.Zero, "MultiNode") ]
    let pool = Dictionary<string, Matrix>()

    let result = PoseResolver.resolveAll core data snapshot pool
    Assert.AreEqual(1, result.Length)
    Assert.AreEqual(3, result.[0].Nodes.Length)

  [<TestMethod>]
  member _.``resolveAll skips entities with missing config``() =
    let core = createRenderCore()
    let data = createEntityRenderData()
    let entityId: Guid<EntityId> = %Guid.NewGuid()

    let snapshot =
      createSnapshot [ (entityId, Vector2.Zero, "NonExistentConfig") ]

    let pool = Dictionary<string, Matrix>()

    let result = PoseResolver.resolveAll core data snapshot pool
    Assert.AreEqual(0, result.Length)

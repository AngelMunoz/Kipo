module EmitterTests

open System
open System.Collections.Generic
open Xunit
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.Animation
open Pomo.Core.Domain.Projectile
open Pomo.Core.Domain.Spatial
open Pomo.Core.Graphics
open Pomo.Core.Rendering
open Pomo.Core.Stores
open Pomo.Core.Projections

// ============================================================================
// Fake Implementations (no mocks)
// ============================================================================

/// Creates a fake Model for testing (MonoGame Model requires GraphicsDevice, so we use null)
let fakeModel() : Model = Unchecked.defaultof<Model>

/// Creates a fake Texture2D for testing
let fakeTexture() : Texture2D = Unchecked.defaultof<Texture2D>

/// Standard isometric PPU
let isoPpu = Vector2(32.0f, 16.0f)

/// Creates a RenderCore with standard settings
let createRenderCore() : RenderCore = {
  PixelsPerUnit = isoPpu
  ToRenderPos = fun pos alt -> RenderMath.LogicRender.toRender pos alt isoPpu
}

// ============================================================================
// EntityEmitter Tests
// ============================================================================

module EntityEmitterTests =

  [<Fact>]
  let ``emit returns empty array for empty entities``() =
    let getModel _ = ValueNone
    let result = EntityEmitter.emit getModel Array.empty
    Assert.Empty(result)

  [<Fact>]
  let ``emit returns commands for entities with valid models``() =
    let model = fakeModel()

    let getModel(asset: string) =
      if asset = "TestAsset" then ValueSome model else ValueNone

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
    Assert.Single(result) |> ignore

  [<Fact>]
  let ``emit filters out nodes with missing models``() =
    let model = fakeModel()

    let getModel(asset: string) =
      if asset = "Valid" then ValueSome model else ValueNone

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
    Assert.Equal(2, result.Length)

  [<Fact>]
  let ``emit handles multiple entities``() =
    let model = fakeModel()
    let getModel _ = ValueSome model

    let entities = [|
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
    Assert.Equal(3, result.Length)

// ============================================================================
// TerrainEmitter Tests
// ============================================================================

module TerrainEmitterTests =

  /// Creates a fake MapLayer with optional tiles
  let createLayer
    (width: int)
    (height: int)
    (tileIds: (int * int * int) list)
    : MapLayer =
    let tiles = Array2D.create width height ValueNone

    for (x, y, id) in tileIds do
      tiles.[x, y] <- ValueSome { TileId = %id; X = x; Y = y }

    {
      Id = %1
      Name = "TestLayer"
      Width = width
      Height = height
      Visible = true
      Opacity = 1.0f
      Properties = HashMap.empty
      Tiles = tiles
    }

  [<Fact>]
  let ``emitLayer returns empty for invisible layer``() =
    let core = createRenderCore()

    let data: TerrainRenderData = {
      GetTileTexture = fun _ -> ValueSome(fakeTexture())
      LayerRenderIndices = readOnlyDict []
    }

    let map: MapDefinition = {
      Key = "test"
      Version = "1.0"
      TiledVersion = "1.0"
      Width = 10
      Height = 10
      TileWidth = 32
      TileHeight = 32
      Orientation = Orthogonal
      RenderOrder = RightDown
      Infinite = false
      StaggerAxis = ValueNone
      StaggerIndex = ValueNone
      Tilesets = IndexList.empty
      Layers = IndexList.empty
      ObjectGroups = IndexList.empty
      BackgroundColor = ValueNone
      Properties = HashMap.empty
    }

    let layer = {
      createLayer 10 10 [ (0, 0, 1) ] with
          Visible = false
    }

    let viewBounds = struct (-1000.0f, 1000.0f, -1000.0f, 1000.0f)

    let result = TerrainEmitter.emitLayer core data map layer viewBounds
    Assert.Empty(result)

    let result = TerrainEmitter.emitLayer core data map layer viewBounds
    Assert.Empty(result)

// ============================================================================
// PoseResolver Tests
// ============================================================================

module PoseResolverTests =

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
              }
            else
              ValueNone

          member _.all() = Seq.empty
      }

    {
      ModelStore = modelStore
      GetModelByAsset = fun _ -> ValueSome(fakeModel())
      EntityPoses = Dictionary.empty()
      LiveProjectiles = HashMap.empty
      SquishFactor = 2.0f
      ModelScale = 1.0f
    }

  /// Creates a MovementSnapshot with entities at given positions
  let createSnapshot
    (entities: (Guid<EntityId> * Vector2 * string) list)
    : MovementSnapshot =
    {
      Positions =
        entities |> List.map(fun (id, pos, _) -> id, pos) |> Dictionary.ofSeq
      SpatialGrid = Dictionary.empty()
      Rotations = Dictionary.empty()
      ModelConfigIds =
        entities |> List.map(fun (id, _, cfg) -> id, cfg) |> Dictionary.ofSeq
    }

  [<Fact>]
  let ``resolveAll returns empty for empty snapshot``() =
    let core = createRenderCore()
    let data = createEntityRenderData()
    let snapshot = MovementSnapshot.Empty
    let pool = Dictionary<string, Matrix>()

    let result = PoseResolver.resolveAll core data snapshot pool
    Assert.Empty(result)

  [<Fact>]
  let ``resolveAll creates ResolvedEntity for valid entities``() =
    let core = createRenderCore()
    let data = createEntityRenderData()
    let entityId: Guid<EntityId> = %Guid.NewGuid()

    let snapshot =
      createSnapshot [ (entityId, Vector2(100.0f, 200.0f), "TestConfig") ]

    let pool = Dictionary<string, Matrix>()

    let result = PoseResolver.resolveAll core data snapshot pool
    Assert.Single(result) |> ignore
    Assert.Equal(entityId, result.[0].EntityId)

  [<Fact>]
  let ``resolveAll includes all rig nodes``() =
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
              }
            else
              ValueNone

          member _.all() = Seq.empty
      }

    let core = createRenderCore()

    let data = {
      ModelStore = modelStore
      GetModelByAsset = fun _ -> ValueSome(fakeModel())
      EntityPoses = Dictionary.empty()
      LiveProjectiles = HashMap.empty
      SquishFactor = 2.0f
      ModelScale = 1.0f
    }

    let entityId: Guid<EntityId> = %Guid.NewGuid()
    let snapshot = createSnapshot [ (entityId, Vector2.Zero, "MultiNode") ]
    let pool = Dictionary<string, Matrix>()

    let result = PoseResolver.resolveAll core data snapshot pool
    Assert.Single(result) |> ignore
    Assert.Equal(3, result.[0].Nodes.Length)

  [<Fact>]
  let ``resolveAll skips entities with missing config``() =
    let core = createRenderCore()
    let data = createEntityRenderData()
    let entityId: Guid<EntityId> = %Guid.NewGuid()

    let snapshot =
      createSnapshot [ (entityId, Vector2.Zero, "NonExistentConfig") ]

    let pool = Dictionary<string, Matrix>()

    let result = PoseResolver.resolveAll core data snapshot pool
    Assert.Empty(result)

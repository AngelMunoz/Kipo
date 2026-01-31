namespace Pomo.Lib

open System.Text.Json
open Mibo.Elmish
open JDeck
open Pomo.Lib.Services

module JsonSerializerOptions =
  open System.Text.Json.Serialization

  let createOptions() : JsonSerializerOptions =
    let options =
      JsonSerializerOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
      )
    // Register all custom codecs
    options
    |> Codec.useEncoder DomainSerializers.vector3Encoder
    |> Codec.useDecoder DomainSerializers.vector3Decoder
    |> Codec.useEncoder DomainSerializers.quaternionEncoder
    |> Codec.useDecoder DomainSerializers.quaternionDecoder
    |> Codec.useEncoder DomainSerializers.gridDimensionsEncoder
    |> Codec.useDecoder DomainSerializers.gridDimensionsDecoder
    |> Codec.useEncoder DomainSerializers.collisionTypeEncoder
    |> Codec.useDecoder DomainSerializers.collisionTypeDecoder
    |> Codec.useEncoder DomainSerializers.engagementRulesEncoder
    |> Codec.useDecoder DomainSerializers.engagementRulesDecoder
    |> Codec.useEncoder DomainSerializers.mapObjectShapeEncoder
    |> Codec.useDecoderWithOptions
      DomainSerializers.mapObjectShapeDecoderFactory
    |> Codec.useEncoder DomainSerializers.mapObjectDataEncoder
    |> Codec.useDecoderWithOptions DomainSerializers.mapObjectDataDecoderFactory
    |> Codec.useEncoder DomainSerializers.placedBlockEncoder
    |> Codec.useDecoder DomainSerializers.placedBlockDecoder
    |> Codec.useEncoder DomainSerializers.blockTypeEncoder
    |> Codec.useDecoder DomainSerializers.blockTypeDecoder
    |> Codec.useEncoder DomainSerializers.paletteEncoder
    |> Codec.useDecoder DomainSerializers.paletteDecoder
    |> Codec.useEncoder DomainSerializers.blocksEncoder
    |> Codec.useDecoder DomainSerializers.blocksDecoder
    |> Codec.useEncoder DomainSerializers.mapSettingsEncoder
    |> Codec.useDecoder DomainSerializers.mapSettingsDecoder
    |> Codec.useEncoder DomainSerializers.mapObjectEncoder
    |> Codec.useDecoder DomainSerializers.mapObjectDecoder
    |> Codec.useEncoder DomainSerializers.mapObjectsEncoder
    |> Codec.useDecoder DomainSerializers.mapObjectsDecoder
    |> Codec.useEncoder DomainSerializers.blockMapDefinitionEncoder
    |> Codec.useDecoder DomainSerializers.blockMapDefinitionDecoder

module EnvFactory =
  let create(ctx: GameContext) : AppEnv =
    let fileSystem = FileSystem.live
    let serializationOptions = JsonSerializerOptions.createOptions()
    let serialization = SerializationService.live serializationOptions
    let blockMapPersistence = BlockMapPersistence.live fileSystem serialization
    let assets = Mibo.Elmish.Assets.getService ctx
    let editorCursor = EditorCursor.live ctx.GraphicsDevice

    {
      FileSystemService = fileSystem
      BlockMapPersistenceService = blockMapPersistence
      AssetsService = assets
      EditorCursorService = editorCursor
      SerializationService = serialization
    }

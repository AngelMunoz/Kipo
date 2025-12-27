namespace Pomo.Core.Domain

module AssetManifest =

  /// Asset categories for preloading
  type ManifestAssets = {
    Models: string[]
    Textures: string[]
    Icons: string[]
  }

  /// Loading manifest for a specific map
  type LoadingManifest = {
    MapKey: string
    Version: string
    Assets: ManifestAssets
  }

  /// Result of manifest lookup
  type ManifestLookup =
    | Explicit of LoadingManifest
    | UseHeuristics

  module Serialization =
    open JDeck
    open JDeck.Decode

    module ManifestAssets =
      let decoder: Decoder<ManifestAssets> =
        fun json -> decode {
          let! models =
            VOptional.Property.array ("Models", Required.string) json

          let! textures =
            VOptional.Property.array ("Textures", Required.string) json

          let! icons = VOptional.Property.array ("Icons", Required.string) json

          return {
            Models = models |> ValueOption.defaultValue [||]
            Textures = textures |> ValueOption.defaultValue [||]
            Icons = icons |> ValueOption.defaultValue [||]
          }
        }

    module LoadingManifest =
      let decoder: Decoder<LoadingManifest> =
        fun json -> decode {
          let! mapKey = Required.Property.get ("MapKey", Required.string) json
          let! version = Required.Property.get ("Version", Required.string) json

          let! assets =
            Required.Property.get ("Assets", ManifestAssets.decoder) json

          return {
            MapKey = mapKey
            Version = version
            Assets = assets
          }
        }

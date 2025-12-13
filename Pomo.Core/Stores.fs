namespace Pomo.Core

open System
open System.IO
open System.Text.Json

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.AI
open Pomo.Core.Domain.Animation
open Pomo.Core.Domain.Particles
open Pomo.Core.Serialization

module JsonFileLoader =

  let readSkills (deserializer: JDeckDeserializer) (filePath: string) =
    try

      let json =
        Path.Combine(AppContext.BaseDirectory, filePath) |> File.ReadAllBytes

      match deserializer.Deserialize<Map<string, Skill.Skill>> json with
      | Ok result ->
        let mutable newMap = HashMap.empty<int<SkillId>, Skill>

        for KeyValue(key, value) in result do
          newMap <- HashMap.add (UMX.tag<SkillId>(int key)) value newMap

        Ok newMap
      | Error decodeError -> Error $"Deserialization error: {decodeError}"
    with ex ->
      Error $"Failed to load file '{filePath}': {ex.Message}"

  let readItems (deserializer: JDeckDeserializer) (filePath: string) =
    try

      let json =
        Path.Combine(AppContext.BaseDirectory, filePath) |> File.ReadAllBytes

      match deserializer.Deserialize<Map<string, Item.ItemDefinition>> json with
      | Ok result ->
        let mutable newMap = HashMap.empty<int<ItemId>, Item.ItemDefinition>

        for KeyValue(key, value) in result do
          newMap <- HashMap.add (UMX.tag<ItemId>(int key)) value newMap

        Ok newMap
      | Error decodeError -> Error $"Deserialization error: {decodeError}"
    with ex ->
      Error $"Failed to load file '{filePath}': {ex.Message}"

  let readAIArchetypes (deserializer: JDeckDeserializer) (filePath: string) =
    try
      let json =
        Path.Combine(AppContext.BaseDirectory, filePath) |> File.ReadAllBytes

      match deserializer.Deserialize<AI.AIArchetype array> json with
      | Ok result ->
        let mutable newMap = HashMap.empty<int<AiArchetypeId>, AI.AIArchetype>

        for archetype in result do
          newMap <- HashMap.add archetype.id archetype newMap

        Ok newMap
      | Error decodeError -> Error $"Deserialization error: {decodeError}"
    with ex ->
      Error $"Failed to load file '{filePath}': {ex.Message}"

  let readModels (deserializer: JDeckDeserializer) (filePath: string) =
    try
      let json =
        Path.Combine(AppContext.BaseDirectory, filePath) |> File.ReadAllBytes

      match deserializer.Deserialize<Map<string, ModelConfig>> json with
      | Ok result ->
        let mutable newMap = HashMap.empty<string, ModelConfig>

        for KeyValue(key, value) in result do
          newMap <- HashMap.add key value newMap

        Ok newMap
      | Error decodeError -> Error $"Deserialization error: {decodeError}"
    with ex ->
      Error $"Failed to load file '{filePath}': {ex.Message}"

  let readAnimations (deserializer: JDeckDeserializer) (filePath: string) =
    try
      let json =
        Path.Combine(AppContext.BaseDirectory, filePath) |> File.ReadAllBytes

      match deserializer.Deserialize<Map<string, AnimationClip>> json with
      | Ok result ->
        let mutable newMap = HashMap.empty<string, AnimationClip>

        for KeyValue(key, value) in result do
          // Assign the dictionary key as the Name of the clip
          let clip = { value with Name = key }
          newMap <- HashMap.add key clip newMap

        Ok newMap
      | Error decodeError -> Error $"Deserialization error: {decodeError}"
    with ex ->
      Error $"Failed to load file '{filePath}': {ex.Message}"

  let readParticles (deserializer: JDeckDeserializer) (filePath: string) =
    try
      let json =
        Path.Combine(AppContext.BaseDirectory, filePath) |> File.ReadAllBytes

      match deserializer.Deserialize<EmitterConfig list> json with
      | Ok result ->
        let mutable newMap = HashMap.empty<string, EmitterConfig list>

        // Group by Name
        let grouped = result |> List.groupBy(fun config -> config.Name)

        for (name, configs) in grouped do
          newMap <- HashMap.add name configs newMap

        Ok newMap
      | Error decodeError -> Error $"Deserialization error: {decodeError}"
    with ex ->
      Error $"Failed to load file '{filePath}': {ex.Message}"


module Stores =
  open Pomo.Core.Domain.Units

  type SkillStore =
    abstract member find: skillId: int<SkillId> -> Skill
    abstract member tryFind: skillId: int<SkillId> -> Skill voption
    abstract member all: unit -> seq<Skill>

  type ItemStore =
    abstract member find: itemId: int<ItemId> -> Item.ItemDefinition
    abstract member tryFind: itemId: int<ItemId> -> Item.ItemDefinition voption
    abstract member all: unit -> seq<Item.ItemDefinition>

  type AIArchetypeStore =
    abstract member find: archetypeId: int<AiArchetypeId> -> AI.AIArchetype

    abstract member tryFind:
      archetypeId: int<AiArchetypeId> -> AI.AIArchetype voption

    abstract member all: unit -> seq<AI.AIArchetype>

  type MapStore =
    abstract member find: key: string -> Map.MapDefinition
    abstract member tryFind: key: string -> Map.MapDefinition voption
    abstract member all: unit -> seq<Map.MapDefinition>

  type ModelStore =
    abstract member find: configId: string -> ModelConfig
    abstract member tryFind: configId: string -> ModelConfig voption
    abstract member all: unit -> seq<ModelConfig>

  type AnimationStore =
    abstract member find: clipId: string -> AnimationClip
    abstract member tryFind: clipId: string -> AnimationClip voption
    abstract member all: unit -> seq<AnimationClip>

  type ParticleStore =
    abstract member find: effectId: string -> EmitterConfig list
    abstract member tryFind: effectId: string -> EmitterConfig list voption
    abstract member all: unit -> seq<string * EmitterConfig list>


  module Skill =

    let create(loader: string -> Result<HashMap<int<SkillId>, Skill>, string>) =
      match loader "Content/Skills.json" with
      | Ok skillMap ->

        { new SkillStore with
            member _.all() : Skill seq = skillMap |> HashMap.toValueSeq

            member _.find(skillId: int<SkillId>) : Skill =
              HashMap.find skillId skillMap

            member _.tryFind(skillId: int<SkillId>) : Skill voption =
              HashMap.tryFindV skillId skillMap
        }

      | Error errMsg -> failwith $"Failed to create SkillStore: {errMsg}"

  module Item =

    let create
      (loader: string -> Result<HashMap<int<ItemId>, ItemDefinition>, string>)
      =
      match loader "Content/Items.json" with
      | Ok itemMap ->

        { new ItemStore with
            member _.all() : ItemDefinition seq = itemMap |> HashMap.toValueSeq

            member _.find(itemId: int<ItemId>) : ItemDefinition =
              HashMap.find itemId itemMap

            member _.tryFind(itemId: int<ItemId>) : ItemDefinition voption =
              HashMap.tryFindV itemId itemMap
        }

      | Error errMsg -> failwith $"Failed to create ItemStore: {errMsg}"

  module AIArchetype =

    let create
      (loader:
        string -> Result<HashMap<int<AiArchetypeId>, AI.AIArchetype>, string>)
      =
      match loader "Content/AIArchetypes.json" with
      | Ok archetypeMap ->

        { new AIArchetypeStore with
            member _.all() : AI.AIArchetype seq =
              archetypeMap |> HashMap.toValueSeq

            member _.find(archetypeId: int<AiArchetypeId>) : AI.AIArchetype =
              HashMap.find archetypeId archetypeMap

            member _.tryFind
              (archetypeId: int<AiArchetypeId>)
              : AI.AIArchetype voption =
              HashMap.tryFindV archetypeId archetypeMap
        }

      | Error errMsg -> failwith $"Failed to create AIArchetypeStore: {errMsg}"

  module Map =

    let create (loader: string -> Map.MapDefinition) (mapPaths: string list) =
      let maps =
        mapPaths
        |> List.map loader
        |> List.map(fun m -> m.Key, m)
        |> HashMap.ofList

      { new MapStore with
          member _.find(key) = HashMap.find key maps
          member _.tryFind(key) = HashMap.tryFindV key maps
          member _.all() = maps |> HashMap.toValueSeq
      }

  module Model =
    let create(loader: string -> Result<HashMap<string, ModelConfig>, string>) =
      match loader "Content/Models.json" with
      | Ok modelMap ->
        { new ModelStore with
            member _.find(configId: string) = HashMap.find configId modelMap

            member _.tryFind(configId: string) =
              HashMap.tryFindV configId modelMap

            member _.all() = modelMap |> HashMap.toValueSeq
        }
      | Error errMsg -> failwith $"Failed to create ModelStore: {errMsg}"

  module Animation =
    let create
      (loader: string -> Result<HashMap<string, AnimationClip>, string>)
      =
      match loader "Content/Animations.json" with
      | Ok animMap ->
        { new AnimationStore with
            member _.find(clipId: string) = HashMap.find clipId animMap
            member _.tryFind(clipId: string) = HashMap.tryFindV clipId animMap
            member _.all() = animMap |> HashMap.toValueSeq
        }
      | Error errMsg -> failwith $"Failed to create AnimationStore: {errMsg}"

  module Particle =
    let create
      (loader: string -> Result<HashMap<string, EmitterConfig list>, string>)
      =
      match loader "Content/Particles.json" with
      | Ok particleMap ->
        { new ParticleStore with
            member _.find(effectId: string) = HashMap.find effectId particleMap

            member _.tryFind(effectId: string) =
              HashMap.tryFindV effectId particleMap

            member _.all() = particleMap |> HashMap.toSeq
        }
      | Error errMsg -> failwith $"Failed to create ParticleStore: {errMsg}"

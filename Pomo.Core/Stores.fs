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

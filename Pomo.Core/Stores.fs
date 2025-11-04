namespace Pomo.Core

open System
open System.IO
open System.Text.Json

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Skill
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


module Stores =
  open Pomo.Core.Domain.Units

  type SkillStore =
    abstract member find: skillId: int<SkillId> -> Skill
    abstract member tryFind: skillId: int<SkillId> -> Skill voption
    abstract member all: unit -> seq<Skill>

  module Skill =
    open Pomo.Core.Domain.Skill

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

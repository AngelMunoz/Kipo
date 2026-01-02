namespace Pomo.Core.Systems

open FSharp.Data.Adaptive
open Pomo.Core.Domain.Entity

module GameLogic =

  module Faction =

    [<Struct>]
    type Relation =
      | Ally
      | Enemy
      | Neutral

    let getRelation
      (sourceFactions: HashSet<Faction>)
      (targetFactions: HashSet<Faction>)
      =
      if HashSet.isEmpty sourceFactions || HashSet.isEmpty targetFactions then
        Relation.Neutral
      else
        // Rule 1: Same faction NEVER attacks same faction
        let hasOverlap =
          not(HashSet.isEmpty(HashSet.intersect sourceFactions targetFactions))

        if hasOverlap then
          Relation.Ally
        else
          // Rule 2: Ally and Player don't attack each other
          let sourceIsPlayerSide =
            sourceFactions.Contains Faction.Player
            || sourceFactions.Contains Faction.Ally

          let targetIsPlayerSide =
            targetFactions.Contains Faction.Player
            || targetFactions.Contains Faction.Ally

          if sourceIsPlayerSide && targetIsPlayerSide then
            Relation.Ally
          else
            Relation.Enemy

namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.EventBus

module Projectile =
  let generateEvents
    (positions: amap<_, _>)
    projectileId
    (projectile: Projectile.LiveProjectile)
    =
    adaptive {
      // Adaptively find the positions of the projectile and its target.
      // This inner block will only re-run if these specific positions change.
      let! projPos = positions |> AMap.tryFind projectileId
      let! targetPos = positions |> AMap.tryFind projectile.Target
      let mutable events = IndexList.empty

      match projPos, targetPos with
      | Some projPos, Some targetPos ->
        let distance = Vector2.Distance(projPos, targetPos)
        let threshold = 4.0f // Close enough for impact

        if distance < threshold then
          // Impact: Create impact and removal events
          let impact: ProjectileImpact = {
            ProjectileId = projectileId
            CasterId = projectile.Caster
            TargetId = projectile.Target
            SkillId = projectile.SkillId
          }

          events <- IndexList.add (ProjectileImpacted impact) events
          events <- IndexList.add (EntityRemoved projectileId) events
        else
          // Keep moving: Create a velocity change event
          let direction = Vector2.Normalize(targetPos - projPos)
          let velocity = direction * projectile.Info.Speed

          events <-
            IndexList.add
              (VelocityChanged struct (projectileId, velocity))
              events
      | Some _, None
      | None, Some _
      | None, None ->
        // If projectile or target has no position, remove projectile
        events <- IndexList.add (EntityRemoved projectileId) events

      return events
    }

type ProjectileSystem(game: Game) as this =
  inherit GameSystem(game)

  let eventsToPublish =
    let inline append acc _ value = IndexList.append value acc

    this.World.LiveProjectiles
    |> AMap.mapA(Projectile.generateEvents this.World.Positions)
    |> AMap.fold append IndexList.empty

  override this.Update _ =
    eventsToPublish |> AVal.force |> this.EventBus.Publish

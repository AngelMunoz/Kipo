namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.Events

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
      let mutable systemEvents = IndexList.empty
      let mutable stateEvents = IndexList.empty

      match projPos, targetPos with
      | Some projPos, Some targetPos ->
        let distance = Vector2.Distance(projPos, targetPos)
        let threshold = 4.0f // Close enough for impact

        if distance < threshold then
          // Impact: Create impact and removal events
          let impact: SystemCommunications.ProjectileImpacted = {
            ProjectileId = projectileId
            CasterId = projectile.Caster
            TargetId = projectile.Target
            SkillId = projectile.SkillId
          }

          systemEvents <- IndexList.add impact systemEvents

          stateEvents <-
            IndexList.add
              (StateChangeEvent.EntityLifecycle(
                EntityLifecycleEvents.Removed projectileId
              ))
              stateEvents
        else
          // Keep moving: Create a velocity change event
          let direction = Vector2.Normalize(targetPos - projPos)
          let velocity = direction * projectile.Info.Speed

          stateEvents <-
            IndexList.add
              (StateChangeEvent.Physics(
                PhysicsEvents.VelocityChanged struct (projectileId, velocity)
              ))
              stateEvents
      | Some _, None
      | None, Some _
      | None, None ->
        // If projectile or target has no position, remove projectile
        stateEvents <-
          IndexList.add
            (StateChangeEvent.EntityLifecycle(
              EntityLifecycleEvents.Removed projectileId
            ))
            stateEvents

      return struct (systemEvents, stateEvents)
    }

type ProjectileSystem(game: Game) as this =
  inherit GameSystem(game)

  let eventsToPublish =
    this.World.LiveProjectiles
    |> AMap.mapA(Projectile.generateEvents this.World.Positions)
    |> AMap.fold
      (fun (sysAcc, stateAcc) _ struct (sysEvents, stateEvents) ->
        (IndexList.append sysEvents sysAcc,
         IndexList.append stateEvents stateAcc))
      (IndexList.empty, IndexList.empty)

  override this.Update _ =
    let sysEvents, stateEvents = eventsToPublish |> AVal.force
    sysEvents |> IndexList.iter(fun e -> this.EventBus.Publish(e))
    stateEvents |> IndexList.iter(fun e -> this.EventBus.Publish(e))

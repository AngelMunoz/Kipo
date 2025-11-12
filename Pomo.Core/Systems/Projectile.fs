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
open Pomo.Core.Domain.Projectile

module Projectile =
  let private findNextChainTarget
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (casterId: Guid<EntityId>)
    (currentTargetId: Guid<EntityId>)
    (maxRange: float32)
    =
    positions
    |> HashMap.toList
    |> List.filter(fun (id, _) -> id <> casterId && id <> currentTargetId)
    |> List.map(fun (id, pos) ->
      id, pos, Vector2.DistanceSquared(positions[currentTargetId], pos))
    |> List.filter(fun (_, _, distSq) -> distSq <= maxRange * maxRange)
    |> List.sortBy(fun (_, _, distSq) -> distSq)
    |> List.tryHead
    |> Option.map(fun (id, _, _) -> id)

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

          // Always remove the current projectile after impact
          stateEvents <-
            IndexList.add (EntityLifecycle(Removed projectileId)) stateEvents

          // Handle chaining
          match projectile.Info.Variations with
          | ValueSome(Chained(jumpsLeft, maxRange)) when jumpsLeft > 0 ->
            let allPositions = positions |> AMap.force

            let nextTarget =
              findNextChainTarget
                allPositions
                projectile.Caster
                projectile.Target
                maxRange

            match nextTarget with
            | Some newTargetId ->
              let newProjectileId = Guid.NewGuid() |> UMX.tag<EntityId>

              let newLiveProjectile: LiveProjectile = {
                projectile with
                    Target = newTargetId
                    Info = {
                      projectile.Info with
                          Variations =
                            ValueSome(Chained(jumpsLeft - 1, maxRange))
                    }
              }

              stateEvents <-
                IndexList.add
                  (StateChangeEvent.CreateProjectile
                    struct (newProjectileId,
                            newLiveProjectile,
                            ValueSome targetPos))
                  stateEvents
            | None -> ()
          | _ -> ()

        else
          // Keep moving: Create a velocity change event
          let direction = Vector2.Normalize(targetPos - projPos)
          let velocity = direction * projectile.Info.Speed

          stateEvents <-
            IndexList.add
              (Physics(VelocityChanged struct (projectileId, velocity)))
              stateEvents
      | Some _, None
      | None, Some _
      | None, None ->
        // If projectile or target has no position, remove projectile
        stateEvents <-
          IndexList.add (EntityLifecycle(Removed projectileId)) stateEvents

      return struct (systemEvents, stateEvents)
    }

type ProjectileSystem(game: Game) as this =
  inherit GameSystem(game)

  let eventsToPublish =
    this.World.LiveProjectiles
    |> AMap.mapA(
      Projectile.generateEvents(Projections.UpdatedPositions this.World)
    )
    |> AMap.fold
      (fun (sysAcc, stateAcc) _ struct (sysEvents, stateEvents) ->
        (IndexList.append sysEvents sysAcc,
         IndexList.append stateEvents stateAcc))
      (IndexList.empty, IndexList.empty)

  override this.Update _ =
    let sysEvents, stateEvents = eventsToPublish |> AVal.force
    sysEvents |> IndexList.iter(fun e -> this.EventBus.Publish(e))
    stateEvents |> IndexList.iter(fun e -> this.EventBus.Publish(e))

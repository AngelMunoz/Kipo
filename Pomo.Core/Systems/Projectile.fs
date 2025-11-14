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
    (positions: amap<Guid<EntityId>, Vector2>)
    (liveEntities: aset<Guid<EntityId>>)
    (casterId: Guid<EntityId>)
    (currentTargetId: Guid<EntityId>)
    (originPos: Vector2)
    (maxRange: float32)
    =
    positions
    |> AMap.filterA(fun id _ -> adaptive {
      let! isLive = liveEntities |> ASet.contains id
      return isLive && id <> casterId && id <> currentTargetId
    })
    |> AMap.chooseA(fun _ pos -> adaptive {
      let distance = Vector2.DistanceSquared(originPos, pos)

      if distance <= maxRange * maxRange then
        return Some distance
      else
        return None
    })
    |> AMap.sortBy(fun _ distance -> distance)
    |> AList.toAVal

  let generateEvents
    (positions: amap<Guid<EntityId>, Vector2>)
    (liveEntities: aset<Guid<EntityId>>)
    (projectileId: Guid<EntityId>)
    (projectile: Projectile.LiveProjectile)
    =

    let generateFoundTarget projPos targetPos = adaptive {

      let distance = Vector2.Distance(projPos, targetPos)
      let threshold = 4.0f // Close enough for impact
      let commEvents = ResizeArray()
      let stateEvents = ResizeArray()

      let inline addComms(event) = commEvents.Add event

      let inline addState(event) = stateEvents.Add event

      if distance < threshold then
        let remainingJumps =
          match projectile.Info.Variations with
          | ValueSome(Chained(jumpsLeft, _)) -> ValueSome jumpsLeft
          | _ -> ValueNone
        // Impact: Create impact and removal events
        let impact: SystemCommunications.ProjectileImpacted = {
          ProjectileId = projectileId
          CasterId = projectile.Caster
          TargetId = projectile.Target
          SkillId = projectile.SkillId
          RemainingJumps = remainingJumps
        }

        do addComms impact

        // Always remove the current projectile after impact
        do addState <| EntityLifecycle(Removed projectileId)

        // Handle chaining
        let! nextTarget = adaptive {
          match projectile.Info.Variations with
          | ValueSome(Chained(jumpsLeft, maxRange)) when jumpsLeft >= 0 ->
            let! nextTarget =
              findNextChainTarget
                positions
                liveEntities
                projectile.Caster
                projectile.Target
                targetPos
                maxRange

            let targets = nextTarget.AsArray

            let selectedTarget =
              match targets with
              | [||] -> ValueNone
              | targets ->
                let selected, _ = Array.randomChoice targets
                ValueSome selected

            match selectedTarget with
            | ValueSome newTargetId ->
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

              return
                ValueSome
                <| CreateProjectile
                  struct (newProjectileId,
                          newLiveProjectile,
                          ValueSome targetPos)
            | ValueNone -> return ValueNone
          | _ -> return ValueNone
        }

        do nextTarget |> ValueOption.iter addState

        return
          struct (commEvents |> IndexList.ofSeq, stateEvents |> IndexList.ofSeq)
      else
        // Keep moving: Create a velocity change event
        let direction = Vector2.Normalize(targetPos - projPos)
        let velocity = direction * projectile.Info.Speed

        do addState <| Physics(VelocityChanged struct (projectileId, velocity))

        return
          struct (commEvents |> IndexList.ofSeq, stateEvents |> IndexList.ofSeq)
    }

    adaptive {
      let! projPos = positions |> AMap.tryFind projectileId
      let! targetPos = positions |> AMap.tryFind projectile.Target


      match projPos, targetPos with
      | Some projPos, Some targetPos ->
        return! generateFoundTarget projPos targetPos
      | Some _, None
      | None, Some _
      | None, None ->
        return
          struct (IndexList.empty,
                  IndexList.single(EntityLifecycle(Removed projectileId)))
    }

type ProjectileSystem(game: Game) as this =
  inherit GameSystem(game)

  let eventsToPublish =
    let positions = Projections.UpdatedPositions this.World
    let liveEntities = Projections.LiveEntities this.World

    this.World.LiveProjectiles
    |> AMap.mapA(Projectile.generateEvents positions liveEntities)
    |> AMap.fold
      (fun (sysAcc, stateAcc) _ struct (sysEvents, stateEvents) ->
        (IndexList.append sysEvents sysAcc,
         IndexList.append stateEvents stateAcc))
      (IndexList.empty, IndexList.empty)

  override this.Update _ =
    let sysEvents, stateEvents = eventsToPublish |> AVal.force
    sysEvents |> IndexList.iter this.EventBus.Publish
    stateEvents |> IndexList.iter this.EventBus.Publish

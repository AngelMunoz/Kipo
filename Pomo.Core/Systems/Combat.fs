module Pomo.Core.Domains.Combat

open System
open Microsoft.Xna.Framework
open FSharp.UMX

open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.EventBus
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Systems

module Handlers =

  let private handleAbilityIntent
    (eventBus: EventBus)
    (skillStore: Stores.SkillStore)
    (casterId: Guid<EntityId>)
    (skillId: int<SkillId>)
    (targetId: Guid<EntityId>)
    =
    match skillStore.tryFind skillId with
    | ValueSome(Skill.Active activeSkill) ->
      match activeSkill.Delivery with
      | Delivery.Projectile projectileInfo ->
        let projectileId = Guid.NewGuid() |> UMX.tag<EntityId>

        let liveProjectile: Projectile.LiveProjectile = {
          Caster = casterId
          Target = targetId
          SkillId = skillId
          Info = projectileInfo
        }

        eventBus.Publish(CreateProjectile struct (projectileId, liveProjectile))
      | Delivery.Instant -> () // Explicitly ignored for now
      | Delivery.Melee -> () // Explicitly ignored for now
    | ValueSome(Skill.Passive _) -> () // Not an active skill, do nothing
    | ValueNone -> () // Skill not found, do nothing

  let private handleProjectileImpact
    (eventBus: EventBus)
    (impact: ProjectileImpact)
    =
    // TODO: Calculate damage based on stats and formula
    let damage = 10 // Placeholder damage
    eventBus.Publish(DamageDealt(impact.TargetId, damage))

  /// The main event handler, with dependencies injected via a tuple.
  let handleEvent
    (dependencies: EventBus * Stores.SkillStore)
    (event: WorldEvent)
    =
    let eventBus, skillStore = dependencies

    match event with
    | AbilityIntent(casterId, skillId, ValueSome targetId) ->
      handleAbilityIntent eventBus skillStore casterId skillId targetId
    | ProjectileImpacted impact -> handleProjectileImpact eventBus impact
    | _ -> ()


type CombatSystem(game: Microsoft.Xna.Framework.Game) as this =
  inherit GameSystem(game)

  let eventBus = this.EventBus
  let skillStore = game.Services.GetService<Stores.SkillStore>()
  let mutable subscription: IDisposable = null

  // Create the partially-applied handler with its dependencies injected.
  let injectedHandler = Handlers.handleEvent(eventBus, skillStore)

  override this.Initialize() =
    base.Initialize()
    // Subscribe the handler to the event bus.
    subscription <- eventBus |> Observable.subscribe injectedHandler

  override this.Dispose disposing =
    base.Dispose disposing
    // Unsubscribe when the system is disposed to prevent memory leaks.
    match subscription with
    | null -> ()
    | sub -> sub.Dispose()

  override _.Kind = SystemKind.Combat

  override this.Update gameTime = base.Update gameTime

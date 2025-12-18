namespace Pomo.Core.Systems

open System

open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.World
open Pomo.Core.Systems.Systems
open Pomo.Core.EventBus

module Effects =

  module EffectApplication =
    open Pomo.Core.Domain.Core

    let private createNewActiveEffect
      (intent: SystemCommunications.EffectApplicationIntent)
      (totalGameTime: TimeSpan)
      =
      {
        Id = Guid.NewGuid() |> UMX.tag
        SourceEffect = intent.Effect
        SourceEntity = intent.SourceEntity
        TargetEntity = intent.TargetEntity
        StartTime = totalGameTime
        StackCount = 1
      }

    type ApplyEventResult =
      | Persistent of StateChangeEvent
      | InstantRes of SystemCommunications.EffectResourceIntent
      | InstantDmg of SystemCommunications.EffectDamageIntent

    let private applyEffect
      (world: World)
      (intent: SystemCommunications.EffectApplicationIntent)
      =
      if intent.Effect.Duration.IsInstant then
        let instantEvent =
          match intent.Effect.Kind with
          | ResourceOverTime ->
            let resIntent: SystemCommunications.EffectResourceIntent = {
              SourceEntity = intent.SourceEntity
              TargetEntity = intent.TargetEntity
              Effect = intent.Effect
              ActiveEffectId = Guid.NewGuid() |> UMX.tag
            }

            InstantRes resIntent |> ValueSome
          | DamageOverTime ->
            let dmgIntent: SystemCommunications.EffectDamageIntent = {
              SourceEntity = intent.SourceEntity
              TargetEntity = intent.TargetEntity
              Effect = intent.Effect
            }

            InstantDmg dmgIntent |> ValueSome
          | _ -> ValueNone

        instantEvent
      else
        let totalGameTime = world.Time |> AVal.map _.TotalGameTime |> AVal.force

        let findExisting(effects: IndexList<ActiveEffect>) =
          effects
          |> IndexList.tryFind(fun _ ae ->
            ae.SourceEffect.Name = intent.Effect.Name)

        let effectsOnTarget =
          world.ActiveEffects
          |> AMap.tryFind intent.TargetEntity
          |> AVal.force
          |> Option.defaultValue IndexList.empty

        let event =
          match intent.Effect.Stacking with
          | NoStack ->
            if findExisting effectsOnTarget |> Option.isSome then
              ValueNone
            else
              let newEffect = createNewActiveEffect intent totalGameTime

              EffectApplied struct (intent.TargetEntity, newEffect)
              |> StateChangeEvent.Combat
              |> Persistent
              |> ValueSome
          | RefreshDuration ->
            match findExisting effectsOnTarget with
            | Some activeEffect ->
              let refreshedEffect = {
                activeEffect with
                    StartTime = totalGameTime
              }


              EffectRefreshed struct (intent.TargetEntity, refreshedEffect.Id)
              |> StateChangeEvent.Combat
              |> Persistent
              |> ValueSome
            | None ->
              let newEffect = createNewActiveEffect intent totalGameTime

              EffectApplied struct (intent.TargetEntity, newEffect)
              |> StateChangeEvent.Combat
              |> Persistent
              |> ValueSome
          | AddStack maxStacks ->
            match findExisting effectsOnTarget with
            | Some effectToStack ->
              if effectToStack.StackCount < maxStacks then
                let newStackCount = effectToStack.StackCount + 1


                EffectStackChanged
                  struct (intent.TargetEntity, effectToStack.Id, newStackCount)
                |> StateChangeEvent.Combat
                |> Persistent
                |> ValueSome
              else
                ValueNone
            | None ->
              let newEffect = createNewActiveEffect intent totalGameTime

              EffectApplied struct (intent.TargetEntity, newEffect)
              |> StateChangeEvent.Combat
              |> Persistent
              |> ValueSome

        event

    let create(world: World, eventBus: EventBus) =
      let handler(intent: SystemCommunications.EffectApplicationIntent) =
        match applyEffect world intent with
        | ValueSome(Persistent ev) -> eventBus.Publish(GameEvent.State ev)
        | ValueSome(InstantDmg dmg) ->
          eventBus.Publish(GameEvent.Intent(IntentEvent.EffectDamage dmg))
        | ValueSome(InstantRes res) ->
          eventBus.Publish(GameEvent.Intent(IntentEvent.EffectResource res))
        | ValueNone -> ()

      { new CoreEventListener with
          member _.StartListening() =
            eventBus.Observable
            |> Observable.choose(fun e ->
              match e with
              | GameEvent.Intent(IntentEvent.EffectApplication intent) ->
                Some intent
              | _ -> None)
            |> Observable.subscribe handler
      }

  // Wrapper DU to hold different event types
  [<Struct>]
  type TickingEffect =
    | DamageIntent of damage: SystemCommunications.EffectDamageIntent
    | ResourceIntent of resource: SystemCommunications.EffectResourceIntent

  [<Struct>]
  type private LifecycleEvent =
    | State of state: StateChangeEvent
    | EffectTick of intent: TickingEffect

  let timedEffects(effects: amap<Guid<EntityId>, IndexList<ActiveEffect>>) =
    effects
    |> AMap.map(fun _ effectList ->
      effectList
      |> IndexList.filter(fun effect ->
        match effect.SourceEffect.Duration with
        | Timed _ -> true
        | _ -> false))

  let loopEffects(effects: amap<Guid<EntityId>, IndexList<ActiveEffect>>) =
    effects
    |> AMap.map(fun _ effectList ->
      effectList
      |> IndexList.filter(fun effect ->
        match effect.SourceEffect.Duration with
        | Loop _ -> true
        | _ -> false))

  let permanentLoopEffects
    (effects: amap<Guid<EntityId>, IndexList<ActiveEffect>>)
    =
    effects
    |> AMap.map(fun _ effectList ->
      effectList
      |> IndexList.filter(fun effect ->
        match effect.SourceEffect.Duration with
        | PermanentLoop _ -> true
        | _ -> false))

  module private Helpers =
    let generateInstantEvents(effect: ActiveEffect) =
      match effect.SourceEffect.Duration with
      | Duration.Instant ->
        match effect.SourceEffect.Kind with
        | DamageOverTime ->
          let intent: SystemCommunications.EffectDamageIntent = {
            SourceEntity = effect.SourceEntity
            TargetEntity = effect.TargetEntity
            Effect = effect.SourceEffect
          }

          IndexList.single(EffectTick(DamageIntent intent))
        | ResourceOverTime ->
          let intent: SystemCommunications.EffectResourceIntent = {
            SourceEntity = effect.SourceEntity
            TargetEntity = effect.TargetEntity
            Effect = effect.SourceEffect
            ActiveEffectId = effect.Id
          }

          IndexList.single(EffectTick(ResourceIntent intent))
        | _ -> IndexList.empty
      | _ -> IndexList.empty

    let generateIntervalEvents
      (totalGameTime: TimeSpan)
      (previousTime: TimeSpan)
      (effect: ActiveEffect)
      (interval: TimeSpan)
      =
      if interval <= TimeSpan.Zero then
        IndexList.empty
      else
        let startTime = effect.StartTime
        let effectivePrevTime = max previousTime startTime

        if totalGameTime > effectivePrevTime then
          let ticks = floor((totalGameTime - startTime) / interval)
          let prevTicks = floor((effectivePrevTime - startTime) / interval)
          let tickCount = int(ticks - prevTicks)

          if tickCount > 0 then
            let rec buildEvents count acc =
              if count <= 0 then
                acc
              else
                let nextAcc =
                  match effect.SourceEffect.Kind with
                  | DamageOverTime ->
                    let intent: SystemCommunications.EffectDamageIntent = {
                      SourceEntity = effect.SourceEntity
                      TargetEntity = effect.TargetEntity
                      Effect = effect.SourceEffect
                    }

                    IndexList.add (EffectTick(DamageIntent intent)) acc
                  | ResourceOverTime ->
                    let intent: SystemCommunications.EffectResourceIntent = {
                      SourceEntity = effect.SourceEntity
                      TargetEntity = effect.TargetEntity
                      Effect = effect.SourceEffect
                      ActiveEffectId = effect.Id
                    }

                    IndexList.add (EffectTick(ResourceIntent intent)) acc
                  | _ -> acc

                buildEvents (count - 1) nextAcc

            buildEvents tickCount IndexList.empty
          else
            IndexList.empty
        else
          IndexList.empty

  let private calculateTimedEvents
    (world: World)
    (effects: amap<_, IndexList<ActiveEffect>>)
    =
    effects
    |> AMap.mapA(fun entityId effectList -> adaptive {
      let! totalGameTime = world.Time |> AVal.map _.TotalGameTime

      return
        effectList
        |> IndexList.collect(fun effect ->
          let duration =
            match effect.SourceEffect.Duration with
            | Timed d -> d
            | _ -> failwith "Impossible: Not a timed effect"

          let elapsedTime = totalGameTime - effect.StartTime

          if elapsedTime >= duration then
            IndexList.single(
              State(
                StateChangeEvent.Combat(
                  EffectExpired struct (entityId, effect.Id)
                )
              )
            )
          else
            IndexList.empty)
    })

  let private calculateLoopEvents
    (world: World)
    (effects: amap<_, IndexList<ActiveEffect>>)
    =
    effects
    |> AMap.mapA(fun entityId effectList -> adaptive {
      let! time = world.Time
      let totalGameTime = time.TotalGameTime
      let previousTime = time.Previous

      let events =
        effectList
        |> IndexList.collect(fun effect ->
          let interval, duration =
            match effect.SourceEffect.Duration with
            | Loop(i, d) -> i, d
            | _ -> failwith "Impossible: Not a loop effect"

          let elapsedTime = totalGameTime - effect.StartTime

          if elapsedTime >= duration then
            IndexList.single(
              State(
                StateChangeEvent.Combat(
                  EffectExpired struct (entityId, effect.Id)
                )
              )
            )
          else
            Helpers.generateIntervalEvents
              totalGameTime
              previousTime
              effect
              interval)

      return events
    })

  let private calculatePermanentLoopEvents
    (world: World)
    (effects: amap<_, IndexList<ActiveEffect>>)
    =
    effects
    |> AMap.mapA(fun entityId effectList -> adaptive {
      let! time = world.Time
      let totalGameTime = time.TotalGameTime
      let previousTime = time.Previous

      let events =
        effectList
        |> IndexList.collect(fun effect ->
          let interval =
            match effect.SourceEffect.Duration with
            | PermanentLoop i -> i
            | _ -> failwith "Impossible: Not a permanent loop effect"

          Helpers.generateIntervalEvents
            totalGameTime
            previousTime
            effect
            interval)

      return events
    })

  let private calculateInstantEvents
    (effects: amap<_, IndexList<ActiveEffect>>)
    =
    effects
    |> AMap.map(fun _ effectList ->
      effectList |> IndexList.collect Helpers.generateInstantEvents)

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type EffectProcessingSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices

    let timedEvents =
      core.World.ActiveEffects
      |> timedEffects
      |> calculateTimedEvents core.World

    let loopEvents =
      core.World.ActiveEffects |> loopEffects |> calculateLoopEvents core.World

    let permanentLoopEvents =
      core.World.ActiveEffects
      |> permanentLoopEffects
      |> calculatePermanentLoopEvents core.World

    let allEvents =
      let inline resolve _ a b = IndexList.append a b

      AMap.unionWith
        resolve
        timedEvents
        (AMap.unionWith resolve loopEvents permanentLoopEvents)

    override _.Kind = Effects

    override _.Update g =
      base.Update g

      let publishEvents(events: IndexList<LifecycleEvent> seq) =
        for evts in events do
          for evt in evts do
            match evt with
            | State stateEvent ->
              core.EventBus.Publish(GameEvent.State stateEvent)
            | EffectTick(DamageIntent dmg) ->
              core.EventBus.Publish(
                GameEvent.Intent(IntentEvent.EffectDamage dmg)
              )
            | EffectTick(ResourceIntent res) ->
              core.EventBus.Publish(
                GameEvent.Intent(IntentEvent.EffectResource res)
              )

      publishEvents(allEvents |> AMap.toASetValues |> ASet.force)

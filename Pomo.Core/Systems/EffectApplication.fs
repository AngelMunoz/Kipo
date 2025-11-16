namespace Pomo.Core.Systems

open System

open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.World
open Pomo.Core.Systems
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

    let private applyEffect
      (world: World)
      (intent: SystemCommunications.EffectApplicationIntent)
      =
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

            ValueSome(
              StateChangeEvent.Combat(
                EffectApplied struct (intent.TargetEntity, newEffect)
              )
            )
        | RefreshDuration ->
          match findExisting effectsOnTarget with
          | Some activeEffect ->
            let refreshedEffect = {
              activeEffect with
                  StartTime = totalGameTime
            }

            ValueSome(
              StateChangeEvent.Combat(
                EffectRefreshed struct (intent.TargetEntity, refreshedEffect.Id)
              )
            )
          | None ->
            let newEffect = createNewActiveEffect intent totalGameTime

            ValueSome(
              StateChangeEvent.Combat(
                EffectApplied struct (intent.TargetEntity, newEffect)
              )
            )
        | AddStack maxStacks ->
          match findExisting effectsOnTarget with
          | Some effectToStack ->
            if effectToStack.StackCount < maxStacks then
              let newStackCount = effectToStack.StackCount + 1

              ValueSome(
                StateChangeEvent.Combat(
                  EffectStackChanged
                    struct (intent.TargetEntity, effectToStack.Id, newStackCount)
                )
              )
            else
              ValueNone
          | None ->
            let newEffect = createNewActiveEffect intent totalGameTime

            ValueSome(
              StateChangeEvent.Combat(
                EffectApplied struct (intent.TargetEntity, newEffect)
              )
            )

      event

    let create(world: World, eventBus: EventBus) =
      let handler(intent: SystemCommunications.EffectApplicationIntent) =
        match applyEffect world intent with
        | ValueSome ev -> eventBus.Publish ev
        | ValueNone -> ()

      { new CoreEventListener with
          member _.StartListening() =
            eventBus.GetObservableFor<
              SystemCommunications.EffectApplicationIntent
             >()
            |> Observable.subscribe handler
      }

  // Wrapper DU to hold different event types
  [<Struct>]
  type private LifecycleEvent =
    | State of state: StateChangeEvent
    | EffectTick of intent: SystemCommunications.EffectDamageIntent

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

                    IndexList.add (EffectTick intent) acc
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

  type EffectProcessingSystem(game: Game) as this =
    inherit GameSystem(game)

    let timedEvents =
      this.World.ActiveEffects
      |> timedEffects
      |> calculateTimedEvents this.World

    let loopEvents =
      this.World.ActiveEffects |> loopEffects |> calculateLoopEvents this.World

    let permanentLoopEvents =
      this.World.ActiveEffects
      |> permanentLoopEffects
      |> calculatePermanentLoopEvents this.World

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
            | State stateEvent -> this.EventBus.Publish stateEvent
            | EffectTick intent -> this.EventBus.Publish intent

      publishEvents(allEvents |> AMap.toASetValues |> ASet.force)

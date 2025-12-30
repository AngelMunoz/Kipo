namespace Pomo.Core.Systems

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Orbital
open Pomo.Core.Domain.Events
open Pomo.Core.Systems.Systems
open Pomo.Core.Environment

module OrbitalSystem =

  [<Struct>]
  type OrbitalEffectEntry = {
    Effect: Particles.VisualEffect
    Index: int
  }


  let spawnOrbitalEffects
    (core: CoreServices)
    (stores: StoreServices)
    (orbital: ActiveOrbital)
    (casterPos: Vector2)
    =
    match orbital.Config.Visual.VfxId with
    | ValueSome vfxId ->
      match stores.ParticleStore.tryFind vfxId with
      | ValueSome configs ->
        let entries = Array.zeroCreate<OrbitalEffectEntry> orbital.Config.Count

        for i = 0 to orbital.Config.Count - 1 do
          let struct (billboard, mesh) =
            Particles.splitEmittersByRenderMode configs

          let effectId = Guid.NewGuid().ToString()

          let localOffset = Orbital.calculatePosition orbital.Config 0.0f i

          let worldPos =
            Vector3(casterPos.X, 0.0f, casterPos.Y)
            + orbital.Config.CenterOffset
            + localOffset

          let effect: Particles.VisualEffect = {
            Id = effectId
            Emitters = billboard
            MeshEmitters = mesh
            Position = ref worldPos
            Rotation = ref Quaternion.Identity
            Scale = ref Vector3.One
            IsAlive = ref true
            Owner = ValueNone
            Overrides = Particles.EffectOverrides.empty
          }

          core.World.VisualEffects.Add(effect)
          entries[i] <- { Effect = effect; Index = i }

        entries
      | ValueNone -> Array.empty
    | ValueNone -> Array.empty

  type OrbitalSystem(game: Game, env: PomoEnvironment) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices
    let (Stores stores) = env.StoreServices
    let (Gameplay gameplay) = env.GameplayServices

    let stateWrite = core.StateWrite
    let eventBus = core.EventBus

    let effectCache = Dictionary<Guid<EntityId>, OrbitalEffectEntry[]>()
    let currentCasters = Collections.Generic.HashSet<Guid<EntityId>>()
    let toRemove = ResizeArray<Guid<EntityId>>()

    // Pre-allocated dictionary for scenario grouping (avoids Seq.groupBy allocations)
    let orbitalsByScenario =
      Dictionary<
        Guid<ScenarioId>,
        ResizeArray<struct (Guid<EntityId> * ActiveOrbital)>
       >()

    override _.Kind = Systems.Orbital

    override _.Update _ =
      let totalTime =
        core.World.Time
        |> AVal.map(_.TotalGameTime.TotalSeconds >> float32)
        |> AVal.force

      let activeOrbitals = core.World.ActiveOrbitals |> AMap.force
      let activeCharges = core.World.ActiveCharges |> AMap.force
      let entityScenarios = core.World.EntityScenario |> AMap.force
      let entityExists = core.World.EntityExists

      // === CHARGE EXPIRY LOGIC ===
      for casterId, charge: ActiveCharge in activeCharges do
        // Entity existence check
        if entityExists.Contains casterId then
          let chargeElapsed = totalTime - float32 charge.StartTime.TotalSeconds
          let duration = float32 charge.Duration.TotalSeconds

          if chargeElapsed >= duration then
            // Publish ChargeCompleted event
            let completedEvent: SystemCommunications.ChargeCompleted = {
              CasterId = casterId
              SkillId = charge.SkillId
              Target = charge.Target
            }

            eventBus.Publish(
              GameEvent.Lifecycle(LifecycleEvent.ChargeCompleted completedEvent)
            )

            // Cleanup charge and orbital
            stateWrite.RemoveActiveCharge(casterId)
            stateWrite.RemoveActiveOrbital(casterId)

            // Immediately mark visual effects as dead and clear particles
            match effectCache.TryGetValue(casterId) with
            | true, effects ->
              for i = 0 to effects.Length - 1 do
                let effect = effects[i].Effect
                effect.IsAlive.Value <- false

                // Clear all particles to immediately remove visuals
                for emitter in effect.Emitters do
                  emitter.Particles.Clear()

                for meshEmitter in effect.MeshEmitters do
                  meshEmitter.Particles.Clear()

              effectCache.Remove(casterId) |> ignore
            | false, _ -> ()

      // === ORBITAL VISUAL UPDATE LOGIC ===
      // Clear and rebuild scenario grouping imperatively
      for KeyValue(_, list) in orbitalsByScenario do
        list.Clear()

      for casterId, (orbital: ActiveOrbital) in activeOrbitals do
        match orbital.Center with
        | EntityCenter entityId ->
          match entityScenarios.TryFindV entityId with
          | ValueSome scenarioId ->
            match orbitalsByScenario.TryGetValue(scenarioId) with
            | true, list -> list.Add(struct (casterId, orbital))
            | false, _ ->
              let list = ResizeArray()
              list.Add(struct (casterId, orbital))
              orbitalsByScenario[scenarioId] <- list
          | ValueNone -> ()
        | PositionCenter _ -> ()

      currentCasters.Clear()

      for KeyValue(scenarioId, orbitals) in orbitalsByScenario do

        if orbitals.Count > 0 then
          let snapshot =
            gameplay.Projections.ComputeMovementSnapshot(scenarioId)

          for struct (casterId, orbital) in orbitals do
            currentCasters.Add(casterId) |> ignore

            // Get center position based on OrbitalCenter type
            let centerPosOpt =
              match orbital.Center with
              | EntityCenter entityId ->
                snapshot.Positions.TryFindV entityId
                |> ValueOption.map WorldPosition.toVector2
              | PositionCenter pos -> ValueSome pos

            // Get entity facing rotation (only for entity-centered orbitals)
            let entityRotation =
              match orbital.Center with
              | EntityCenter entityId ->
                snapshot.Rotations.TryFindV entityId
                |> ValueOption.defaultValue 0.0f
              | PositionCenter _ -> 0.0f

            let facingQuat =
              Quaternion.CreateFromAxisAngle(Vector3.Up, entityRotation)

            centerPosOpt
            |> ValueOption.iter(fun casterPos ->
              let elapsed = totalTime - orbital.StartTime

              // Initialize effects if new
              let mutable effects = Unchecked.defaultof<OrbitalEffectEntry[]>

              if not(effectCache.TryGetValue(casterId, &effects)) then
                effects <- spawnOrbitalEffects core stores orbital casterPos

                if effects.Length > 0 then
                  effectCache[casterId] <- effects

              // Update positions with entity facing rotation applied
              if not(isNull effects) then
                for i = 0 to effects.Length - 1 do
                  let entry = effects[i]

                  let localOffset =
                    Orbital.calculatePosition
                      orbital.Config
                      elapsed
                      entry.Index

                  // Only rotate CenterOffset to track "behind" the character
                  // localOffset stays unrotated so the X-Z orbit renders as isometric ellipse
                  let rotatedCenterOffset =
                    Vector3.Transform(orbital.Config.CenterOffset, facingQuat)

                  // The orbital's world position
                  let worldPos =
                    Vector3(casterPos.X, 0.0f, casterPos.Y)
                    + rotatedCenterOffset
                    + localOffset

                  // Update effect position (for billboard particles)
                  entry.Effect.Position.Value <- worldPos

                  // Update mesh particles directly with world position
                  // With SimulationSpace: World, particle.Position is used directly
                  for meshEmitter in entry.Effect.MeshEmitters do
                    for j = 0 to meshEmitter.Particles.Count - 1 do
                      let p = meshEmitter.Particles.[j]

                      meshEmitter.Particles.[j] <- {
                        p with
                            Position = worldPos
                      })

      // Cleanup stale effect caches
      toRemove.Clear()

      for kvp in effectCache do
        if not(currentCasters.Contains kvp.Key) then
          toRemove.Add(kvp.Key)

      for casterId in toRemove do
        let effects = effectCache[casterId]

        for i = 0 to effects.Length - 1 do
          effects[i].Effect.IsAlive.Value <- false

        effectCache.Remove(casterId) |> ignore

namespace Pomo.Core.Systems

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Orbital
open Pomo.Core.Domain.Events
open Pomo.Core.Systems.Systems
open Pomo.Core.Environment

module OrbitalSystem =

  [<Struct>]
  type OrbitalEffectEntry = {
    Effect: Particles.ActiveEffect
    Index: int
  }

  let inline calculateOrbitalPosition
    (config: OrbitalConfig)
    (elapsed: float32)
    (index: int)
    =
    let accel = (config.EndSpeed - config.StartSpeed) / config.Duration
    let angle = config.StartSpeed * elapsed + 0.5f * accel * elapsed * elapsed

    let indexOffset = (MathHelper.TwoPi / float32 config.Count) * float32 index
    let totalAngle = angle + indexOffset

    let x = MathF.Cos totalAngle * config.Radius * config.PathScale.X
    let y = MathF.Sin totalAngle * config.Radius * config.PathScale.Y
    let localPos2D = Vector3(x, y, 0.0f)

    let rotation =
      if config.RotationAxis = Vector3.UnitZ then
        Quaternion.Identity
      else
        let axis = Vector3.Cross(Vector3.UnitZ, config.RotationAxis)

        if axis.LengthSquared() < 0.001f then
          if Vector3.Dot(Vector3.UnitZ, config.RotationAxis) < 0.0f then
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.Pi)
          else
            Quaternion.Identity
        else
          let angle =
            MathF.Acos(Vector3.Dot(Vector3.UnitZ, config.RotationAxis))

          Quaternion.CreateFromAxisAngle(Vector3.Normalize axis, angle)

    Vector3.Transform(localPos2D, rotation)

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

          let localOffset = calculateOrbitalPosition orbital.Config 0.0f i

          let worldPos =
            Vector3(casterPos.X, 0.0f, casterPos.Y)
            + orbital.Config.CenterOffset
            + localOffset

          let effect: Particles.ActiveEffect = {
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
              | EntityCenter entityId -> snapshot.Positions.TryFindV entityId
              | PositionCenter pos -> ValueSome pos

            centerPosOpt
            |> ValueOption.iter(fun casterPos ->
              let elapsed = totalTime - orbital.StartTime

              // Initialize effects if new
              let mutable effects = Unchecked.defaultof<OrbitalEffectEntry[]>

              if not(effectCache.TryGetValue(casterId, &effects)) then
                effects <- spawnOrbitalEffects core stores orbital casterPos

                if effects.Length > 0 then
                  effectCache[casterId] <- effects

              // Update positions
              if not(isNull effects) then
                for i = 0 to effects.Length - 1 do
                  let entry = effects[i]

                  let localOffset =
                    calculateOrbitalPosition orbital.Config elapsed entry.Index

                  let worldPos =
                    Vector3(casterPos.X, 0.0f, casterPos.Y)
                    + orbital.Config.CenterOffset
                    + localOffset

                  entry.Effect.Position.Value <- worldPos)

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

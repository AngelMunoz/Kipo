namespace Pomo.Core.Rendering

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Particles
open Pomo.Core.Graphics

module ParticleEmitter =

  /// Computes effect position from owner or fallback
  let inline private computeEffectPosition
    (owner: Guid<EntityId> voption)
    (positions: HashMap<Guid<EntityId>, Vector2>)
    (fallbackPos: Vector3)
    =
    match owner with
    | ValueSome ownerId ->
      match positions |> HashMap.tryFindV ownerId with
      | ValueSome interpPos -> Vector3(interpPos.X, 0.0f, interpPos.Y)
      | ValueNone -> fallbackPos
    | ValueNone -> fallbackPos

  /// Computes particle world position based on simulation space
  /// For Local space, particle position is rotated by effect rotation before adding effect position
  let inline private computeParticleWorldPosition
    (config: EmitterConfig)
    (particlePos: Vector3)
    (effectPos: Vector3)
    (effectRotation: Quaternion)
    =
    match config.SimulationSpace with
    | SimulationSpace.World -> particlePos
    | SimulationSpace.Local ->
      // Rotate local position by effect rotation, then add effect position
      let rotatedLocal = Vector3.Transform(particlePos, effectRotation)
      rotatedLocal + effectPos

  /// Transforms particle position to render space
  /// Delegates to RenderMath.ParticleToRender which handles isometric scaling
  let inline private particleToRenderPosition
    (pWorldPos: Vector3)
    (pixelsPerUnit: Vector2)
    =
    RenderMath.ParticleToRender pWorldPos pixelsPerUnit

  /// Emits billboard particle commands (parallel over effects)
  let emitBillboards
    (core: RenderCore)
    (data: ParticleRenderData)
    (effects: VisualEffect[])
    : BillboardCommand[] =
    effects
    |> Array.Parallel.collect(fun effect ->
      if not effect.IsAlive.Value then
        Array.empty
      else
        let effectPos =
          computeEffectPosition
            effect.Owner
            data.EntityPositions
            effect.Position.Value

        effect.Emitters
        |> Seq.toArray
        |> Array.collect(fun emitter ->
          match emitter.Config.RenderMode with
          | Billboard textureId ->
            data.GetTexture textureId
            |> function
              | ValueSome texture ->
                emitter.Particles
                |> Seq.toArray
                |> Array.map(fun particle ->
                  let pWorldPos =
                    computeParticleWorldPosition
                      emitter.Config
                      particle.Position
                      effectPos
                      effect.Rotation.Value

                  let renderPos =
                    particleToRenderPosition pWorldPos core.PixelsPerUnit

                  let size = particle.Size * data.ModelScale

                  {
                    Texture = texture
                    Position = renderPos
                    Size = size
                    Color = particle.Color
                    BlendMode = emitter.Config.BlendMode
                  })
              | ValueNone -> Array.empty
          | Mesh _ -> Array.empty))

  /// Emits mesh particle commands (parallel over effects)
  let emitMeshes
    (core: RenderCore)
    (data: ParticleRenderData)
    (effects: VisualEffect[])
    : MeshCommand[] =
    effects
    |> Array.Parallel.collect(fun effect ->
      if not effect.IsAlive.Value then
        Array.empty
      else
        let effectPos = effect.Position.Value

        effect.MeshEmitters
        |> Seq.toArray
        |> Array.collect(fun meshEmitter ->
          data.GetModelByAsset meshEmitter.ModelPath
          |> function
            | ValueSome model ->
              meshEmitter.Particles
              |> Seq.toArray
              |> Array.map(fun particle ->
                let pWorldPos =
                  computeParticleWorldPosition
                    meshEmitter.Config
                    particle.Position
                    effectPos
                    effect.Rotation.Value

                let renderPos =
                  particleToRenderPosition pWorldPos core.PixelsPerUnit

                let baseScale = particle.Scale * data.ModelScale

                let worldMatrix =
                  RenderMath.CreateMeshParticleWorldMatrix
                    renderPos
                    particle.Rotation
                    baseScale
                    meshEmitter.Config.ScaleAxis
                    meshEmitter.Config.ScalePivot
                    data.SquishFactor

                {
                  Model = model
                  WorldMatrix = worldMatrix
                })
            | ValueNone -> Array.empty))

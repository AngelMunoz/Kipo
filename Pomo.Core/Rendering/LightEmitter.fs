namespace Pomo.Core.Rendering

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

module LightEmitter =

  /// Light command for dynamic lighting (placeholder for future)
  [<Struct>]
  type LightCommand = {
    Position: Vector3
    Color: Color
    Intensity: float32
    Radius: float32
  }

  /// Applies default lighting configuration to a BasicEffect
  /// Matches legacy Render.fs lighting setup
  let applyDefaultLighting(effect: inref<BasicEffect>) =
    effect.LightingEnabled <- true
    effect.PreferPerPixelLighting <- true

    effect.AmbientLightColor <- Vector3(0.2f, 0.2f, 0.2f)
    effect.SpecularColor <- Vector3.Zero

    effect.DirectionalLight0.Enabled <- true
    effect.DirectionalLight0.Direction <- Vector3(-1.0f, -1.7f, 0.0f)
    effect.DirectionalLight0.DiffuseColor <- Vector3(0.6f, 0.6f, 0.6f)
    effect.DirectionalLight0.SpecularColor <- Vector3.Zero

    effect.DirectionalLight1.Enabled <- false
    effect.DirectionalLight2.Enabled <- false

  /// Placeholder for particle-emitted lights (future feature)
  let emitFromParticles() : LightCommand[] = Array.empty

  /// Placeholder for entity-emitted lights (future feature)
  let emitFromEntities() : LightCommand[] = Array.empty

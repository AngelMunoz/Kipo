namespace Pomo.Core.Rendering

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.World

module TextEmitter =
  open Pomo.Core.Domain.Events

  /// Loads the HUD font for text rendering
  let loadFont(content: ContentManager) : SpriteFont =
    content.Load<SpriteFont> "Fonts/monogram-extended"

  /// Emits text commands from world text with camera culling.
  /// Sequential - typically < 20 notifications, parallelism overhead exceeds benefit.
  let emit
    (renderCore: RenderCore)
    (viewport: Viewport)
    (view: Matrix)
    (projection: Matrix)
    (notifications: WorldText seq)
    (viewBounds: struct (float32 * float32 * float32 * float32))
    (palette: Pomo.Core.Domain.UI.HUDColorPalette)
    : TextCommand[] =
    let struct (viewLeft, viewRight, viewTop, viewBottom) = viewBounds

    notifications
    |> Seq.choose(fun notif ->
      let x = notif.Position.X
      let y = notif.Position.Z // Z is the 2D Y for WorldPosition

      // Cull if outside camera view (logic-space bounds)
      if x < viewLeft || x > viewRight || y < viewTop || y > viewBottom then
        None
      else
        let renderPos = renderCore.ToRenderPos notif.Position

        let projected =
          viewport.Project(renderPos, projection, view, Matrix.Identity)

        if
          Single.IsNaN projected.X
          || Single.IsNaN projected.Y
          || Single.IsNaN projected.Z
          || projected.Z < 0.0f
          || projected.Z > 1.0f
        then
          None
        else
          let screenPos = Vector2(projected.X, projected.Y)

          if not(viewport.Bounds.Contains screenPos) then
            None
          else
            let alpha = notif.Life / notif.MaxLife

            let struct (color, scale) =
              match notif.Type with
              | SystemCommunications.Normal -> struct (palette.TextNormal, 1.0f)
              | SystemCommunications.Damage -> struct (palette.TextDamage, 1.5f)
              | SystemCommunications.Crit -> struct (palette.TextCrit, 2.0f)
              | SystemCommunications.Heal -> struct (palette.TextHeal, 1.2f)
              | SystemCommunications.Status -> struct (palette.TextStatus, 0.8f)
              | SystemCommunications.Miss -> struct (palette.TextMiss, 1.0f)

            Some {
              Text = notif.Text
              ScreenPosition = screenPos
              Alpha = alpha
              Color = color
              Scale = scale
            })
    |> Seq.toArray

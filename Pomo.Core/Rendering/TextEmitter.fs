namespace Pomo.Core.Rendering

open Microsoft.Xna.Framework.Content
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.World

module TextEmitter =

  /// Loads the HUD font for text rendering
  let loadFont(content: ContentManager) : SpriteFont =
    content.Load<SpriteFont> "Fonts/Hud"

  /// Emits text commands from world text with camera culling.
  /// Sequential - typically < 20 notifications, parallelism overhead exceeds benefit.
  let emit
    (notifications: WorldText seq)
    (viewBounds: struct (float32 * float32 * float32 * float32))
    : TextCommand[] =
    let struct (viewLeft, viewRight, viewTop, viewBottom) = viewBounds

    notifications
    |> Seq.choose(fun notif ->
      let x = notif.Position.X
      let y = notif.Position.Y

      // Cull if outside camera view
      if x < viewLeft || x > viewRight || y < viewTop || y > viewBottom then
        None
      else
        let alpha = notif.Life / notif.MaxLife

        Some {
          Text = notif.Text
          ScreenPosition = notif.Position
          Alpha = alpha
        })
    |> Seq.toArray

namespace Pomo.Core

open Microsoft.Xna.Framework
open Myra.Graphics2D.UI
open Myra

// Base reusable UI screen using Myra Desktop
[<AbstractClass>]
type GameScreen(game: Game) =
  inherit DrawableGameComponent(game)
  let mutable desktop: Desktop = null
  member this.Desktop
    with get() = desktop
    and set v = desktop <- v
  abstract member BuildUI: unit -> Widget
  override this.Initialize() =
    base.Initialize()
    let root = this.BuildUI()
    desktop <- new Desktop(Root = root)
  override this.Draw(gameTime) =
    if desktop <> null then desktop.Render()
    base.Draw(gameTime)

namespace Pomo.Core

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open Myra

type PomoGame() as this =
  inherit Game()

  let graphics = new GraphicsDeviceManager(this)
  let screenManager = new ScreenManager(this)

  do
    this.Content.RootDirectory <- "Content"
    this.IsMouseVisible <- true

  override this.Initialize() =
    MyraEnvironment.Game <- this
    this.Services.AddService(typeof<ScreenManager>, screenManager)
    screenManager.Change(new SelectionScreen(this))
    base.Initialize()

  override this.Update(gameTime) =
    if Keyboard.GetState().IsKeyDown(Keys.Escape) then this.Exit() else base.Update(gameTime)

  override this.Draw(gameTime) =
    this.GraphicsDevice.Clear(Color.CornflowerBlue)
    base.Draw(gameTime)

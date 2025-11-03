namespace Pomo.Core

open System
open System.Collections.Generic
open System.Globalization
open type System.Net.Mime.MediaTypeNames

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input

open Pomo.Core.Localization

type PomoGame() as this =
  inherit Game()

  let graphicsDeviceManager = new GraphicsDeviceManager(this)

  let isMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()

  let isDesktop =
    OperatingSystem.IsWindows()
    || OperatingSystem.IsLinux()
    || OperatingSystem.IsMacOS()

  do
    base.Services.AddService(
      typeof<GraphicsDeviceManager>,
      graphicsDeviceManager
    )

    base.Content.RootDirectory <- "Content"

    graphicsDeviceManager.SupportedOrientations <-
      DisplayOrientation.LandscapeLeft ||| DisplayOrientation.LandscapeRight


  override this.Initialize() =
    base.Initialize()

    LocalizationManager.DefaultCultureCode |> LocalizationManager.SetCulture


  override this.LoadContent() = base.LoadContent()

  // Load game content here
  // e.g., this.Content.Load<Texture2D>("textureName")

  override this.Update(gameTime) =

    let state =
      GamePad.GetState(PlayerIndex.One).Buttons.Back = ButtonState.Pressed
      || Keyboard.GetState().IsKeyDown(Keys.Escape)

    if state then
      this.Exit()

    else
      // Update game logic here
      // e.g., update game entities, handle input, etc.
      base.Update(gameTime)


  override this.Draw(gameTime) =

    base.GraphicsDevice.Clear(Color.MonoGameOrange)
    // Draw game content here

    base.Draw(gameTime)

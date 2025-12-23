namespace Pomo.Core.Systems

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.EventBus
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Camera
open Pomo.Core.Graphics


module Notification =

  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type OnScreenNotification = {
    Text: string
    Position: Vector2
    Velocity: Vector2
    Life: float32
    MaxLife: float32
  }

  type NotificationSystem(game: Game, env: PomoEnvironment) =
    inherit DrawableGameComponent(game)

    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let (MonoGame monoGame) = env.MonoGameServices

    let mutable notifications = List<OnScreenNotification>()
    let spriteBatch = lazy new SpriteBatch(monoGame.GraphicsDevice)
    let mutable hudFont: SpriteFont = null

    let handleEvent(event: SystemCommunications.ShowNotification) =
      let newNotification = {
        Text = event.Message
        Position = event.Position
        Velocity = Vector2(0.0f, -20.0f) // Float upwards
        Life = 2.0f // Live for 2 seconds
        MaxLife = 2.0f
      }

      notifications.Add newNotification

    let mutable sub: IDisposable = null

    override _.Dispose(disposing: bool) =
      base.Dispose disposing

      match sub with
      | null -> ()
      | s -> s.Dispose()

    override _.Initialize() =
      base.Initialize()

      sub <-
        core.EventBus.Observable
        |> Observable.choose(fun e ->
          match e with
          | GameEvent.Notification(NotificationEvent.ShowMessage msg) ->
            Some msg
          | _ -> None)
        |> Observable.subscribe(handleEvent)

    override _.LoadContent() =
      base.LoadContent()
      hudFont <- monoGame.Content.Load<SpriteFont>("Fonts/Hud")

    override _.Update(gameTime) =
      let dt = float32 gameTime.ElapsedGameTime.TotalSeconds
      let updatedNotifications = List<OnScreenNotification>()

      for notification in notifications do
        let newLife = notification.Life - dt

        if newLife > 0.0f then
          let newPosition = notification.Position + notification.Velocity * dt

          updatedNotifications.Add {
            notification with
                Life = newLife
                Position = newPosition
          }

      notifications <- updatedNotifications

    override _.Draw(gameTime) =
      let sb = spriteBatch.Value
      let cameraService = gameplay.CameraService
      let cameras = cameraService.GetAllCameras()

      for struct (playerId, camera) in cameras do
        game.GraphicsDevice.Viewport <- camera.Viewport

        let transform =
          RenderMath.Get2DViewMatrix camera.Position camera.Zoom camera.Viewport

        sb.Begin(transformMatrix = transform)

        for notification in notifications do
          let alpha = notification.Life / notification.MaxLife
          let color = Color.White * alpha
          let textSize = hudFont.MeasureString(notification.Text)
          let textPosition = notification.Position - textSize / 2.0f
          sb.DrawString(hudFont, notification.Text, textPosition, color)

        sb.End()

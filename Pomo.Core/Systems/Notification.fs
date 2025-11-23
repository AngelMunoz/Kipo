namespace Pomo.Core.Systems

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.EventBus
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Core


module Notification =

  type OnScreenNotification = {
    Text: string
    Position: Vector2
    Velocity: Vector2
    Life: float32
    MaxLife: float32
  }

  type NotificationSystem(game: Game, eventBus: EventBus) =
    inherit DrawableGameComponent(game)

    let mutable notifications = List<OnScreenNotification>()
    let spriteBatch = lazy new SpriteBatch(game.GraphicsDevice)
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
        eventBus.GetObservableFor<SystemCommunications.ShowNotification>()
        |> Observable.subscribe(handleEvent)

    override _.LoadContent() =
      base.LoadContent()
      hudFont <- game.Content.Load<SpriteFont>("Fonts/Hud")

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
      let cameraService = game.Services.GetService<CameraService>()
      let cameras = cameraService.GetAllCameras()

      for struct (playerId, camera) in cameras do
        game.GraphicsDevice.Viewport <- camera.Viewport

        let transform =
          Matrix.CreateTranslation(-camera.Position.X, -camera.Position.Y, 0.0f)
          * Matrix.CreateScale(camera.Zoom)
          * Matrix.CreateTranslation(
            float32 camera.Viewport.Width / 2.0f,
            float32 camera.Viewport.Height / 2.0f,
            0.0f
          )

        sb.Begin(transformMatrix = transform)

        for notification in notifications do
          let alpha = notification.Life / notification.MaxLife
          let color = Color.White * alpha
          let textSize = hudFont.MeasureString(notification.Text)
          let textPosition = notification.Position - textSize / 2.0f
          sb.DrawString(hudFont, notification.Text, textPosition, color)

        sb.End()

namespace Pomo.Core.Systems

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain
open Pomo.Core.Domain.EventBus
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.World

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

    let handleEvent(event: WorldEvent) =
      match event with
      | ShowNotification(message, position) ->
        let newNotification = {
          Text = message
          Position = position
          Velocity = Vector2(0.0f, -20.0f) // Float upwards
          Life = 2.0f // Live for 2 seconds
          MaxLife = 2.0f
        }

        notifications.Add newNotification
      | _ -> ()

    let mutable sub: IDisposable = null

    override _.Dispose(disposing: bool) =
      base.Dispose disposing

      match sub with
      | null -> ()
      | s -> s.Dispose()

    override _.Initialize() =
      base.Initialize()
      sub <- eventBus |> Observable.subscribe(handleEvent)

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
      sb.Begin()

      for notification in notifications do
        let alpha = notification.Life / notification.MaxLife
        let color = Color.White * alpha
        let textSize = hudFont.MeasureString(notification.Text)
        let textPosition = notification.Position - textSize / 2.0f
        sb.DrawString(hudFont, notification.Text, textPosition, color)

      sb.End()

namespace Pomo.Core.Systems

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Pomo.Core.EventBus
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.World

module Notification =

  open Pomo.Core.Environment

  type NotificationSystem(game: Game, env: PomoEnvironment) =
    inherit GameComponent(game)

    let (Core core) = env.CoreServices
    let stateWrite = core.StateWrite
    let world = core.World

    let mutable sub: IDisposable = null

    let handleEvent(event: SystemCommunications.ShowNotification) =
      let drift = (float32(world.Rng.NextDouble()) * 20.0f) - 10.0f

      let newNotification: WorldText = {
        Text = event.Message
        Type = event.Type
        Position = event.Position
        Velocity = Vector2(drift, -20.0f)
        Life = 2.0f
        MaxLife = 2.0f
      }

      stateWrite.AddNotification newNotification

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
          | Notification(ShowMessage msg) -> Some msg
          | _ -> None)
        |> Observable.subscribe handleEvent

    override _.Update(gameTime) =
      let dt = float32 gameTime.ElapsedGameTime.TotalSeconds
      let notifications = world.Notifications

      // Build updated notification list
      let updatedNotifications = List<WorldText>()

      for i = 0 to notifications.Count - 1 do
        let notification = notifications.[i]
        let newLife = notification.Life - dt

        if newLife > 0.0f then
          let newPosition = notification.Position + notification.Velocity * dt

          updatedNotifications.Add {
            notification with
                Life = newLife
                Position = newPosition
          }

      stateWrite.SetNotifications(updatedNotifications.ToArray())

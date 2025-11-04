namespace Pomo.Core.Domains

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Input.Touch

open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Systems

module Input =
  open Pomo.Core

  type InputSystem(game: Game, player: Guid<EntityId>) as this =
    inherit GameSystem(game)
    let mutable prevKeyboard = Keyboard.GetState()
    let mutable prevGamePad = GamePad.GetState(PlayerIndex.One)
    let mutable lastVelocity = Vector2.Zero
    let speed = 100.0f
    let gamepadDeadZoneSq = 0.2f * 0.2f

    override _.Update(gameTime) =
      let keyboard = Keyboard.GetState()
      let gamePad = GamePad.GetState(PlayerIndex.One)
      let mutable move = Vector2.Zero

      // Keyboard input (WASD/Arrows)
      if keyboard.IsKeyDown(Keys.W) || keyboard.IsKeyDown(Keys.Up) then
        move <- move + Vector2.UnitY * -1.0f

      if keyboard.IsKeyDown(Keys.S) || keyboard.IsKeyDown(Keys.Down) then
        move <- move + Vector2.UnitY

      if keyboard.IsKeyDown(Keys.A) || keyboard.IsKeyDown(Keys.Left) then
        move <- move + Vector2.UnitX * -1.0f

      if keyboard.IsKeyDown(Keys.D) || keyboard.IsKeyDown(Keys.Right) then
        move <- move + Vector2.UnitX

      // Gamepad input (left stick)
      let leftStick = gamePad.ThumbSticks.Left
      let lsq = leftStick.LengthSquared()

      if lsq > gamepadDeadZoneSq then
        move <- move + Vector2(leftStick.X, -leftStick.Y)

      // Normalize move vector
      if move.LengthSquared() > 1.0f then
        move <- Vector2.Normalize(move)

      let velocity =
        let m2 = move.LengthSquared()

        if m2 > 0.0001f then
          Vector2.Normalize(move) * speed
        else
          Vector2.Zero

      if velocity <> lastVelocity then
        this.EventBus.Publish(World.VelocityChanged struct (player, velocity))
        lastVelocity <- velocity

      prevKeyboard <- keyboard
      prevGamePad <- gamePad

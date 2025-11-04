namespace Pomo.Core.Domains

open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Input.Touch
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Systems
open Pomo.Core.Domain.EventBus
open Pomo.Core.Domain.RawInput
open Pomo.Core.Domain.Units

module RawInput =
  open Microsoft.Xna.Framework

  type RawInputSystem(game: Game, entityId: Guid<EntityId>) as this =
    inherit GameSystem(game)


    let prevInputState = this.World.RawInputStates |> AMap.tryFind entityId

    override val Kind = RawInput with get

    override this.Update _ =
      let currentKeyboard = Keyboard.GetState()
      let currentMouse = Mouse.GetState()
      let currentGamePad = GamePad.GetState(PlayerIndex.One) // Assuming Player One
      let currentTouch = TouchPanel.GetState()

      let prevInputState =
        prevInputState
        |> AVal.force
        |> Option.defaultValue {
          Keyboard = currentKeyboard
          Mouse = currentMouse
          GamePad = currentGamePad
          Touch = currentTouch
          PrevKeyboard = currentKeyboard
          PrevMouse = currentMouse
          PrevGamePad = currentGamePad
          PrevTouch = currentTouch
        }

      let newRawInputState = {
        Keyboard = currentKeyboard
        Mouse = currentMouse
        GamePad = currentGamePad
        Touch = currentTouch
        PrevKeyboard = prevInputState.Keyboard
        PrevMouse = prevInputState.Mouse
        PrevGamePad = prevInputState.GamePad
        PrevTouch = prevInputState.Touch
      }

      this.EventBus.Publish(
        RawInputStateChanged struct (entityId, newRawInputState)
      )

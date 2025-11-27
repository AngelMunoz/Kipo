namespace Pomo.Core.Systems

open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Input.Touch
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.RawInput
open Pomo.Core.Systems.Systems

module RawInput =
  open Microsoft.Xna.Framework


  open Pomo.Core.Environment
  open Pomo.Core.Environment.Patterns

  type RawInputSystem
    (game: Game, env: PomoEnvironment, entityId: Guid<EntityId>) =
    inherit GameSystem(game)

    let (Core core) = env.CoreServices

    let prevInputState = core.World.RawInputStates |> AMap.tryFind entityId

    override val Kind = RawInput with get

    override this.Update _ =
      let currentKeyboard = Keyboard.GetState()
      let currentMouse = Mouse.GetState()
      let currentGamePad = GamePad.GetState(PlayerIndex.One) // Assuming Player One
      let currentTouch = TouchPanel.GetState()

      let isMouseOverUI = core.UIService.IsMouseOverUI |> AVal.force

      // If mouse is over UI, we suppress mouse button clicks for the game world
      let effectiveMouse =
        if isMouseOverUI then
          // Keep position, but release buttons
          MouseState(
            currentMouse.X,
            currentMouse.Y,
            currentMouse.ScrollWheelValue,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released
          )
        else
          currentMouse

      let prevInputState =
        prevInputState
        |> AVal.force
        |> Option.defaultValue {
          Keyboard = currentKeyboard
          Mouse = effectiveMouse
          GamePad = currentGamePad
          Touch = currentTouch
          PrevKeyboard = currentKeyboard
          PrevMouse = effectiveMouse
          PrevGamePad = currentGamePad
          PrevTouch = currentTouch
        }

      let newRawInputState = {
        Keyboard = currentKeyboard
        Mouse = effectiveMouse
        GamePad = currentGamePad
        Touch = currentTouch
        PrevKeyboard = prevInputState.Keyboard
        PrevMouse = prevInputState.Mouse
        PrevGamePad = prevInputState.GamePad
        PrevTouch = prevInputState.Touch
      }

      core.EventBus.Publish(
        StateChangeEvent.Input(
          InputEvents.RawStateChanged struct (entityId, newRawInputState)
        )
      )

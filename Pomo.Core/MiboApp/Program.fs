namespace Pomo.Core.MiboApp

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

module Program =

  let private update
    (msg: AppMsg)
    (model: AppModel)
    : struct (AppModel * Cmd<AppMsg>) =
    match msg with
    | Tick gt ->
      match model.CurrentScene with
      | Editor state ->
        let state = Editor.update gt state

        { CurrentScene = Editor state }, Cmd.none
      | MainMenu -> model, Cmd.none
    | TransitionTo scene -> { CurrentScene = scene }, Cmd.none

  let private view
    (ctx: GameContext)
    (model: AppModel)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    match model.CurrentScene with
    | Editor state -> Editor.view ctx state buffer
    | MainMenu ->
      Draw3D.viewport ctx.GraphicsDevice.Viewport buffer
      Draw3D.clear (ValueSome Color.Black) true buffer

  let private init(ctx: GameContext) : struct (AppModel * Cmd<AppMsg>) =
    let editorState = Editor.createEmpty()
    { CurrentScene = Editor editorState }, Cmd.none

  let create() =
    Mibo.Elmish.Program.mkProgram init update
    |> Program.withInput
    |> Program.withAssets
    |> Program.withRenderer(Batch3DRenderer.create view)
    |> Program.withTick Tick

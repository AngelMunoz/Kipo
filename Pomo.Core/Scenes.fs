namespace Pomo.Core.Scenes

open System
open Microsoft.Xna.Framework


type SceneComponentCollection() =
  let components = ResizeArray<IGameComponent>()
  let updateables = ResizeArray<IUpdateable>()
  let drawables = ResizeArray<IDrawable>()

  member _.Add(gameComponent: IGameComponent) =
    components.Add(gameComponent)

    match gameComponent with
    | :? IUpdateable as u -> updateables.Add(u)
    | _ -> ()

    match gameComponent with
    | :? IDrawable as d -> drawables.Add(d)
    | _ -> ()

  member _.Sort() =
    updateables.Sort(fun a b -> a.UpdateOrder.CompareTo(b.UpdateOrder))
    drawables.Sort(fun a b -> a.DrawOrder.CompareTo(b.DrawOrder))

  member _.Initialize() =
    for c in components do
      c.Initialize()

  member _.Update(gameTime: GameTime) =
    for u in updateables do
      if u.Enabled then
        u.Update(gameTime)

  member _.Draw(gameTime: GameTime) =
    for d in drawables do
      if d.Visible then
        d.Draw(gameTime)

  member _.Dispose() =
    for c in components do
      match c with
      | :? IDisposable as d -> d.Dispose()
      | _ -> ()

    components.Clear()
    updateables.Clear()
    drawables.Clear()

[<AbstractClass>]
type Scene() =

  abstract member Initialize: unit -> unit
  default _.Initialize() = ()

  abstract member Update: GameTime -> unit
  default _.Update(_) = ()

  abstract member Draw: GameTime -> unit
  default _.Draw(_) = ()


  abstract member LoadMap: string -> unit
  default _.LoadMap(_) = ()

  abstract member Dispose: unit -> unit
  default _.Dispose() = ()

  interface IDisposable with
    member this.Dispose() = this.Dispose()


type SceneManager() =
  let mutable currentScene: Scene voption = ValueNone
  let mutable nextScene: Scene voption = ValueNone

  member _.LoadScene(scene: Scene) = nextScene <- ValueSome scene

  member _.Update(gameTime: GameTime) =
    nextScene
    |> ValueOption.iter(fun newScene ->
      currentScene |> ValueOption.iter(fun s -> s.Dispose())
      currentScene <- ValueSome newScene
      newScene.Initialize()
      nextScene <- ValueNone)

    currentScene |> ValueOption.iter(fun s -> s.Update gameTime)

  member _.Draw(gameTime: GameTime) =
    currentScene |> ValueOption.iter(fun s -> s.Draw gameTime)

  interface IDisposable with
    member _.Dispose() =
      currentScene |> ValueOption.iter(fun s -> s.Dispose())
      nextScene |> ValueOption.iter(fun s -> s.Dispose())

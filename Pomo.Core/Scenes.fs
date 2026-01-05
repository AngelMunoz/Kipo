namespace Pomo.Core.Scenes

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.Data.Adaptive
open Myra.Graphics2D.UI
open Pomo.Core
open Pomo.Core.Domain.Scenes
open Pomo.Core.Domain.UI

type SceneManager
  (
    game: Game,
    hudService: IHUDService,
    sceneEvents: IObservable<Scene>,
    loader: Scene -> struct (IGameComponent list * IDisposable)
  ) =
  inherit DrawableGameComponent(game)

  let mutable desktop: Desktop voption = ValueNone
  let mutable currentDisposable: IDisposable voption = ValueNone
  let mutable currentComponents: IGameComponent list = []
  let mutable subscription: IDisposable voption = ValueNone
  let mutable nextScene: Scene voption = ValueNone

  let switchTo(scene: Scene) =
    // 1. Remove components first to ensure they stop updating/drawing
    for c in currentComponents do
      game.Components.Remove(c) |> ignore

    // 2. Cleanup old scene
    currentDisposable |> ValueOption.iter(fun d -> d.Dispose())

    for c in currentComponents do
      match c with
      | :? IDisposable as d -> d.Dispose()
      | _ -> ()

    // 3. Load new scene
    let struct (newComponents, newDisposable) = loader scene

    // 4. Register new components
    for c in newComponents do
      game.Components.Add(c)

    // 5. Update state
    currentComponents <- newComponents
    currentDisposable <- ValueSome newDisposable

  override _.Initialize() =
    base.Initialize()

    let root =
      Systems.HUDComponents.globalOverlay
        hudService.Config
        hudService.LoadingOverlayVisible

    desktop <- ValueSome(new Desktop(Root = root))

    subscription <-
      ValueSome(
        sceneEvents
        |> Observable.subscribe(fun scene -> nextScene <- ValueSome scene)
      )

  override _.Update(gameTime) =
    match nextScene with
    | ValueSome scene ->
      switchTo scene
      nextScene <- ValueNone
    | ValueNone -> ()

  override _.Draw _ =
    desktop |> ValueOption.iter(fun d -> d.Render())

  override _.Dispose(disposing: bool) =
    if disposing then
      subscription |> ValueOption.iter(fun s -> s.Dispose())
      currentDisposable |> ValueOption.iter(fun d -> d.Dispose())

      for c in currentComponents do
        game.Components.Remove(c) |> ignore

        match c with
        | :? IDisposable as d -> d.Dispose()
        | _ -> ()

      currentComponents <- []

    base.Dispose disposing

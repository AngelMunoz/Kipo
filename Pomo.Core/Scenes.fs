namespace Pomo.Core.Scenes

open System
open Microsoft.Xna.Framework

/// <summary>
/// Helper class to manage a collection of GameComponents within a Scene.
/// Handles sorting, updating, drawing, and disposing.
/// </summary>
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

  /// <summary>
  /// Sorts components by UpdateOrder and DrawOrder.
  /// Call this after adding all components.
  /// </summary>
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

/// <summary>
/// Represents a distinct state of the game (e.g., Main Menu, Gameplay, Credits).
/// It owns its own World, Systems, and UI.
/// Implementations should prefer F# Object Expressions.
/// </summary>
[<AbstractClass>]
type Scene() =
  /// <summary>
  /// Called once when the scene is loaded.
  /// </summary>
  abstract member Initialize: unit -> unit
  default _.Initialize() = ()

  /// <summary>
  /// Called every frame to update game logic.
  /// </summary>
  abstract member Update: GameTime -> unit
  default _.Update(_) = ()

  /// <summary>
  /// Called every frame to draw elements to the screen.
  /// </summary>
  abstract member Draw: GameTime -> unit
  default _.Draw(_) = ()


  abstract member LoadMap: string -> unit
  default _.LoadMap(_) = ()

  abstract member Dispose: unit -> unit
  default _.Dispose() = ()

  interface IDisposable with
    member this.Dispose() = this.Dispose()

/// <summary>
/// Manages the active scene and transitions between them.
/// </summary>
type SceneManager(game: Game) =
  let mutable currentScene: Scene voption = ValueNone

  /// <summary>
  /// Unloads the current scene (disposing it) and loads the new one.
  /// </summary>
  member this.LoadScene(scene: Scene) =
    // Dispose of the old scene if one exists
    currentScene |> ValueOption.iter(fun s -> s.Dispose())

    currentScene <- ValueSome scene
    scene.Initialize()

  /// <summary>
  /// Updates the currently active scene.
  /// </summary>
  member this.Update(gameTime: GameTime) =
    currentScene |> ValueOption.iter(fun s -> s.Update(gameTime))

  /// <summary>
  /// Draws the currently active scene.
  /// </summary>
  member this.Draw(gameTime: GameTime) =
    currentScene |> ValueOption.iter(fun s -> s.Draw(gameTime))

  /// <summary>
  /// Disposes the current scene when the manager itself is disposed.
  /// </summary>
  interface IDisposable with
    member this.Dispose() =
      currentScene |> ValueOption.iter(fun s -> s.Dispose())

namespace Pomo.iOS

open Pomo.Core
open Foundation
open UIKit

[<Register("AppDelegate")>]
type Program() =
  inherit UIApplicationDelegate()

  static member private game: PomoGame ref = ref Unchecked.defaultof<PomoGame>

  /// <summary>
  /// Initializes and starts the game by creating an instance of the
  /// Game class and invoking its Run method.
  /// </summary>
  static member private RunGame() =
    Program.game.Value <- new PomoGame()
    Program.game.Value.Run()

  /// <summary>
  /// Called when the application has finished launching.
  /// This method starts the game by calling RunGame.
  /// </summary>
  /// <param name="app">The UIApplication instance representing the application.</param>
  override this.FinishedLaunching(app: UIApplication) = Program.RunGame()

  /// <summary>
  /// The main entry point for the application.
  /// This sets up the application and specifies the UIApplicationDelegate
  /// class to handle application lifecycle events.
  /// </summary>
  /// <param name="args">Command-line arguments passed to the application.</param>
  static member Main(args: string[]) =
    UIApplication.Main(args, null, typeof<Program>)

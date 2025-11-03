namespace Pomo.Android

open Android.App
open Android.Content.PM
open Android.OS
open Android.Views

open Microsoft.Xna.Framework

open Pomo.Core

/// <summary>
/// The main activity for the Android application. It initializes the game instance,
/// sets up the rendering view, and starts the game loop.
/// </summary>
/// <remarks>
/// This class is responsible for managing the Android activity lifecycle and integrating
/// with the MonoGame framework.
/// </remarks>
[<Activity(Label = "Pomo",
           MainLauncher = true,
           Icon = "@drawable/icon",
           Theme = "@style/Theme.Splash",
           AlwaysRetainTaskState = true,
           LaunchMode = LaunchMode.SingleInstance,
           ScreenOrientation = ScreenOrientation.SensorLandscape,
           ConfigurationChanges =
             (ConfigChanges.Orientation
              ||| ConfigChanges.Keyboard
              ||| ConfigChanges.KeyboardHidden))>]
type MainActivity() =
  inherit AndroidGameActivity()

  let mutable game: PomoGame = Unchecked.defaultof<PomoGame>
  let mutable view: View = Unchecked.defaultof<View>

  /// <summary>
  /// Called when the activity is first created. Initializes the game instance,
  /// retrieves its rendering view, and sets it as the content view of the activity.
  /// Finally, starts the game loop.
  /// </summary>
  /// <param name="bundle">A Bundle containing the activity's previously saved state, if any.</param>
  override this.OnCreate(bundle: Bundle) =
    base.OnCreate bundle

    // Create a new instance of the game.
    game <- new PomoGame()

    // Retrieve the rendering view from the game services.
    view <- game.Services.GetService typeof<View> :?> View

    // Set the content view to the game's rendering view.
    this.SetContentView view

    // Start the game loop.
    game.Run()

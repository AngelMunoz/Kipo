namespace Pomo.Core

open Microsoft.Xna.Framework
open Myra.Graphics2D.UI
open System

// Selection Screen derived from GameScreen
type SelectionScreen(game: Game) =
  inherit GameScreen(game)
  override this.BuildUI() =
    let panel = new VerticalStackPanel(Spacing = 16)
    panel.HorizontalAlignment <- HorizontalAlignment.Center
    panel.VerticalAlignment <- VerticalAlignment.Center
    panel.Widgets.Add(new Label(Text = "Selection Screen"))

    let addButton text onClick =
      let b = new Button(Width = 180)
      b.Content <- new Label(Text = text)
      b.Click.Add(onClick)
      panel.Widgets.Add(b)

    // Access ScreenManager via services
    let mgr = game.Services.GetService(typeof<ScreenManager>) :?> ScreenManager

    addButton "New Game" (fun _ -> mgr.Change(new GameplayScreen(game)))
    addButton "Settings" (fun _ -> mgr.Change(new SettingsScreen(game)))
    addButton "Exit" (fun _ -> game.Exit())
    panel :> Widget

// Gameplay Screen
and GameplayScreen(game: Game) =
  inherit GameScreen(game)
  override this.BuildUI() =
    let panel = new VerticalStackPanel(Spacing = 12)
    panel.HorizontalAlignment <- HorizontalAlignment.Center
    panel.VerticalAlignment <- VerticalAlignment.Center
    panel.Widgets.Add(new Label(Text = "Gameplay Screen"))
    let btnBack = new Button(Width = 180)
    btnBack.Content <- new Label(Text = "Back")
    btnBack.Click.Add(fun _ ->
      let mgr = game.Services.GetService(typeof<ScreenManager>) :?> ScreenManager
      mgr.Change(new SelectionScreen(game)))
    panel.Widgets.Add(btnBack)
    panel :> Widget

// Settings Screen
and SettingsScreen(game: Game) =
  inherit GameScreen(game)
  override this.BuildUI() =
    let panel = new VerticalStackPanel(Spacing = 12)
    panel.HorizontalAlignment <- HorizontalAlignment.Center
    panel.VerticalAlignment <- VerticalAlignment.Center
    panel.Widgets.Add(new Label(Text = "Settings Screen"))
    let chk = new CheckBox()
    chk.Text <- "Enable Fancy Option"
    panel.Widgets.Add(chk)
    let btnBack = new Button(Width = 180)
    btnBack.Content <- new Label(Text = "Back")
    btnBack.Click.Add(fun _ ->
      let mgr = game.Services.GetService(typeof<ScreenManager>) :?> ScreenManager
      mgr.Change(new SelectionScreen(game)))
    panel.Widgets.Add(btnBack)
    panel :> Widget

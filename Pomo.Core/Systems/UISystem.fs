namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Myra
open Myra.Graphics2D
open Myra.Graphics2D.UI
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Map
open Pomo.Core.Systems
open Pomo.Core.Environment.Patterns
open Pomo.Core.EventBus
open Pomo.Core.Stores
open Pomo.Core.Systems.UIService // Required for GuiAction

module MainMenuUI =
  let build (game: Game) (publishGuiAction: GuiAction -> unit) =
    let panel = new VerticalStackPanel(Spacing = 16)
    panel.HorizontalAlignment <- HorizontalAlignment.Center
    panel.VerticalAlignment <- VerticalAlignment.Center
    panel.Widgets.Add(new Label(Text = "Selection Screen"))

    let addButton text onClick =
      let b = new Button(Width = 180)
      b.Content <- new Label(Text = text)
      b.Click.Add(onClick)
      panel.Widgets.Add(b)

    addButton "New Game" (fun _ -> publishGuiAction GuiAction.StartNewGame)

    addButton "Settings" (fun _ -> publishGuiAction GuiAction.OpenSettings)
    addButton "Exit" (fun _ -> publishGuiAction GuiAction.ExitGame)

    panel

module GameplayUI =
  let build (game: Game) (publishGuiAction: GuiAction -> unit) =
    let panel = new Panel()

    let topPanel = new HorizontalStackPanel(Spacing = 8)
    topPanel.HorizontalAlignment <- HorizontalAlignment.Right
    topPanel.VerticalAlignment <- VerticalAlignment.Top
    topPanel.Padding <- Thickness(10)

    let backButton = new Button()
    backButton.Content <- new Label(Text = "Back to Main Menu")

    backButton.Click.Add(fun _ -> publishGuiAction GuiAction.BackToMainMenu)

    topPanel.Widgets.Add(backButton)
    panel.Widgets.Add(topPanel)

    panel

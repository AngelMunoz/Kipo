namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Myra
open Myra.Graphics2D
open Myra.Graphics2D.UI
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Map
open Pomo.Core.Systems
open Pomo.Core.Environment.Patterns
open Pomo.Core.EventBus
open Pomo.Core.Stores
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Action
open Pomo.Core.Systems.UIService // Required for GuiAction
open Pomo.Core.Environment

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

module private UIHelpers =
  let mapAnchor
    (anchor: UI.HUDAnchor)
    : struct (HorizontalAlignment * VerticalAlignment) =
    match anchor with
    | UI.TopLeft -> HorizontalAlignment.Left, VerticalAlignment.Top
    | UI.TopCenter -> HorizontalAlignment.Center, VerticalAlignment.Top
    | UI.TopRight -> HorizontalAlignment.Right, VerticalAlignment.Top
    | UI.CenterLeft -> HorizontalAlignment.Left, VerticalAlignment.Center
    | UI.Center -> HorizontalAlignment.Center, VerticalAlignment.Center
    | UI.CenterRight -> HorizontalAlignment.Right, VerticalAlignment.Center
    | UI.BottomLeft -> HorizontalAlignment.Left, VerticalAlignment.Bottom
    | UI.BottomCenter -> HorizontalAlignment.Center, VerticalAlignment.Bottom
    | UI.BottomRight -> HorizontalAlignment.Right, VerticalAlignment.Bottom

module GameplayUI =
  let build
    (game: Game)
    (env: PomoEnvironment)
    (playerId: Guid<EntityId>)
    (publishGuiAction: GuiAction -> unit)
    =
    let panel = new Panel()

    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let hudService = core.HUDService
    let resources = core.World.Resources
    let derivedStats = gameplay.Projections.DerivedStats

    // 1. Top Panel (Existing)
    let topPanel = new HorizontalStackPanel(Spacing = 8)
    topPanel.HorizontalAlignment <- HorizontalAlignment.Right
    topPanel.VerticalAlignment <- VerticalAlignment.Top
    topPanel.Padding <- Thickness(10)

    let backButton = new Button()
    backButton.Content <- new Label(Text = "Back to Main Menu")
    backButton.Click.Add(fun _ -> publishGuiAction GuiAction.BackToMainMenu)

    topPanel.Widgets.Add(backButton)
    panel.Widgets.Add(topPanel)

    // 2. HUD Components
    let config = hudService.Config
    let worldTime = core.World.Time

    // Player Vitals
    let layout = (AVal.force config).Layout.PlayerVitals

    let resourceZero: Entity.Resource = {
      HP = 0
      MP = 0
      Status = Entity.Status.Dead
    }

    let derivedStatsZero: Entity.DerivedStats = {
      AP = 0
      AC = 0
      DX = 0
      MP = 0
      MA = 0
      MD = 0
      WT = 0
      DA = 0
      LK = 0
      HP = 0
      DP = 0
      HV = 0
      MS = 0
      HPRegen = 0
      MPRegen = 0
      ElementAttributes = HashMap.empty
      ElementResistances = HashMap.empty
    }

    let playerResources =
      resources
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue resourceZero)

    let derivedStats =
      derivedStats
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue derivedStatsZero)

    if layout.Visible then
      let playerVitals =
        HUDComponents.createPlayerVitals
          config
          worldTime
          playerResources
          derivedStats

      let struct (hAlign, vAlign) = UIHelpers.mapAnchor layout.Anchor
      playerVitals.HorizontalAlignment <- hAlign
      playerVitals.VerticalAlignment <- vAlign
      playerVitals.Left <- layout.OffsetX
      playerVitals.Top <- layout.OffsetY

      panel.Widgets.Add(playerVitals)

    // Action Bar
    let actionBarLayout = (AVal.force config).Layout.ActionBar

    if actionBarLayout.Visible then
      let actionSetsEmpty =
        HashMap.empty<int, HashMap<Action.GameAction, SlotProcessing>>

      let actionSets =
        core.World.ActionSets
        |> AMap.tryFind playerId
        |> AVal.map(Option.defaultValue actionSetsEmpty)

      let activeSetIndex =
        core.World.ActiveActionSets
        |> AMap.tryFind playerId
        |> AVal.map(Option.defaultValue 0)

      let cooldownsEmpty = HashMap.empty<int<SkillId>, TimeSpan>

      let cooldowns =
        core.World.AbilityCooldowns
        |> AMap.tryFind playerId
        |> AVal.map(Option.defaultValue cooldownsEmpty)

      let inputMapEmpty = HashMap.empty<RawInput, GameAction>

      let inputMap =
        core.World.InputMaps
        |> AMap.tryFind playerId
        |> AVal.map(Option.defaultValue inputMapEmpty)

      let (Stores stores) = env.StoreServices

      let actionBar =
        HUDComponents.createActionBar
          config
          worldTime
          actionSets
          activeSetIndex
          cooldowns
          inputMap
          stores.SkillStore

      let struct (hAlign, vAlign) = UIHelpers.mapAnchor actionBarLayout.Anchor
      actionBar.HorizontalAlignment <- hAlign
      actionBar.VerticalAlignment <- vAlign
      actionBar.Left <- actionBarLayout.OffsetX
      actionBar.Top <- actionBarLayout.OffsetY

      panel.Widgets.Add(actionBar)

    panel

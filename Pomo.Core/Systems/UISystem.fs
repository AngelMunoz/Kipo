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
open Pomo.Core.UI

module MainMenuUI =
  let build (game: Game) (publishGuiAction: GuiAction -> unit) =
    let titleLabel = Label.create "Selection Screen"

    let newGameBtn =
      Btn.create "New Game"
      |> W.width 180
      |> Btn.onClick(fun () -> publishGuiAction GuiAction.StartNewGame)

    let settingsBtn =
      Btn.create "Settings"
      |> W.width 180
      |> Btn.onClick(fun () -> publishGuiAction GuiAction.OpenSettings)

    let exitBtn =
      Btn.create "Exit"
      |> W.width 180
      |> Btn.onClick(fun () -> publishGuiAction GuiAction.ExitGame)

    VStack.spaced 16
    |> W.hAlign HorizontalAlignment.Center
    |> W.vAlign VerticalAlignment.Center
    |> W.childrenV [ titleLabel; newGameBtn; settingsBtn; exitBtn ]

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
  open UIHelpers

  /// Apply layout properties to a widget using DSL
  let private applyLayout (layout: UI.HUDComponentLayout) (widget: Widget) =
    let struct (hAlign, vAlign) = mapAnchor layout.Anchor

    widget
    |> W.hAlign hAlign
    |> W.vAlign vAlign
    |> W.left layout.OffsetX
    |> W.top layout.OffsetY

  let build
    (game: Game)
    (env: PomoEnvironment)
    (playerId: Guid<EntityId>)
    (publishGuiAction: GuiAction -> unit)
    =
    let panel = Panel.create()

    let (Core core) = env.CoreServices
    let (Gameplay gameplay) = env.GameplayServices
    let (Stores stores) = env.StoreServices
    let hudService = core.HUDService
    let config = hudService.Config
    let worldTime = core.World.Time
    let resources = core.World.Resources
    let derivedStats = gameplay.Projections.DerivedStats

    // --- Fixed top panel (always visible) ---
    let topPanel =
      HStack.create()
      |> W.hAlign HorizontalAlignment.Right
      |> W.vAlign VerticalAlignment.Top
      |> W.padding 10
      |> W.spacing 8

    let backButton =
      Btn.create "Back to Main Menu"
      |> Btn.onClick(fun () -> publishGuiAction GuiAction.BackToMainMenu)

    topPanel.Widgets.Add(backButton)

    // --- Prepare data sources ---
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

    let playerDerivedStats =
      derivedStats
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue derivedStatsZero)

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

    let activeEffects =
      core.World.ActiveEffects
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue IndexList.empty)

    // Target frame placeholder (deferred - always ValueNone for now)
    let selectedEntityId = AVal.constant ValueNone

    // --- Create HUD components (unconditionally) ---
    let playerVitals =
      HUDComponents.createPlayerVitals
        config
        worldTime
        playerResources
        playerDerivedStats

    let actionBar: Widget =
      let playerInventory =
        gameplay.Projections.ResolvedInventories |> AMap.tryFind playerId

      HUDComponents.createActionBar
        config
        worldTime
        actionSets
        activeSetIndex
        cooldowns
        inputMap
        playerInventory
        stores.SkillStore

    let statusEffectsBar: Widget =
      HUDComponents.createStatusEffectsBar config worldTime activeEffects

    let targetFrame: Widget =
      HUDComponents.createTargetFrame
        config
        worldTime
        selectedEntityId
        resources
        derivedStats
        core.World.Factions

    let castBar: Widget =
      HUDComponents.createCastBar
        config
        worldTime
        core.World.ActiveCharges
        playerId
        stores.SkillStore

    // --- Build reactive children list based on layout visibility ---
    let layout = config |> AVal.map _.Layout

    let hudChildren =
      layout
      |> AVal.map(fun l -> [
        // Top panel is always included
        topPanel :> Widget

        if l.PlayerVitals.Visible then
          applyLayout l.PlayerVitals playerVitals

        if l.ActionBar.Visible then
          applyLayout l.ActionBar actionBar

        if l.StatusEffects.Visible then
          applyLayout l.StatusEffects statusEffectsBar

        if l.TargetFrame.Visible then
          applyLayout l.TargetFrame targetFrame

        if l.CastBar.Visible then
          applyLayout l.CastBar castBar
      ])

    panel |> Panel.bindChildren hudChildren |> ignore

    panel

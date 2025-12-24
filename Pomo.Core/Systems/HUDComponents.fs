namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Myra.Graphics2D.UI
open Myra.Graphics2D
open FSharp.Data.Adaptive
open Pomo.Core.Domain.UI
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.World
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.RawInput
open Pomo.Core.Stores
open Pomo.Core.Systems.HUDAnimation
open Microsoft.Xna.Framework.Input

// Alias to avoid conflict with Skill.GroundAreaKind.Rectangle
type Rect = Microsoft.Xna.Framework.Rectangle

module HUDComponents =

  [<Struct>]
  type ResourceColorType =
    | Health
    | Mana

  let createResourceBar
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (colorType: ResourceColorType)
    =
    let mutable colorFill = Color.Red
    let mutable colorLow = Color.Red
    let mutable colorBg = Color.Black
    let mutable visualValue = 0.0f
    let mutable targetValue = 0.0f
    let mutable maxValue = 100.0f
    let mutable pulse = Pulse.create()
    let mutable lastTime = TimeSpan.Zero

    config.AddWeakCallback(fun cfg ->
      let colors = cfg.Theme.Colors

      match colorType with
      | Health ->
        colorFill <- colors.HealthFill
        colorLow <- colors.HealthLow
        colorBg <- colors.HealthBackground
      | Mana ->
        colorFill <- colors.ManaFill
        colorLow <- colors.ManaFill
        colorBg <- colors.ManaBackground)
    |> ignore

    let widget =
      { new Widget() with
          override this.InternalRender(context) =
            let bounds = this.ActualBounds

            let time = AVal.force worldTime

            let dt =
              if lastTime = TimeSpan.Zero then
                0.016f
              else
                let elapsed = time.TotalGameTime - lastTime
                float32 elapsed.TotalSeconds |> min 0.1f

            lastTime <- time.TotalGameTime

            visualValue <- Lerp.smoothDamp visualValue targetValue 0.15f dt

            context.FillRectangle(bounds, colorBg)

            let fillPct = MathHelper.Clamp(visualValue / maxValue, 0.0f, 1.0f)
            let fillWidth = int(float32 bounds.Width * fillPct)

            if fillWidth > 0 then
              let fillRect = Rect(bounds.X, bounds.Y, fillWidth, bounds.Height)

              let isLowHealth = fillPct < 0.25f

              let finalColor =
                if isLowHealth then
                  pulse <- Pulse.update 2.0f dt pulse
                  Color.Lerp(colorFill, colorLow, pulse.Intensity)
                else
                  colorFill

              context.FillRectangle(fillRect, finalColor)
      }

    widget.ClipToBounds <- true

    let update (current: float32) (max: float32) =
      if max <> maxValue then
        maxValue <- max

      if current <> targetValue then
        targetValue <- current

    struct (widget, update)


  let createPlayerVitals
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (resources: Resource aval)
    (derivedStats: DerivedStats aval)
    =
    let panel = new VerticalStackPanel()

    let struct (hpWidget, updateHp) = createResourceBar config worldTime Health

    hpWidget.Height <- 16
    hpWidget.Width <- 200
    hpWidget.HorizontalAlignment <- HorizontalAlignment.Left

    let struct (mpWidget, updateMp) = createResourceBar config worldTime Mana

    mpWidget.Height <- 8
    mpWidget.Width <- 200
    mpWidget.HorizontalAlignment <- HorizontalAlignment.Left
    mpWidget.Margin <- Thickness(0, 4, 0, 0)

    let hpLabel = new Label()
    hpLabel.HorizontalAlignment <- HorizontalAlignment.Center
    hpLabel.VerticalAlignment <- VerticalAlignment.Center
    hpLabel.Margin <- Thickness(0, -20, 0, 0)
    hpLabel.TextColor <- Color.White

    panel.Widgets.Add(hpWidget)
    panel.Widgets.Add(mpWidget)
    panel.Widgets.Add(hpLabel)

    let resourcesData =
      (resources, derivedStats)
      ||> AVal.map2(fun res derived ->
        struct (res.HP, derived.HP, res.MP, derived.MP))

    let _subscription =
      resourcesData.AddWeakCallback(fun (struct (hp, maxHp, mp, maxMp)) ->
        updateHp (float32 hp) (float32 maxHp)
        updateMp (float32 mp) (float32 maxMp)
        hpLabel.Text <- $"{int hp} / {int maxHp}")

    panel.Tag <- _subscription :> obj
    panel


  let private getSkillAbbreviation
    (skillStore: SkillStore)
    (skillId: int<SkillId>)
    =
    match skillStore.tryFind skillId with
    | ValueSome(Skill.Active active) ->
      let name = active.Name

      if name.Length <= 2 then
        name
      elif name.Contains(" ") then
        let words = name.Split(' ')
        $"{words.[0].[0]}{words.[1].[0]}"
      else
        name.Substring(0, min 2 name.Length)
    | ValueSome(Skill.Passive passive) ->
      let name = passive.Name

      if name.Length <= 2 then
        name
      else
        name.Substring(0, min 2 name.Length)
    | ValueNone -> "??"


  let private slotActions = [|
    GameAction.UseSlot1
    GameAction.UseSlot2
    GameAction.UseSlot3
    GameAction.UseSlot4
    GameAction.UseSlot5
    GameAction.UseSlot6
    GameAction.UseSlot7
    GameAction.UseSlot8
  |]

  let private slotCount = slotActions.Length


  let private getKeyLabelForAction (inputMap: InputMap) (action: GameAction) =
    // Reverse lookup: find the RawInput that maps to this action
    inputMap
    |> HashMap.fold
      (fun acc rawInput mappedAction ->
        match acc with
        | Some _ -> acc // Already found
        | None ->
          if mappedAction = action then
            match rawInput with
            | Key k ->
              let name = k.ToString()

              if name.StartsWith("D") && name.Length = 2 then
                Some(name.Substring(1)) // D1 -> 1
              else
                Some name
            | _ -> None
          else
            None)
      None
    |> Option.defaultValue "?"


  let createActionSlot (config: HUDConfig aval) (worldTime: Time aval) =
    let mutable skillAbbrev = ""
    let mutable cooldownEndTime = TimeSpan.Zero
    let mutable bgColor = Color.DarkSlateGray
    let mutable cooldownColor = Color(0, 0, 0, 160)
    let mutable lastTime = TimeSpan.Zero
    let mutable keybindText = "?"

    config.AddWeakCallback(fun cfg ->
      cooldownColor <- cfg.Theme.CooldownOverlayColor)
    |> ignore

    let keyLabel = new Label()

    let widget =
      { new Widget() with
          override this.InternalRender(context) =
            let bounds = this.ActualBounds
            let time = AVal.force worldTime
            lastTime <- time.TotalGameTime

            // Background
            context.FillRectangle(bounds, bgColor)

            // Cooldown overlay (vertical sweep from bottom)
            if cooldownEndTime > time.TotalGameTime then
              let remaining =
                (cooldownEndTime - time.TotalGameTime).TotalSeconds

              let cdPct =
                MathHelper.Clamp(float32 remaining / 10.0f, 0.0f, 1.0f)

              let overlayHeight = int(float32 bounds.Height * cdPct)

              if overlayHeight > 0 then
                let overlayRect =
                  Rect(
                    bounds.X,
                    bounds.Y + bounds.Height - overlayHeight,
                    bounds.Width,
                    overlayHeight
                  )

                context.FillRectangle(overlayRect, cooldownColor)

            // Border
            let borderRect =
              Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height)

            context.DrawRectangle(borderRect, Color.Gray, 1.0f)
      }

    widget.Width <- 40
    widget.Height <- 40
    widget.ClipToBounds <- true

    // Overlay container for text
    let container = new Panel()
    container.Width <- 40
    container.Height <- 40

    container.Widgets.Add(widget)

    // Skill abbreviation label
    let abbrevLabel = new Label()
    abbrevLabel.HorizontalAlignment <- HorizontalAlignment.Center
    abbrevLabel.VerticalAlignment <- VerticalAlignment.Center
    abbrevLabel.TextColor <- Color.White
    container.Widgets.Add(abbrevLabel)

    // Keybind label in corner
    keyLabel.Text <- keybindText
    keyLabel.HorizontalAlignment <- HorizontalAlignment.Right
    keyLabel.VerticalAlignment <- VerticalAlignment.Top
    keyLabel.TextColor <- Color.LightGray
    keyLabel.Margin <- Thickness(0, 2, 4, 0)
    container.Widgets.Add(keyLabel)

    let updateSlot (abbrev: string) (cdEnd: TimeSpan) (keyLbl: string) =
      skillAbbrev <- abbrev
      cooldownEndTime <- cdEnd
      abbrevLabel.Text <- abbrev

      if keyLbl <> keybindText then
        keybindText <- keyLbl
        keyLabel.Text <- keyLbl

    struct (container, updateSlot)


  let createActionBar
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (actionSets: HashMap<int, HashMap<GameAction, SlotProcessing>> aval)
    (activeSetIndex: int aval)
    (cooldowns: HashMap<int<SkillId>, TimeSpan> aval)
    (inputMap: InputMap aval)
    (skillStore: SkillStore)
    =
    let panel = new HorizontalStackPanel(Spacing = 4)

    // Action set indicator (shows which set is active: 1-8)
    let setIndicator = new Label()
    setIndicator.Text <- "1"
    setIndicator.TextColor <- Color.Yellow
    setIndicator.VerticalAlignment <- VerticalAlignment.Center
    setIndicator.Margin <- Thickness(0, 0, 8, 0)
    panel.Widgets.Add(setIndicator)

    let slots = [|
      for _ in 0 .. slotCount - 1 ->
        let struct (widget, update) = createActionSlot config worldTime
        panel.Widgets.Add(widget)
        struct (widget, update)
    |]

    let updateAllSlots
      (sets: HashMap<int, HashMap<GameAction, SlotProcessing>>)
      (setIdx: int)
      (cds: HashMap<int<SkillId>, TimeSpan>)
      (imap: InputMap)
      =
      match HashMap.tryFindV setIdx sets with
      | ValueSome activeSet ->
        for i in 0 .. slotCount - 1 do
          let action = slotActions.[i]
          let struct (_widget, update) = slots.[i]
          let keyLbl = getKeyLabelForAction imap action

          match HashMap.tryFindV action activeSet with
          | ValueSome(SlotProcessing.Skill skillId) ->
            let abbrev = getSkillAbbreviation skillStore skillId

            let cdEnd =
              HashMap.tryFindV skillId cds
              |> ValueOption.defaultValue TimeSpan.Zero

            update abbrev cdEnd keyLbl
          | ValueSome(SlotProcessing.Item _) ->
            update "ITM" TimeSpan.Zero keyLbl
          | ValueNone -> update "" TimeSpan.Zero keyLbl
      | ValueNone ->
        for i in 0 .. slotCount - 1 do
          let struct (_widget, update) = slots.[i]
          update "" TimeSpan.Zero "?"

    let combinedData =
      let sets_idx =
        (actionSets, activeSetIndex) ||> AVal.map2(fun s i -> struct (s, i))

      let cds_imap =
        (cooldowns, inputMap) ||> AVal.map2(fun c im -> struct (c, im))

      (sets_idx, cds_imap)
      ||> AVal.map2(fun (struct (s, i)) (struct (c, im)) ->
        struct (s, i, c, im))

    let _subscription =
      combinedData.AddWeakCallback(fun (struct (sets, idx, cds, imap)) ->
        setIndicator.Text <- $"{idx}"
        updateAllSlots sets idx cds imap)

    panel.Tag <- _subscription :> obj
    panel

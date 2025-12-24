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
open Pomo.Core.Stores
open Pomo.Core.UI
open Microsoft.Xna.Framework.Input

// Alias to avoid conflict with Skill.GroundAreaKind.Rectangle
type Rect = Microsoft.Xna.Framework.Rectangle

module HUDComponents =
  open Pomo.Core.UI.HUDAnimation

  [<Struct>]
  type ResourceColorType =
    | Health
    | Mana

  let createPlayerVitals
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (resources: Resource aval)
    (derivedStats: DerivedStats aval)
    =
    // Create HP avals
    let hpCurrent = resources |> AVal.map(_.HP >> float32)
    let hpMax = derivedStats |> AVal.map(_.HP >> float32)
    let hpColorFill = config |> AVal.map _.Theme.Colors.HealthFill
    let hpColorBg = config |> AVal.map _.Theme.Colors.HealthBackground

    // Create MP avals
    let mpCurrent = resources |> AVal.map(_.MP >> float32)
    let mpMax = derivedStats |> AVal.map(_.MP >> float32)
    let mpColorFill = config |> AVal.map _.Theme.Colors.ManaFill
    let mpColorBg = config |> AVal.map _.Theme.Colors.ManaBackground

    let hpText =
      (resources, derivedStats)
      ||> AVal.map2(fun res derived -> $"{int res.HP} / {int derived.HP}")

    let hpWidget =
      ResourceBar.health()
      |> W.size 200 16
      |> W.hAlign HorizontalAlignment.Left
      |> W.bindCurrentValue hpCurrent
      |> W.bindMaxValue hpMax
      |> W.bindColorFill hpColorFill
      |> W.bindColorBackground hpColorBg
      |> W.bindWorldTime worldTime

    let mpWidget =
      ResourceBar.mana()
      |> W.size 200 8
      |> W.hAlign HorizontalAlignment.Left
      |> W.margin4 0 4 0 0
      |> W.bindCurrentValue mpCurrent
      |> W.bindMaxValue mpMax
      |> W.bindColorFill mpColorFill
      |> W.bindColorBackground mpColorBg
      |> W.bindWorldTime worldTime

    let hpLabel =
      Label.create ""
      |> W.textColor Color.White
      |> W.hAlign HorizontalAlignment.Center
      |> W.vAlign VerticalAlignment.Center
      |> W.margin4 0 -20 0 0
      |> W.bindText hpText

    VStack.create() |> W.childrenV [ hpWidget; mpWidget; hpLabel ]


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
    let bgColor = config |> AVal.map(fun c -> c.Theme.Colors.HealthBackground) // Using HealthBackground as placeholder if no slot bg
    let cdColor = config |> AVal.map(fun c -> c.Theme.CooldownOverlayColor)

    let abbrevLabel =
      Label.create ""
      |> W.textColor Color.White
      |> W.hAlign HorizontalAlignment.Center
      |> W.vAlign VerticalAlignment.Center

    let keyLabel =
      Label.create ""
      |> W.textColor Color.LightGray
      |> W.hAlign HorizontalAlignment.Right
      |> W.vAlign VerticalAlignment.Top
      |> W.margin4 0 2 4 0

    let slot =
      ActionSlot.create()
      |> W.size 40 40
      |> W.bindWorldTime worldTime
      |> W.bindBgColor bgColor
      |> W.bindCooldownColor cdColor

    let container =
      Panel.sized 40 40 |> W.childrenP [ slot; abbrevLabel; keyLabel ]

    struct (container, slot, abbrevLabel, keyLabel)


  let createActionBar
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (actionSets: HashMap<int, HashMap<GameAction, SlotProcessing>> aval)
    (activeSetIndex: int aval)
    (cooldowns: HashMap<int<SkillId>, TimeSpan> aval)
    (inputMap: InputMap aval)
    (skillStore: SkillStore)
    =

    let setIndicator =
      let idxText = activeSetIndex |> AVal.map(fun i -> $"{i}")

      Label.create "1"
      |> W.textColor Color.Yellow
      |> W.vAlign VerticalAlignment.Center
      |> W.margin4 0 0 8 0
      |> W.bindText idxText

    let panel = HStack.spaced 4 |> W.childrenH [ setIndicator ]

    for i in 0 .. slotCount - 1 do
      let action = slotActions.[i]

      let struct (container, slot, abbrev, key) =
        createActionSlot config worldTime

      panel.Widgets.Add(container)

      // 1. Bind Keybind Label
      let keyText =
        inputMap |> AVal.map(fun im -> getKeyLabelForAction im action)

      key |> W.bindText keyText |> ignore

      // 2. Current Slot Processing State
      let slotProc =
        (actionSets, activeSetIndex)
        ||> AVal.map2(fun sets idx ->
          HashMap.tryFindV idx sets
          |> ValueOption.bind(HashMap.tryFindV action))

      // 3. Bind Abbreviation
      let abbrevText =
        slotProc
        |> AVal.map (function
          | ValueSome(SlotProcessing.Skill id) ->
            getSkillAbbreviation skillStore id
          | ValueSome(SlotProcessing.Item _) -> "ITM"
          | ValueNone -> "")

      abbrev |> W.bindText abbrevText |> ignore

      // 4. Bind Cooldown
      let cdEnd =
        (slotProc, cooldowns)
        ||> AVal.map2(fun proc cds ->
          match proc with
          | ValueSome(SlotProcessing.Skill id) ->
            HashMap.tryFindV id cds |> ValueOption.defaultValue TimeSpan.Zero
          | _ -> TimeSpan.Zero)

      slot |> W.bindCooldownEndTime cdEnd |> ignore

    panel

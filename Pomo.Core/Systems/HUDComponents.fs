namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Myra.Graphics2D.UI
open Myra.Graphics2D
open FSharp.UMX
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
    let hpCurrent = resources |> AVal.map(_.HP >> float32)
    let hpMax = derivedStats |> AVal.map(_.HP >> float32)
    let hpColorFill = config |> AVal.map _.Theme.Colors.HealthFill
    let hpColorBg = config |> AVal.map _.Theme.Colors.HealthBackground

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
    inputMap
    |> HashMap.fold
      (fun acc rawInput mappedAction ->
        match acc with
        | Some _ -> acc
        | None ->
          if mappedAction = action then
            match rawInput with
            | Key k ->
              let name = k.ToString()

              if name.StartsWith("D") && name.Length = 2 then
                Some(name.Substring(1))
              else
                Some name
            | _ -> None
          else
            None)
      None
    |> Option.defaultValue "?"


  let createActionSlot (config: HUDConfig aval) (worldTime: Time aval) =
    let bgColor = config |> AVal.map(fun c -> c.Theme.Colors.HealthBackground)
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

      let keyText =
        inputMap |> AVal.map(fun im -> getKeyLabelForAction im action)

      key |> W.bindText keyText |> ignore

      let slotProc =
        (actionSets, activeSetIndex)
        ||> AVal.map2(fun sets idx ->
          HashMap.tryFindV idx sets
          |> ValueOption.bind(HashMap.tryFindV action))

      let abbrevText =
        slotProc
        |> AVal.map (function
          | ValueSome(SlotProcessing.Skill id) ->
            getSkillAbbreviation skillStore id
          | ValueSome(SlotProcessing.Item _) -> "ITM"
          | ValueNone -> "")

      abbrev |> W.bindText abbrevText |> ignore

      let cdEnd =
        (slotProc, cooldowns)
        ||> AVal.map2(fun proc cds ->
          match proc with
          | ValueSome(SlotProcessing.Skill id) ->
            HashMap.tryFindV id cds |> ValueOption.defaultValue TimeSpan.Zero
          | _ -> TimeSpan.Zero)

      slot |> W.bindCooldownEndTime cdEnd |> ignore

    panel


  let createStatusEffect
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (effect: ActiveEffect)
    =
    let theme = config |> AVal.map _.Theme
    let colors = theme |> AVal.map _.Colors

    let colorBuff = colors |> AVal.map _.BuffBorder
    let colorDebuff = colors |> AVal.map _.DebuffBorder
    let colorDot = colors |> AVal.map _.DotBorder
    let cdColor = theme |> AVal.map _.CooldownOverlayColor

    let abbrev =
      let name = effect.SourceEffect.Name

      if name.Length <= 2 then
        name
      else
        name.Substring(0, 2).ToUpper()

    let abbrevLabel =
      Label.create abbrev
      |> W.textColor Color.White
      |> W.hAlign HorizontalAlignment.Center
      |> W.vAlign VerticalAlignment.Center

    let stackLabel =
      let text = if effect.StackCount > 1 then $"{effect.StackCount}" else ""

      Label.create text
      |> W.textColor Color.Yellow
      |> W.hAlign HorizontalAlignment.Right
      |> W.vAlign VerticalAlignment.Bottom
      |> W.margin4 0 0 2 0

    let totalDuration =
      match effect.SourceEffect.Duration with
      | Timed t -> float32 t.TotalSeconds
      | Loop(i, _) -> float32 i.TotalSeconds
      | PermanentLoop i -> float32 i.TotalSeconds
      | _ -> 0.0f

    let endTime =
      match effect.SourceEffect.Duration with
      | Timed t -> effect.StartTime + t
      | _ -> TimeSpan.Zero

    let widget =
      StatusEffect.create()
      |> W.size 32 32
      |> W.bindWorldTime worldTime
      |> W.cooldownEndTime endTime
      |> W.totalDurationSeconds totalDuration
      |> W.effectKind effect.SourceEffect.Kind
      |> W.bindColorBuff colorBuff
      |> W.bindColorDebuff colorDebuff
      |> W.bindColorDot colorDot
      |> W.bindCooldownColor cdColor

    Panel.sized 32 32 |> W.childrenP [ widget; abbrevLabel; stackLabel ]

  let createStatusEffectsBar
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (activeEffects: IndexList<ActiveEffect> aval)
    =
    let container = HStack.spaced 4

    let effectsWidgets =
      activeEffects
      |> AVal.map(fun effects ->
        effects
        |> IndexList.sortBy(fun e ->
          let kindScore =
            match e.SourceEffect.Kind with
            | Buff -> 0
            | DamageOverTime -> 1
            | Debuff
            | Stun
            | Silence
            | Taunt -> 2
            | _ -> 3

          struct (kindScore, e.StartTime))
        |> IndexList.map(createStatusEffect config worldTime))

    container |> HStack.bindIndexListChildren effectsWidgets

  let createTargetFrame
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (selectedEntityId: Guid<EntityId> voption aval)
    (allResources: amap<Guid<EntityId>, Resource>)
    (allDerivedStats: amap<Guid<EntityId>, DerivedStats>)
    (allFactions: amap<Guid<EntityId>, HashSet<Faction>>)
    =
    let configVal = config |> AVal.force
    let colors = configVal.Theme.Colors

    let targetResources =
      selectedEntityId
      |> AVal.bind(fun idOpt ->
        match idOpt with
        | ValueSome id -> allResources |> AMap.tryFind id
        | ValueNone -> AVal.constant None)

    let targetDerived =
      selectedEntityId
      |> AVal.bind(fun idOpt ->
        match idOpt with
        | ValueSome id -> allDerivedStats |> AMap.tryFind id
        | ValueNone -> AVal.constant None)

    let targetFaction =
      selectedEntityId
      |> AVal.bind(fun idOpt ->
        match idOpt with
        | ValueSome id -> allFactions |> AMap.tryFind id
        | ValueNone -> AVal.constant None)

    let visibility = selectedEntityId |> AVal.map ValueOption.isSome

    let hpCurrent =
      targetResources
      |> AVal.map(Option.map(_.HP >> float32) >> Option.defaultValue 0.0f)

    let hpMax =
      targetDerived
      |> AVal.map(Option.map(_.HP >> float32) >> Option.defaultValue 100.0f)

    let hpBar =
      ResourceBar.health()
      |> W.size 150 12
      |> W.bindCurrentValue hpCurrent
      |> W.bindMaxValue hpMax
      |> W.bindWorldTime worldTime
      |> W.colorBackground colors.HealthBackground
      |> W.colorFill colors.HealthFill

    let nameText =
      targetFaction
      |> AVal.map(fun fOpt ->
        match fOpt with
        | Some f ->
          if HashSet.contains Player f then "Player"
          elif HashSet.contains Enemy f then "Enemy"
          else "Target"
        | None -> "Target")

    let nameLabel =
      Label.create ""
      |> W.textColor Color.White
      |> W.hAlign HorizontalAlignment.Left
      |> W.bindText nameText

    let frame =
      VStack.spaced 2
      |> W.childrenV [ nameLabel; hpBar ]
      |> W.padding 8
      |> W.bindOpacity(
        visibility |> AVal.map(fun v -> if v then 1.0f else 0.0f)
      )

    frame

  let createCastBar
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (activeCharges: amap<Guid<EntityId>, ActiveCharge>)
    (playerId: Guid<EntityId>)
    (skillStore: SkillStore)
    =
    let configVal = config |> AVal.force
    let colors = configVal.Theme.Colors

    let playerCharge = activeCharges |> AMap.tryFind playerId

    let visibility = playerCharge |> AVal.map Option.isSome

    let progress =
      (playerCharge, worldTime)
      ||> AVal.map2(fun chargeOpt time ->
        match chargeOpt with
        | Some c ->
          let elapsed = (time.TotalGameTime - c.StartTime).TotalSeconds
          let duration = c.Duration.TotalSeconds
          if duration > 0.0 then float32(elapsed / duration) else 1.0f
        | None -> 0.0f)

    let skillName =
      playerCharge
      |> AVal.map(fun chargeOpt ->
        match chargeOpt with
        | Some c ->
          match skillStore.tryFind c.SkillId with
          | ValueSome(Skill.Active a) -> a.Name
          | ValueSome(Skill.Passive p) -> p.Name
          | ValueNone -> "Casting..."
        | None -> "")

    let bar =
      ResourceBar.create()
      |> W.size 250 14
      |> W.colorFill colors.ManaFill
      |> W.colorBackground colors.ManaBackground
      |> W.bindCurrentValue progress
      |> W.maxValue 1.0f
      |> W.bindWorldTime worldTime
      |> W.smoothSpeed 0.05f

    let label =
      Label.create ""
      |> W.textColor Color.White
      |> W.hAlign HorizontalAlignment.Center
      |> W.bindText skillName

    let container =
      VStack.spaced 4
      |> W.childrenV [ label; bar ]
      |> W.bindOpacity(
        visibility |> AVal.map(fun v -> if v then 1.0f else 0.0f)
      )

    container

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
open Pomo.Core.Projections
open Microsoft.Xna.Framework.Input

type Rect = Microsoft.Xna.Framework.Rectangle

module HUDComponents =
  open Pomo.Core.UI.HUDAnimation

  /// Common functions shared across multiple components
  module Common =
    /// Extract 2-letter abbreviation from a name
    let extractAbbreviation(name: string) =
      if String.IsNullOrWhiteSpace(name) then
        "??"
      elif name.Length <= 2 then
        name.ToUpper()
      elif name.Contains(" ") then
        let words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)

        match words with
        | [| w1; w2; _ |] when w1.Length > 0 && w2.Length > 0 ->
          $"{w1[0]}{w2[0]}".ToUpper()
        | [| w1; w2 |] when w1.Length > 0 && w2.Length > 0 ->
          $"{w1[0]}{w2[0]}".ToUpper()
        | _ -> name.Substring(0, min 2 name.Length).ToUpper()
      else
        name.Substring(0, 2).ToUpper()

    /// Convert boolean visibility to opacity (1.0 or 0.0)
    let inline visibilityToOpacity(visible: bool) =
      if visible then 1.0f else 0.0f

    /// Format resource text as "current / max"
    let inline formatResourceText (current: int) (max: int) =
      $"{current} / {max}"

  /// Functions for ActionBar component
  module ActionBar =
    /// Get skill abbreviation from SkillStore
    let getSkillAbbreviation (skillStore: SkillStore) (skillId: int<SkillId>) =
      match skillStore.tryFind skillId with
      | ValueSome(Skill.Active active) -> Common.extractAbbreviation active.Name
      | ValueSome(Skill.Passive passive) ->
        Common.extractAbbreviation passive.Name
      | ValueNone -> "??"

    /// Get skill intent from SkillStore (if active skill)
    let getSkillIntent (skillStore: SkillStore) (skillId: int<SkillId>) =
      match skillStore.tryFind skillId with
      | ValueSome(Skill.Active active) -> ValueSome active.Intent
      | _ -> ValueNone

    /// Get keyboard label for a game action from input map
    let getKeyLabelForAction (action: GameAction) (inputMap: InputMap) =
      let formatKeyName(k: Keys) =
        let name = k.ToString()

        if name.StartsWith("D") && name.Length = 2 then
          name.Substring(1)
        else
          name

      inputMap
      |> Seq.tryPick(fun (rawInput, mappedAction) ->
        if mappedAction = action then
          match rawInput with
          | Key k -> Some(formatKeyName k)
          | _ -> None
        else
          None)
      |> Option.defaultValue "?"

    /// Get cooldown end time for a slot processing entry
    let inline getSlotCooldownEndTime
      (cds: HashMap<int<SkillId>, TimeSpan>)
      (proc: SlotProcessing voption)
      =
      match proc with
      | ValueSome(SlotProcessing.Skill id) ->
        HashMap.tryFindV id cds |> ValueOption.defaultValue TimeSpan.Zero
      | _ -> TimeSpan.Zero

    /// Get abbreviation text for a slot processing entry
    let getSlotAbbreviation
      (skillStore: SkillStore)
      (inventory: HashMap<int<ItemId>, ResolvedItemStack> option)
      (proc: SlotProcessing voption)
      =
      match proc with
      | ValueSome(SlotProcessing.Skill id) -> getSkillAbbreviation skillStore id
      | ValueSome(SlotProcessing.Item instanceId) ->
        inventory
        |> Option.bind(fun inv ->
          inv
          |> Seq.tryPick(fun (_, stack) ->
            if
              stack.Instances
              |> List.exists(fun inst -> inst.InstanceId = instanceId)
            then
              Some(Common.extractAbbreviation stack.Definition.Name)
            else
              None))
        |> Option.defaultValue "ITM"
      | ValueNone -> ""

    /// Get item uses left for a slot (returns empty string for skills, uses for items)
    let getSlotItemCount
      (inventory: HashMap<int<ItemId>, ResolvedItemStack> option)
      (proc: SlotProcessing voption)
      =
      match proc with
      | ValueSome(SlotProcessing.Skill _) -> ""
      | ValueSome(SlotProcessing.Item instanceId) ->
        inventory
        |> Option.bind(fun inv ->
          inv
          |> Seq.tryPick(fun (_, stack) ->
            stack.Instances
            |> List.tryFind(fun inst -> inst.InstanceId = instanceId)
            |> Option.bind(fun inst ->
              inst.UsesLeft |> ValueOption.toOption |> Option.map string)))
        |> Option.defaultValue ""
      | ValueNone -> ""

    /// Get background color for a slot based on skill intent
    let getSlotBackgroundColor
      (skillStore: SkillStore)
      (colors: HUDColorPalette)
      (proc: SlotProcessing voption)
      =
      match proc with
      | ValueSome(SlotProcessing.Skill id) ->
        match getSkillIntent skillStore id with
        | ValueSome SkillIntent.Offensive -> colors.OffensiveSlot
        | ValueSome SkillIntent.Supportive -> colors.SupportiveSlot
        | ValueNone -> colors.HealthBackground
      | _ -> colors.HealthBackground

  /// Functions for StatusEffect component
  module StatusEffect =
    /// Get total duration in seconds from an effect Duration
    let inline getEffectTotalDuration
      (duration: Pomo.Core.Domain.Skill.Duration)
      =
      match duration with
      | Timed t -> float32 t.TotalSeconds
      | Loop(i, _) -> float32 i.TotalSeconds
      | PermanentLoop i -> float32 i.TotalSeconds
      | _ -> 0.0f

    /// Get end time for an effect (only for Timed effects)
    let inline getEffectEndTime
      (startTime: TimeSpan)
      (duration: Pomo.Core.Domain.Skill.Duration)
      =
      match duration with
      | Timed t -> startTime + t
      | _ -> TimeSpan.Zero

    /// Get sorting score for effect kind (buffs first, then DoTs, then debuffs)
    let inline getEffectKindSortScore(kind: EffectKind) =
      match kind with
      | Buff -> 0
      | DamageOverTime -> 1
      | Debuff
      | Stun
      | Silence
      | Taunt -> 2
      | _ -> 3

  /// Functions for CastBar component
  module CastBar =
    /// Get skill name from SkillStore
    let getSkillName (skillStore: SkillStore) (skillId: int<SkillId>) =
      skillStore.tryFind skillId
      |> ValueOption.map(fun skill ->
        match skill with
        | Skill.Active a -> a.Name
        | Skill.Passive p -> p.Name)
      |> ValueOption.defaultValue "Casting..."

    /// Calculate cast bar progress from charge and time
    let inline getCastProgress (chargeOpt: ActiveCharge option) (time: Time) =
      match chargeOpt with
      | Some c ->
        let elapsed = (time.TotalGameTime - c.StartTime).TotalSeconds
        let duration = c.Duration.TotalSeconds
        if duration > 0.0 then float32(elapsed / duration) else 1.0f
      | None -> 0.0f

    /// Get skill name from an active charge
    let getChargeSkillName
      (skillStore: SkillStore)
      (chargeOpt: ActiveCharge option)
      =
      chargeOpt
      |> Option.map(fun c -> getSkillName skillStore c.SkillId)
      |> Option.defaultValue ""

  /// Functions for TargetFrame component
  module TargetFrame =
    /// Get display name for a faction set
    let getFactionDisplayName(factions: HashSet<Faction> option) =
      match factions with
      | Some f ->
        if HashSet.contains Player f then "Player"
        elif HashSet.contains Enemy f then "Enemy"
        else "Target"
      | None -> "Target"


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
      ||> AVal.map2(fun res derived ->
        Common.formatResourceText res.HP derived.HP)

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

  // Action slot constants
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


  let createActionSlot
    (worldTime: Time aval)
    (cdColor: Color aval)
    (bgColor: Color aval)
    =
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

    let countLabel =
      Label.create ""
      |> W.textColor Color.Yellow
      |> W.hAlign HorizontalAlignment.Right
      |> W.vAlign VerticalAlignment.Bottom
      |> W.margin4 0 0 4 2

    let slot =
      ActionSlot.create()
      |> W.size 48 48
      |> W.bindWorldTime worldTime
      |> W.bindBgColor bgColor
      |> W.bindCooldownColor cdColor

    let container =
      Panel.sized 48 48
      |> W.childrenP [ slot; abbrevLabel; keyLabel; countLabel ]

    struct (container, slot, abbrevLabel, keyLabel, countLabel)


  let createActionBar
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (actionSets: HashMap<int, HashMap<GameAction, SlotProcessing>> aval)
    (activeSetIndex: int aval)
    (cooldowns: HashMap<int<SkillId>, TimeSpan> aval)
    (inputMap: InputMap aval)
    (playerInventory: HashMap<int<ItemId>, ResolvedItemStack> option aval)
    (skillStore: SkillStore)
    =
    let colors = config |> AVal.map _.Theme.Colors
    let cdColor = config |> AVal.map _.Theme.CooldownOverlayColor

    let setIndicator =
      let idxText = activeSetIndex |> AVal.map string

      Label.create "1"
      |> W.textColor Color.Yellow
      |> W.vAlign VerticalAlignment.Center
      |> W.margin4 0 0 8 0
      |> W.bindText idxText

    let panel = HStack.spaced 4 |> W.childrenH [ setIndicator ]

    for i in 0 .. slotCount - 1 do
      let action = slotActions[i]

      let slotProc =
        (actionSets, activeSetIndex)
        ||> AVal.map2(fun sets idx ->
          HashMap.tryFindV idx sets
          |> ValueOption.bind(HashMap.tryFindV action))

      let bgColor =
        (slotProc, colors)
        ||> AVal.map2(fun proc cs ->
          ActionBar.getSlotBackgroundColor skillStore cs proc)

      let struct (container, slot, abbrev, key, count) =
        createActionSlot worldTime cdColor bgColor

      panel.Widgets.Add(container)

      let keyText = inputMap |> AVal.map(ActionBar.getKeyLabelForAction action)

      key |> W.bindText keyText |> ignore

      let abbrevText =
        (slotProc, playerInventory)
        ||> AVal.map2(fun proc inv ->
          ActionBar.getSlotAbbreviation skillStore inv proc)

      abbrev |> W.bindText abbrevText |> ignore

      let countText =
        (slotProc, playerInventory)
        ||> AVal.map2(fun proc inv -> ActionBar.getSlotItemCount inv proc)

      count |> W.bindText countText |> ignore

      let cdEnd =
        (cooldowns, slotProc) ||> AVal.map2 ActionBar.getSlotCooldownEndTime

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

    let abbrev = Common.extractAbbreviation effect.SourceEffect.Name

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
      StatusEffect.getEffectTotalDuration effect.SourceEffect.Duration

    let endTime =
      StatusEffect.getEffectEndTime
        effect.StartTime
        effect.SourceEffect.Duration

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
          struct (StatusEffect.getEffectKindSortScore e.SourceEffect.Kind,
                  e.StartTime))
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
    let colors = config |> AVal.map _.Theme.Colors
    let hpFill = colors |> AVal.map _.HealthFill
    let hpBg = colors |> AVal.map _.HealthBackground

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
      |> W.bindColorBackground hpBg
      |> W.bindColorFill hpFill

    let nameText = targetFaction |> AVal.map TargetFrame.getFactionDisplayName

    let nameLabel =
      Label.create ""
      |> W.textColor Color.White
      |> W.hAlign HorizontalAlignment.Left
      |> W.bindText nameText

    let frame =
      VStack.spaced 2
      |> W.childrenV [ nameLabel; hpBar ]
      |> W.padding 8
      |> W.bindOpacity(visibility |> AVal.map Common.visibilityToOpacity)

    frame

  let createCastBar
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (activeCharges: amap<Guid<EntityId>, ActiveCharge>)
    (playerId: Guid<EntityId>)
    (skillStore: SkillStore)
    =
    let colors = config |> AVal.map _.Theme.Colors
    let castFill = colors |> AVal.map _.CastBarFill
    let castBg = colors |> AVal.map _.CastBarBackground

    let playerCharge = activeCharges |> AMap.tryFind playerId

    let visibility = playerCharge |> AVal.map Option.isSome

    let progress =
      (playerCharge, worldTime) ||> AVal.map2 CastBar.getCastProgress

    let skillName =
      playerCharge |> AVal.map(CastBar.getChargeSkillName skillStore)

    let bar =
      ResourceBar.create()
      |> W.size 250 14
      |> W.bindColorFill castFill
      |> W.bindColorBackground castBg
      |> W.bindCurrentValue progress
      |> W.maxValue 1.0f
      |> W.bindWorldTime worldTime
      |> W.smoothSpeed 0.05f
      |> W.pulseMode NoPulse

    let label =
      Label.create ""
      |> W.textColor Color.White
      |> W.hAlign HorizontalAlignment.Center
      |> W.bindText skillName

    let container =
      VStack.spaced 4
      |> W.childrenV [ label; bar ]
      |> W.bindOpacity(visibility |> AVal.map Common.visibilityToOpacity)

    container

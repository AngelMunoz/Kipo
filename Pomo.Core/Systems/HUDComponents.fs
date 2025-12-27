namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Myra.Graphics2D.UI
open Myra.Graphics2D
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core
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

/// Shared context for all action slots in a bar
[<Struct>]
type ActionBarContext = {
  WorldTime: Time aval
  CooldownColor: Color aval
}

/// Per-slot reactive data for an action slot
[<Struct>]
type ActionSlotData = {
  BackgroundColor: Color aval
  TooltipText: string aval
  KeyText: string aval
  AbbrevText: string aval
  CountText: string aval
  CooldownEnd: TimeSpan aval
  CooldownDuration: float32 aval
}

module HUDComponents =
  open Pomo.Core.UI.HUDAnimation
  open System.Collections.Generic
  open Pomo.Core.Domain.Item

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

  /// Tooltip text generators (formatted strings)
  module Tooltips =
    /// Format a skill as tooltip text
    let formatSkill (skillStore: SkillStore) (skillId: int<SkillId>) =
      skillStore.tryFind skillId
      |> ValueOption.map(fun skill ->
        match skill with
        | Active a ->
          let cost =
            a.Cost
            |> ValueOption.map(fun c ->
              $"{c.Amount |> ValueOption.defaultValue 0} {c.ResourceType}")
            |> ValueOption.defaultValue "Free"

          let cd =
            a.Cooldown
            |> ValueOption.map(fun t -> $"{t.TotalSeconds:F1}s")
            |> ValueOption.defaultValue "Instant"

          $"{a.Name}\n{a.Description}\nCost: {cost}\nCooldown: {cd}"
        | Passive p -> $"{p.Name} (Passive)\n{p.Description}")
      |> ValueOption.defaultValue ""

    /// Format an item as tooltip text
    let formatItem(stack: ResolvedItemStack) =
      let def = stack.Definition

      let kindText =
        match def.Kind with
        | Wearable w -> $"[{w.Slot}]"
        | Usable _ -> "[Usable]"
        | NonUsable -> ""

      let usesText =
        stack.Instances
        |> List.tryHead
        |> Option.bind(fun i -> i.GetUsesLeft() |> ValueOption.toOption)
        |> Option.map(fun u -> $"\nUses: {u}")
        |> Option.defaultValue ""

      $"{def.Name} {kindText}\nWeight: {def.Weight}{usesText}"

    /// Format an effect as tooltip text
    let formatEffect (effect: ActiveEffect) (currentTime: TimeSpan) =
      let remaining =
        match effect.SourceEffect.Duration with
        | Timed t -> effect.StartTime + t - currentTime
        | _ -> TimeSpan.Zero

      let timeText =
        if remaining.TotalSeconds > 0.0 then
          $" ({int remaining.TotalSeconds}s)"
        else
          ""

      let stackText =
        if effect.StackCount > 1 then
          $"\nStacks: {effect.StackCount}"
        else
          ""

      $"{effect.SourceEffect.Name}{timeText}\n[{effect.SourceEffect.Kind}]{stackText}"

    /// Get tooltip text for a slot processing entry
    let formatSlot
      (skillStore: SkillStore)
      (proc: SlotProcessing voption)
      (inventory: HashMap<int<ItemId>, ResolvedItemStack> option)
      =
      match proc with
      | ValueSome(SlotProcessing.Skill id) -> formatSkill skillStore id
      | ValueSome(SlotProcessing.Item instanceId) ->
        inventory
        |> Option.bind(fun inv ->
          inv
          |> Seq.tryPick(fun (_, stack) ->
            if
              stack.Instances
              |> List.exists(fun i -> i.InstanceId = instanceId)
            then
              Some(formatItem stack)
            else
              None))
        |> Option.defaultValue ""
      | ValueNone -> ""

    let formatEquippedItem
      (itemStore: ItemStore)
      (itemInstances: IReadOnlyDictionary<Guid<ItemInstanceId>, ItemInstance>)
      (equippedItems: HashMap<Slot, Guid<ItemInstanceId>>)
      (slot: Slot)
      =
      equippedItems.TryFindV slot
      |> ValueOption.bind(fun id -> itemInstances |> Dictionary.tryFindV id)
      |> ValueOption.bind(fun inst ->
        itemStore.tryFind inst.ItemId
        |> ValueOption.map(fun def ->
          let kindText =
            match def.Kind with
            | Wearable w ->
              let stats =
                w.Stats |> Array.map(fun s -> $"  {s}") |> String.concat "\n"

              if stats.Length > 0 then
                $"[{w.Slot}]\n{stats}"
              else
                $"[{w.Slot}]"
            | Usable u -> $"[Usable] {u.Effect.Name}"
            | NonUsable -> ""

          $"{def.Name}\n{kindText}\nWeight: {def.Weight}"))
      |> ValueOption.defaultValue ""

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

    /// Get cooldown duration (in seconds) for a slot processing entry
    let getSlotCooldownDuration
      (skillStore: SkillStore)
      (proc: SlotProcessing voption)
      =
      match proc with
      | ValueSome(SlotProcessing.Skill id) ->
        skillStore.tryFind id
        |> ValueOption.bind(fun skill ->
          match skill with
          | Active a -> a.Cooldown
          | _ -> ValueNone)
        |> ValueOption.map(fun ts -> float32 ts.TotalSeconds)
        |> ValueOption.defaultValue 10.0f // Fallback for skills without cooldown
      | _ -> 10.0f

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
              inst.GetUsesLeft() |> ValueOption.toOption |> Option.map string)))
        |> Option.defaultValue ""
      | ValueNone -> ""

    /// Get GetUsesLeft thunk for a slot (returns dummy thunk for skills)
    let getSlotUsesLeftThunk
      (inventory: HashMap<int<ItemId>, ResolvedItemStack> option)
      (proc: SlotProcessing voption)
      : unit -> int voption =
      match proc with
      | ValueSome(SlotProcessing.Item instanceId) ->
        inventory
        |> Option.bind(fun inv ->
          inv
          |> Seq.tryPick(fun (_, stack) ->
            stack.Instances
            |> List.tryFind(fun inst -> inst.InstanceId = instanceId)
            |> Option.map _.GetUsesLeft))
        |> Option.defaultValue(fun () -> ValueNone)
      | _ -> (fun () -> ValueNone)

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
    let getFactionDisplayName
      (factions: FSharp.Data.Adaptive.HashSet<Faction> option)
      =
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
      ResourceBar.create()
      |> W.height 20
      |> W.hAlign HorizontalAlignment.Stretch
      |> W.bindCurrentValue hpCurrent
      |> W.bindMaxValue hpMax
      |> W.bindColorFill hpColorFill
      |> W.bindColorBackground hpColorBg
      |> W.bindWorldTime worldTime

    let mpWidget =
      ResourceBar.create()
      |> W.height 10
      |> W.hAlign HorizontalAlignment.Stretch
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
      |> W.bindText hpText

    // HP bar with overlayed label
    let hpPanel =
      Panel.create()
      |> W.height 20
      |> W.hAlign HorizontalAlignment.Stretch
      |> W.childrenP [ hpWidget; hpLabel ]

    // Container with fixed width for positioning, bars stretch inside
    VStack.spaced 4 |> W.width 600 |> W.childrenV [ hpPanel; mpWidget ]

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


  let createActionSlot (ctx: ActionBarContext) (data: ActionSlotData) =
    let abbrevLabel =
      Label.create ""
      |> W.textColor Color.White
      |> W.hAlign HorizontalAlignment.Center
      |> W.vAlign VerticalAlignment.Center
      |> W.bindText data.AbbrevText

    let keyLabel =
      Label.create ""
      |> W.textColor Color.LightGray
      |> W.hAlign HorizontalAlignment.Right
      |> W.vAlign VerticalAlignment.Top
      |> W.margin4 0 2 4 0
      |> W.bindText data.KeyText

    let countLabel =
      Label.create ""
      |> W.textColor Color.Yellow
      |> W.hAlign HorizontalAlignment.Right
      |> W.vAlign VerticalAlignment.Bottom
      |> W.margin4 0 0 4 2
      |> W.bindText data.CountText

    let slot =
      ActionSlot.create()
      |> W.size 48 48
      |> W.bindWorldTime ctx.WorldTime
      |> W.bindBgColor data.BackgroundColor
      |> W.bindCooldownColor ctx.CooldownColor
      |> W.bindCooldownEndTime data.CooldownEnd
      |> W.bindCooldownDuration data.CooldownDuration

    Panel.sized 48 48
    |> W.childrenP [ slot; abbrevLabel; keyLabel; countLabel ]
    |> W.bindTooltip data.TooltipText


  /// Create an item slot using ItemSlot widget with reactive GetUsesLeft thunk
  let createItemSlot
    (ctx: ActionBarContext)
    (data: ActionSlotData)
    (getUsesLeftAVal: aval<unit -> int voption>)
    =
    let abbrevLabel =
      Label.create ""
      |> W.textColor Color.White
      |> W.hAlign HorizontalAlignment.Center
      |> W.vAlign VerticalAlignment.Center
      |> W.bindText data.AbbrevText

    let keyLabel =
      Label.create ""
      |> W.textColor Color.LightGray
      |> W.hAlign HorizontalAlignment.Right
      |> W.vAlign VerticalAlignment.Top
      |> W.margin4 0 2 4 0
      |> W.bindText data.KeyText

    // CountLabel - no reactive binding; ItemSlot updates it directly in render
    let countLabel =
      Label.create ""
      |> W.textColor Color.Yellow
      |> W.hAlign HorizontalAlignment.Right
      |> W.vAlign VerticalAlignment.Bottom
      |> W.margin4 0 0 4 2

    let slot =
      ItemSlot.create()
      |> W.size 48 48
      |> W.bindWorldTime ctx.WorldTime
      |> W.bindBgColor data.BackgroundColor
      |> W.bindCooldownColor ctx.CooldownColor
      |> W.bindCooldownEndTime data.CooldownEnd
      |> W.bindCooldownDuration data.CooldownDuration
      |> W.bindGetUsesLeft getUsesLeftAVal
      |> W.countLabel countLabel

    Panel.sized 48 48
    |> W.childrenP [ slot; abbrevLabel; keyLabel; countLabel ]
    |> W.bindTooltip data.TooltipText


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

    let barCtx: ActionBarContext = {
      WorldTime = worldTime
      CooldownColor = cdColor
    }

    let setIndicator =
      let idxText = activeSetIndex |> AVal.map string

      Label.create "1"
      |> W.textColor Color.Yellow
      |> W.vAlign VerticalAlignment.Center
      |> W.hAlign HorizontalAlignment.Center
      |> W.bindText idxText

    // Use Grid layout like Equipment panel for consistent tooltip behavior
    let grid = Grid.spaced 4 4 |> Grid.autoColumns(1 + slotCount)

    // Column 0: set indicator
    Grid.SetColumn(setIndicator, 0)
    Grid.SetRow(setIndicator, 0)
    grid.Widgets.Add(setIndicator)

    for i in 0 .. slotCount - 1 do
      let action = slotActions[i]

      let slotProc =
        (actionSets, activeSetIndex)
        ||> AVal.map2(fun sets idx ->
          HashMap.tryFindV idx sets
          |> ValueOption.bind(HashMap.tryFindV action))

      let slotData: ActionSlotData = {
        BackgroundColor =
          (slotProc, colors)
          ||> AVal.map2(fun proc cs ->
            ActionBar.getSlotBackgroundColor skillStore cs proc)
        TooltipText =
          (slotProc, playerInventory)
          ||> AVal.map2(Tooltips.formatSlot skillStore)
        KeyText = inputMap |> AVal.map(ActionBar.getKeyLabelForAction action)
        AbbrevText =
          (slotProc, playerInventory)
          ||> AVal.map2(fun proc inv ->
            ActionBar.getSlotAbbreviation skillStore inv proc)
        CountText =
          (slotProc, playerInventory)
          ||> AVal.map2(fun proc inv -> ActionBar.getSlotItemCount inv proc)
        CooldownEnd =
          (cooldowns, slotProc) ||> AVal.map2 ActionBar.getSlotCooldownEndTime
        CooldownDuration =
          slotProc |> AVal.map(ActionBar.getSlotCooldownDuration skillStore)
      }

      // Reactive thunk that updates when inventory or slot changes
      let usesLeftAVal =
        (playerInventory, slotProc) ||> AVal.map2 ActionBar.getSlotUsesLeftThunk

      // Always use ItemSlot - it works for both items and skills
      let slotWidget = createItemSlot barCtx slotData usesLeftAVal

      Grid.SetColumn(slotWidget, i + 1) // +1 because column 0 is setIndicator
      Grid.SetRow(slotWidget, 0)
      grid.Widgets.Add(slotWidget)

    // Wrap in Panel like Equipment panel for consistent tooltip behavior
    Panel.create() |> W.childrenP [ grid ]


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

    let tooltipText =
      worldTime
      |> AVal.map(fun time -> Tooltips.formatEffect effect time.TotalGameTime)

    Panel.sized 32 32
    |> W.childrenP [ widget; abbrevLabel; stackLabel ]
    |> W.bindTooltip tooltipText

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
    (allFactions: amap<Guid<EntityId>, FSharp.Data.Adaptive.HashSet<Faction>>)
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
      ResourceBar.create()
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

  /// Functions for CharacterSheet component
  module CharacterSheet =
    /// Format a stat label with its derived source if applicable
    let getStatLabel (label: string) (derivedFrom: string option) =
      match derivedFrom with
      | Some source -> $"{label} ({source})"
      | None -> label

    let createStatRow (label: string) (valueAVal: string aval) =
      let lbl = Label.colored label Color.LightGray |> W.width 72

      let valLbl =
        Label.colored "0" Color.White
        |> W.hAlign HorizontalAlignment.Right
        |> W.width 36
        |> W.bindText valueAVal

      HStack.spaced 12 |> W.height 16 |> W.childrenH [ lbl; valLbl ]

    let createStatSection (title: string) (rows: Widget list) =
      let titleLbl = Label.colored title Color.Yellow |> W.margin4 0 8 0 4

      VStack.create() |> W.childrenV [ titleLbl :> Widget; yield! rows ]

  /// Functions for Equipment component
  module Equipment =
    /// Get item abbreviation for an equipped slot
    let getEquippedItemAbbreviation
      (itemStore: ItemStore)
      (itemInstances: IReadOnlyDictionary<Guid<ItemInstanceId>, ItemInstance>)
      (items: HashMap<Slot, Guid<ItemInstanceId>>)
      (slot: Slot)
      =
      items.TryFindV slot
      |> ValueOption.bind(fun id -> itemInstances |> Dictionary.tryFindV id)
      |> ValueOption.bind(fun inst -> itemStore.tryFind inst.ItemId)
      |> ValueOption.map(fun def -> Common.extractAbbreviation def.Name)
      |> ValueOption.defaultValue ""

    let createSlot
      (worldTime: Time aval)
      (bgColor: Color aval)
      (slotName: string)
      (itemAbbrev: string aval)
      (tooltipText: string aval)
      =
      let slot = EquipmentSlot.create() |> W.size 48 48 |> W.bindBgColor bgColor

      let nameLabel =
        Label.create slotName
        |> W.textColor Color.Gray
        |> W.hAlign HorizontalAlignment.Center
        |> W.vAlign VerticalAlignment.Top
        |> W.margin4 0 2 0 0

      let abbrevLabel =
        Label.create ""
        |> W.textColor Color.White
        |> W.hAlign HorizontalAlignment.Center
        |> W.vAlign VerticalAlignment.Center
        |> W.bindText itemAbbrev

      Panel.sized 48 48
      |> W.childrenP [ slot; nameLabel; abbrevLabel ]
      |> W.bindTooltip tooltipText

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

  let createCombatIndicator
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (inCombatUntil: TimeSpan aval)
    =
    let colors = config |> AVal.map _.Theme.Colors
    let combatColor = colors |> AVal.map _.TextDamage

    let isInCombat =
      (inCombatUntil, worldTime)
      ||> AVal.map2(fun until time -> until > time.TotalGameTime)

    CombatIndicator.create()
    |> W.hAlign HorizontalAlignment.Stretch
    |> W.vAlign VerticalAlignment.Stretch
    |> W.bindIsInCombat isInCombat
    |> W.bindWorldTime worldTime
    |> W.bindColor combatColor

  let createCharacterSheet
    (config: HUDConfig aval)
    (baseStats: BaseStats aval)
    (derivedStats: DerivedStats aval)
    =
    // Base stats row
    let baseSection =
      CharacterSheet.createStatSection "Base Stats" [
        CharacterSheet.createStatRow
          "Power"
          (baseStats |> AVal.map(fun s -> string s.Power))
        CharacterSheet.createStatRow
          "Charm"
          (baseStats |> AVal.map(fun s -> string s.Charm))
        CharacterSheet.createStatRow
          "Magic"
          (baseStats |> AVal.map(fun s -> string s.Magic))
        CharacterSheet.createStatRow
          "Sense"
          (baseStats |> AVal.map(fun s -> string s.Sense))
      ]

    // Power column (left)
    let powerColumn =
      VStack.spaced 2
      |> W.childrenV [
        Label.colored "Power" Color.Yellow :> Widget
        CharacterSheet.createStatRow
          "AP"
          (derivedStats |> AVal.map(fun s -> string s.AP))
        CharacterSheet.createStatRow
          "AC"
          (derivedStats |> AVal.map(fun s -> string s.AC))
        CharacterSheet.createStatRow
          "DX"
          (derivedStats |> AVal.map(fun s -> string s.DX))
      ]

    // Charm column
    let charmColumn =
      VStack.spaced 2
      |> W.childrenV [
        Label.colored "Charm" Color.Yellow :> Widget
        CharacterSheet.createStatRow
          "HP"
          (derivedStats |> AVal.map(fun s -> string s.HP))
        CharacterSheet.createStatRow
          "DP"
          (derivedStats |> AVal.map(fun s -> string s.DP))
        CharacterSheet.createStatRow
          "HV"
          (derivedStats |> AVal.map(fun s -> string s.HV))
      ]

    // Magic column
    let magicColumn =
      VStack.spaced 2
      |> W.childrenV [
        Label.colored "Magic" Color.Yellow :> Widget
        CharacterSheet.createStatRow
          "MP"
          (derivedStats |> AVal.map(fun s -> string s.MP))
        CharacterSheet.createStatRow
          "MA"
          (derivedStats |> AVal.map(fun s -> string s.MA))
        CharacterSheet.createStatRow
          "MD"
          (derivedStats |> AVal.map(fun s -> string s.MD))
      ]

    // Sense column (right)
    let senseColumn =
      VStack.spaced 2
      |> W.childrenV [
        Label.colored "Sense" Color.Yellow :> Widget
        CharacterSheet.createStatRow
          "WT"
          (derivedStats |> AVal.map(fun s -> string s.WT))
        CharacterSheet.createStatRow
          "DA"
          (derivedStats |> AVal.map(fun s -> string s.DA))
        CharacterSheet.createStatRow
          "LK"
          (derivedStats |> AVal.map(fun s -> string s.LK))
      ]

    // 4-column derived stats row
    let derivedGrid =
      HStack.spaced 8
      |> W.childrenH [ powerColumn; charmColumn; magicColumn; senseColumn ]

    // General stats section (MS, Regen)
    let generalSection =
      CharacterSheet.createStatSection "General" [
        CharacterSheet.createStatRow
          "MS"
          (derivedStats |> AVal.map(fun s -> string s.MS))
        CharacterSheet.createStatRow
          "HPReg"
          (derivedStats |> AVal.map(fun s -> string s.HPRegen))
        CharacterSheet.createStatRow
          "MPReg"
          (derivedStats |> AVal.map(fun s -> string s.MPRegen))
      ]

    // Helper for element value display
    let getElementValue (elements: HashMap<Element, float>) (elem: Element) =
      elements.TryFindV elem
      |> ValueOption.map(fun v -> $"{int(v * 100.0)}%%")
      |> ValueOption.defaultValue "-"

    // Elemental Attributes section
    let elemAttrSection =
      CharacterSheet.createStatSection "Elemental Attributes" [
        CharacterSheet.createStatRow
          "Fire"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementAttributes Fire))
        CharacterSheet.createStatRow
          "Water"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementAttributes Water))
        CharacterSheet.createStatRow
          "Earth"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementAttributes Earth))
        CharacterSheet.createStatRow
          "Air"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementAttributes Air))
        CharacterSheet.createStatRow
          "Lightning"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementAttributes Lightning))
        CharacterSheet.createStatRow
          "Light"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementAttributes Light))
        CharacterSheet.createStatRow
          "Dark"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementAttributes Dark))
      ]

    // Elemental Resistances section
    let elemResSection =
      CharacterSheet.createStatSection "Resistances" [
        CharacterSheet.createStatRow
          "Fire"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementResistances Fire))
        CharacterSheet.createStatRow
          "Water"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementResistances Water))
        CharacterSheet.createStatRow
          "Earth"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementResistances Earth))
        CharacterSheet.createStatRow
          "Air"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementResistances Air))
        CharacterSheet.createStatRow
          "Lightning"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementResistances Lightning))
        CharacterSheet.createStatRow
          "Light"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementResistances Light))
        CharacterSheet.createStatRow
          "Dark"
          (derivedStats
           |> AVal.map(fun s -> getElementValue s.ElementResistances Dark))
      ]

    // Elemental sections side by side
    let elemGrid =
      HStack.spaced 16 |> W.childrenH [ elemAttrSection; elemResSection ]

    let container =
      VStack.spaced 8
      |> W.padding 12
      |> W.childrenV [ baseSection; derivedGrid; generalSection; elemGrid ]

    // Background panel with border
    Panel.create()
    |> Panel.bindBackground(config |> AVal.map _.Theme.TooltipBackground)
    |> W.childrenP [ container ]

  let createEquipmentPanel
    (config: HUDConfig aval)
    (worldTime: Time aval)
    (equippedItems: HashMap<Slot, Guid<ItemInstanceId>> aval)
    (itemInstances: IReadOnlyDictionary<Guid<ItemInstanceId>, ItemInstance>)
    (itemStore: ItemStore)
    =
    let colors = config |> AVal.map _.Theme.Colors
    let bgColor = colors |> AVal.map _.HealthBackground

    let createSlot(slot: Slot) =
      let name =
        match slot with
        | Head -> "Head"
        | Chest -> "Chest"
        | Legs -> "Legs"
        | Feet -> "Feet"
        | Hands -> "Hands"
        | Weapon -> "Weapon"
        | Shield -> "Shield"
        | Accessory -> "Acc"

      let abbrev =
        equippedItems
        |> AVal.map(fun items ->
          Equipment.getEquippedItemAbbreviation
            itemStore
            itemInstances
            items
            slot)

      let tooltipText =
        equippedItems
        |> AVal.map(fun items ->
          Tooltips.formatEquippedItem itemStore itemInstances items slot)

      Equipment.createSlot worldTime bgColor name abbrev tooltipText

    let grid = Grid.spaced 4 4 |> W.padding 16 |> Grid.autoColumns 2

    let slots = [ Head; Chest; Legs; Feet; Hands; Weapon; Shield; Accessory ]

    for i in 0 .. slots.Length - 1 do
      let widget = createSlot slots[i]
      Grid.SetColumn(widget, i % 2)
      Grid.SetRow(widget, i / 2)
      grid.Widgets.Add(widget)

    // Background panel
    Panel.create()
    |> Panel.bindBackground(config |> AVal.map _.Theme.TooltipBackground)
    |> W.childrenP [ grid ]


  let createMiniMap
    (config: HUDConfig aval)
    (scenario: Scenario option aval)
    (playerId: Guid<EntityId>)
    (positions: IReadOnlyDictionary<Guid<EntityId>, Vector2>)
    (factions: amap<Guid<EntityId>, FSharp.Data.Adaptive.HashSet<Faction>>)
    (camera: Pomo.Core.Domain.Camera.Camera option aval)
    =
    let mapDef = scenario |> AVal.map(Option.map _.Map)
    let factMap = factions |> AMap.toAVal

    // Compute view bounds from camera for frustum culling
    let viewBounds =
      camera
      |> AVal.map(fun camOpt ->
        match camOpt with
        | Some cam ->
          let bounds =
            Pomo.Core.Graphics.RenderMath.Camera.getViewBounds
              cam.Position
              (float32 cam.Viewport.Width)
              (float32 cam.Viewport.Height)
              cam.Zoom

          ValueSome bounds
        | None -> ValueNone)

    MiniMap.create()
    |> W.size 150 150
    |> W.bindMap mapDef
    |> W.playerId playerId
    |> W.mapPositions positions
    |> W.bindMapFactions factMap
    |> W.bindViewBounds viewBounds

  /// Create a full-screen loading overlay that covers the entire UI
  let createLoadingOverlay(config: HUDConfig aval) =
    let label =
      Label.create "Loading..."
      |> W.textColor Color.White
      |> W.hAlign HorizontalAlignment.Center
      |> W.vAlign VerticalAlignment.Center

    Panel.create()
    |> W.hAlign HorizontalAlignment.Stretch
    |> W.vAlign VerticalAlignment.Stretch
    |> W.bindBackground(config |> AVal.map _.Theme.TooltipBackground)
    |> W.childrenP [ label ]

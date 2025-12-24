namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Entity

module UI =


  [<Struct>]
  type HUDAnchor =
    | TopLeft
    | TopCenter
    | TopRight
    | CenterLeft
    | Center
    | CenterRight
    | BottomLeft
    | BottomCenter
    | BottomRight

  [<Struct>]
  type HUDComponentLayout = {
    Anchor: HUDAnchor
    OffsetX: int
    OffsetY: int
    Visible: bool
  }

  module HUDComponentLayout =
    let defaults = {
      Anchor = BottomCenter
      OffsetX = 0
      OffsetY = 0
      Visible = true
    }


  type HUDColorPalette = {
    HealthFill: Color
    HealthLow: Color
    HealthBackground: Color
    ManaFill: Color
    ManaBackground: Color
    BuffBorder: Color
    DebuffBorder: Color
    DotBorder: Color
    FactionPlayer: Color
    FactionAlly: Color
    FactionEnemy: Color
    FactionNPC: Color
    FactionNeutral: Color
    TeamRed: Color
    TeamBlue: Color
    TeamGreen: Color
    TeamYellow: Color
    TeamOrange: Color
    TeamPurple: Color
    TeamPink: Color
    TeamCyan: Color
    TeamWhite: Color
    TeamBlack: Color
    OffensiveSlot: Color
    SupportiveSlot: Color
    CastBarFill: Color
    CastBarBackground: Color
    TextNormal: Color
    TextDamage: Color
    TextCrit: Color
    TextHeal: Color
    TextStatus: Color
    TextMiss: Color
  }

  module HUDColorPalette =
    let defaults = {
      HealthFill = Color(200, 60, 60)
      HealthLow = Color(255, 80, 80)
      HealthBackground = Color(40, 20, 20)
      ManaFill = Color(60, 80, 180)
      ManaBackground = Color(20, 20, 40)
      BuffBorder = Color(60, 180, 80)
      DebuffBorder = Color(180, 60, 60)
      DotBorder = Color(220, 140, 40)
      FactionPlayer = Color(100, 200, 255)
      FactionAlly = Color(60, 180, 100)
      FactionEnemy = Color(200, 60, 60)
      FactionNPC = Color(200, 180, 100)
      FactionNeutral = Color(140, 140, 140)
      TeamRed = Color(200, 60, 60)
      TeamBlue = Color(60, 100, 200)
      TeamGreen = Color(60, 180, 80)
      TeamYellow = Color(220, 200, 60)
      TeamOrange = Color(220, 140, 40)
      TeamPurple = Color(140, 80, 180)
      TeamPink = Color(220, 120, 160)
      TeamCyan = Color(60, 180, 200)
      TeamWhite = Color(220, 220, 220)
      TeamBlack = Color(60, 60, 60)
      OffensiveSlot = Color(80, 40, 40)
      SupportiveSlot = Color(40, 80, 50)
      CastBarFill = Color(220, 180, 60)
      CastBarBackground = Color(40, 35, 20)
      TextNormal = Color.White
      TextDamage = Color.Red
      TextCrit = Color(255, 200, 50)
      TextHeal = Color.LightGreen
      TextStatus = Color.LightGray
      TextMiss = Color.Gray
    }

    let getColorForFaction (palette: HUDColorPalette) (faction: Faction) =
      match faction with
      | Player -> palette.FactionPlayer
      | Ally -> palette.FactionAlly
      | Enemy -> palette.FactionEnemy
      | NPC -> palette.FactionNPC
      | AIControlled -> palette.FactionNeutral
      | TeamRed -> palette.TeamRed
      | TeamBlue -> palette.TeamBlue
      | TeamGreen -> palette.TeamGreen
      | TeamYellow -> palette.TeamYellow
      | TeamOrange -> palette.TeamOrange
      | TeamPurple -> palette.TeamPurple
      | TeamPink -> palette.TeamPink
      | TeamCyan -> palette.TeamCyan
      | TeamWhite -> palette.TeamWhite
      | TeamBlack -> palette.TeamBlack


  type HUDTheme = {
    Colors: HUDColorPalette
    FontName: string
    CooldownOverlayColor: Color
    TooltipBackground: Color
    TooltipBorder: Color
    BarBorderWidth: int
    BarCornerRadius: int
  }

  module HUDTheme =
    let defaults = {
      Colors = HUDColorPalette.defaults
      FontName = "monogram-extended"
      CooldownOverlayColor = Color(0, 0, 0, 160)
      TooltipBackground = Color(20, 20, 25, 240)
      TooltipBorder = Color(80, 80, 90)
      BarBorderWidth = 2
      BarCornerRadius = 4
    }


  type HUDLayout = {
    PlayerVitals: HUDComponentLayout
    ActionBar: HUDComponentLayout
    StatusEffects: HUDComponentLayout
    TargetFrame: HUDComponentLayout
    CastBar: HUDComponentLayout
    CharacterSheet: HUDComponentLayout
    EquipmentPanel: HUDComponentLayout
    MiniMap: HUDComponentLayout
  }

  module HUDLayout =
    let defaults = {
      PlayerVitals = {
        Anchor = BottomLeft
        OffsetX = 20
        OffsetY = -20
        Visible = true
      }
      ActionBar = {
        Anchor = BottomCenter
        OffsetX = 0
        OffsetY = -20
        Visible = true
      }
      StatusEffects = {
        Anchor = BottomLeft
        OffsetX = 20
        OffsetY = -80
        Visible = true
      }
      TargetFrame = {
        Anchor = TopCenter
        OffsetX = 0
        OffsetY = 20
        Visible = true
      }
      CastBar = {
        Anchor = BottomCenter
        OffsetX = 0
        OffsetY = -100
        Visible = true
      }
      CharacterSheet = {
        Anchor = CenterLeft
        OffsetX = 40
        OffsetY = 0
        Visible = false
      }
      EquipmentPanel = {
        Anchor = CenterRight
        OffsetX = -40
        OffsetY = 0
        Visible = false
      }
      MiniMap = {
        Anchor = TopRight
        OffsetX = -20
        OffsetY = 60
        Visible = true
      }
    }


  type HUDConfig = { Theme: HUDTheme; Layout: HUDLayout }

  module HUDConfig =
    let defaults = {
      Theme = HUDTheme.defaults
      Layout = HUDLayout.defaults
    }

  type IHUDService =
    abstract Config: HUDConfig aval
    abstract GetFactionColor: Faction -> Color
    abstract SetPanelVisible: panelName: string -> visible: bool -> unit
    abstract IsPanelVisible: panelName: string -> bool


  module Serialization =
    open JDeck
    open Pomo.Core.Domain.Core.Serialization

    module HUDAnchor =
      let decoder: Decoder<HUDAnchor> =
        fun json -> decode {
          let! anchorStr = Required.string json

          match anchorStr with
          | "TopLeft" -> return TopLeft
          | "TopCenter" -> return TopCenter
          | "TopRight" -> return TopRight
          | "CenterLeft" -> return CenterLeft
          | "Center" -> return Center
          | "CenterRight" -> return CenterRight
          | "BottomLeft" -> return BottomLeft
          | "BottomCenter" -> return BottomCenter
          | "BottomRight" -> return BottomRight
          | other ->
            return!
              DecodeError.ofError(json.Clone(), $"Unknown HUDAnchor: {other}")
              |> Error
        }

    module HUDComponentLayout =
      let decoder: Decoder<HUDComponentLayout> =
        fun json -> decode {
          let d = HUDComponentLayout.defaults

          let! anchor =
            VOptional.Property.get ("Anchor", HUDAnchor.decoder) json

          and! offsetX = VOptional.Property.get ("OffsetX", Required.int) json
          and! offsetY = VOptional.Property.get ("OffsetY", Required.int) json

          and! visible =
            VOptional.Property.get ("Visible", Required.boolean) json

          return {
            Anchor = anchor |> ValueOption.defaultValue d.Anchor
            OffsetX = offsetX |> ValueOption.defaultValue d.OffsetX
            OffsetY = offsetY |> ValueOption.defaultValue d.OffsetY
            Visible = visible |> ValueOption.defaultValue d.Visible
          }
        }

    module HUDColorPalette =
      let decoder: Decoder<HUDColorPalette> =
        fun json -> decode {
          let d = HUDColorPalette.defaults

          let! healthFill =
            VOptional.Property.get ("HealthFill", Helper.colorFromHex) json

          and! healthLow =
            VOptional.Property.get ("HealthLow", Helper.colorFromHex) json

          and! healthBackground =
            VOptional.Property.get
              ("HealthBackground", Helper.colorFromHex)
              json

          and! manaFill =
            VOptional.Property.get ("ManaFill", Helper.colorFromHex) json

          and! manaBackground =
            VOptional.Property.get ("ManaBackground", Helper.colorFromHex) json

          and! buffBorder =
            VOptional.Property.get ("BuffBorder", Helper.colorFromHex) json

          and! debuffBorder =
            VOptional.Property.get ("DebuffBorder", Helper.colorFromHex) json

          and! dotBorder =
            VOptional.Property.get ("DotBorder", Helper.colorFromHex) json

          and! factionPlayer =
            VOptional.Property.get ("FactionPlayer", Helper.colorFromHex) json

          and! factionAlly =
            VOptional.Property.get ("FactionAlly", Helper.colorFromHex) json

          and! factionEnemy =
            VOptional.Property.get ("FactionEnemy", Helper.colorFromHex) json

          and! factionNPC =
            VOptional.Property.get ("FactionNPC", Helper.colorFromHex) json

          and! factionNeutral =
            VOptional.Property.get ("FactionNeutral", Helper.colorFromHex) json

          and! teamRed =
            VOptional.Property.get ("TeamRed", Helper.colorFromHex) json

          and! teamBlue =
            VOptional.Property.get ("TeamBlue", Helper.colorFromHex) json

          and! teamGreen =
            VOptional.Property.get ("TeamGreen", Helper.colorFromHex) json

          and! teamYellow =
            VOptional.Property.get ("TeamYellow", Helper.colorFromHex) json

          and! teamOrange =
            VOptional.Property.get ("TeamOrange", Helper.colorFromHex) json

          and! teamPurple =
            VOptional.Property.get ("TeamPurple", Helper.colorFromHex) json

          and! teamPink =
            VOptional.Property.get ("TeamPink", Helper.colorFromHex) json

          and! teamCyan =
            VOptional.Property.get ("TeamCyan", Helper.colorFromHex) json

          and! teamWhite =
            VOptional.Property.get ("TeamWhite", Helper.colorFromHex) json

          and! teamBlack =
            VOptional.Property.get ("TeamBlack", Helper.colorFromHex) json

          and! offensiveSlot =
            VOptional.Property.get ("OffensiveSlot", Helper.colorFromHex) json

          and! supportiveSlot =
            VOptional.Property.get ("SupportiveSlot", Helper.colorFromHex) json

          and! castBarFill =
            VOptional.Property.get ("CastBarFill", Helper.colorFromHex) json

          and! castBarBackground =
            VOptional.Property.get
              ("CastBarBackground", Helper.colorFromHex)
              json

          and! textNormal =
            VOptional.Property.get ("TextNormal", Helper.colorFromHex) json

          and! textDamage =
            VOptional.Property.get ("TextDamage", Helper.colorFromHex) json

          and! textCrit =
            VOptional.Property.get ("TextCrit", Helper.colorFromHex) json

          and! textHeal =
            VOptional.Property.get ("TextHeal", Helper.colorFromHex) json

          and! textStatus =
            VOptional.Property.get ("TextStatus", Helper.colorFromHex) json

          and! textMiss =
            VOptional.Property.get ("TextMiss", Helper.colorFromHex) json

          return {
            HealthFill = healthFill |> ValueOption.defaultValue d.HealthFill
            HealthLow = healthLow |> ValueOption.defaultValue d.HealthLow
            HealthBackground =
              healthBackground |> ValueOption.defaultValue d.HealthBackground
            ManaFill = manaFill |> ValueOption.defaultValue d.ManaFill
            ManaBackground =
              manaBackground |> ValueOption.defaultValue d.ManaBackground
            BuffBorder = buffBorder |> ValueOption.defaultValue d.BuffBorder
            DebuffBorder =
              debuffBorder |> ValueOption.defaultValue d.DebuffBorder
            DotBorder = dotBorder |> ValueOption.defaultValue d.DotBorder
            FactionPlayer =
              factionPlayer |> ValueOption.defaultValue d.FactionPlayer
            FactionAlly = factionAlly |> ValueOption.defaultValue d.FactionAlly
            FactionEnemy =
              factionEnemy |> ValueOption.defaultValue d.FactionEnemy
            FactionNPC = factionNPC |> ValueOption.defaultValue d.FactionNPC
            FactionNeutral =
              factionNeutral |> ValueOption.defaultValue d.FactionNeutral
            TeamRed = teamRed |> ValueOption.defaultValue d.TeamRed
            TeamBlue = teamBlue |> ValueOption.defaultValue d.TeamBlue
            TeamGreen = teamGreen |> ValueOption.defaultValue d.TeamGreen
            TeamYellow = teamYellow |> ValueOption.defaultValue d.TeamYellow
            TeamOrange = teamOrange |> ValueOption.defaultValue d.TeamOrange
            TeamPurple = teamPurple |> ValueOption.defaultValue d.TeamPurple
            TeamPink = teamPink |> ValueOption.defaultValue d.TeamPink
            TeamCyan = teamCyan |> ValueOption.defaultValue d.TeamCyan
            TeamWhite = teamWhite |> ValueOption.defaultValue d.TeamWhite
            TeamBlack = teamBlack |> ValueOption.defaultValue d.TeamBlack
            OffensiveSlot =
              offensiveSlot |> ValueOption.defaultValue d.OffensiveSlot
            SupportiveSlot =
              supportiveSlot |> ValueOption.defaultValue d.SupportiveSlot
            CastBarFill = castBarFill |> ValueOption.defaultValue d.CastBarFill
            CastBarBackground =
              castBarBackground |> ValueOption.defaultValue d.CastBarBackground
            TextNormal = textNormal |> ValueOption.defaultValue d.TextNormal
            TextDamage = textDamage |> ValueOption.defaultValue d.TextDamage
            TextCrit = textCrit |> ValueOption.defaultValue d.TextCrit
            TextHeal = textHeal |> ValueOption.defaultValue d.TextHeal
            TextStatus = textStatus |> ValueOption.defaultValue d.TextStatus
            TextMiss = textMiss |> ValueOption.defaultValue d.TextMiss
          }
        }

    module HUDTheme =
      let decoder: Decoder<HUDTheme> =
        fun json -> decode {
          let d = HUDTheme.defaults

          let! colors =
            VOptional.Property.get ("Colors", HUDColorPalette.decoder) json

          and! fontName =
            VOptional.Property.get ("FontName", Required.string) json

          and! cooldownOverlayColor =
            VOptional.Property.get
              ("CooldownOverlayColor", Helper.colorFromHex)
              json

          and! tooltipBackground =
            VOptional.Property.get
              ("TooltipBackground", Helper.colorFromHex)
              json

          and! tooltipBorder =
            VOptional.Property.get ("TooltipBorder", Helper.colorFromHex) json

          and! barBorderWidth =
            VOptional.Property.get ("BarBorderWidth", Required.int) json

          and! barCornerRadius =
            VOptional.Property.get ("BarCornerRadius", Required.int) json

          return {
            Colors = colors |> ValueOption.defaultValue d.Colors
            FontName = fontName |> ValueOption.defaultValue d.FontName
            CooldownOverlayColor =
              cooldownOverlayColor
              |> ValueOption.defaultValue d.CooldownOverlayColor
            TooltipBackground =
              tooltipBackground |> ValueOption.defaultValue d.TooltipBackground
            TooltipBorder =
              tooltipBorder |> ValueOption.defaultValue d.TooltipBorder
            BarBorderWidth =
              barBorderWidth |> ValueOption.defaultValue d.BarBorderWidth
            BarCornerRadius =
              barCornerRadius |> ValueOption.defaultValue d.BarCornerRadius
          }
        }

    module HUDLayout =
      let decoder: Decoder<HUDLayout> =
        fun json -> decode {
          let d = HUDLayout.defaults

          let! playerVitals =
            VOptional.Property.get
              ("PlayerVitals", HUDComponentLayout.decoder)
              json

          and! actionBar =
            VOptional.Property.get
              ("ActionBar", HUDComponentLayout.decoder)
              json

          and! statusEffects =
            VOptional.Property.get
              ("StatusEffects", HUDComponentLayout.decoder)
              json

          and! targetFrame =
            VOptional.Property.get
              ("TargetFrame", HUDComponentLayout.decoder)
              json

          and! castBar =
            VOptional.Property.get ("CastBar", HUDComponentLayout.decoder) json

          and! characterSheet =
            VOptional.Property.get
              ("CharacterSheet", HUDComponentLayout.decoder)
              json

          and! equipmentPanel =
            VOptional.Property.get
              ("EquipmentPanel", HUDComponentLayout.decoder)
              json

          and! miniMap =
            VOptional.Property.get ("MiniMap", HUDComponentLayout.decoder) json

          return {
            PlayerVitals =
              playerVitals |> ValueOption.defaultValue d.PlayerVitals
            ActionBar = actionBar |> ValueOption.defaultValue d.ActionBar
            StatusEffects =
              statusEffects |> ValueOption.defaultValue d.StatusEffects
            TargetFrame = targetFrame |> ValueOption.defaultValue d.TargetFrame
            CastBar = castBar |> ValueOption.defaultValue d.CastBar
            CharacterSheet =
              characterSheet |> ValueOption.defaultValue d.CharacterSheet
            EquipmentPanel =
              equipmentPanel |> ValueOption.defaultValue d.EquipmentPanel
            MiniMap = miniMap |> ValueOption.defaultValue d.MiniMap
          }
        }

    module HUDConfig =
      let decoder: Decoder<HUDConfig> =
        fun json -> decode {
          let d = HUDConfig.defaults
          let! theme = VOptional.Property.get ("Theme", HUDTheme.decoder) json

          and! layout =
            VOptional.Property.get ("Layout", HUDLayout.decoder) json

          return {
            Theme = theme |> ValueOption.defaultValue d.Theme
            Layout = layout |> ValueOption.defaultValue d.Layout
          }
        }

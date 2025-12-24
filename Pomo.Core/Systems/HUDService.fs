namespace Pomo.Core.Systems

open System
open System.IO
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open JDeck
open Pomo.Core.Domain.UI
open Pomo.Core.Domain.Entity

type IHUDService =
  abstract Config: HUDConfig aval
  abstract GetFactionColor: Faction -> Color
  abstract SetPanelVisible: panelName: string -> visible: bool -> unit
  abstract IsPanelVisible: panelName: string -> bool

module HUDService =

  let private loadConfig(contentPath: string) : HUDConfig =
    let configPath = Path.Combine(contentPath, "HUDConfig.json")

    if File.Exists(configPath) then
      try
        let json = File.ReadAllText(configPath)

        match Decoding.fromString(json, Serialization.HUDConfig.decoder) with
        | Ok config -> config
        | Error err ->
          printfn $"[HUDService] Failed to parse HUDConfig.json: {err}"
          HUDConfig.defaults
      with ex ->
        printfn $"[HUDService] Error reading HUDConfig.json: {ex.Message}"
        HUDConfig.defaults
    else
      printfn
        $"[HUDService] HUDConfig.json not found at {configPath}, using defaults"

      HUDConfig.defaults

  let create(contentPath: string) : IHUDService =
    let initialConfig = loadConfig contentPath
    let config = cval initialConfig

    { new IHUDService with
        member _.Config = config :> HUDConfig aval

        member _.GetFactionColor(faction: Faction) =
          let cfg = AVal.force config
          HUDColorPalette.getColorForFaction cfg.Theme.Colors faction

        member _.SetPanelVisible (panelName: string) (visible: bool) =
          transact(fun () ->
            let current = config.Value

            let newLayout =
              match panelName with
              | "CharacterSheet" -> {
                  current.Layout with
                      CharacterSheet = {
                        current.Layout.CharacterSheet with
                            Visible = visible
                      }
                }
              | "EquipmentPanel" -> {
                  current.Layout with
                      EquipmentPanel = {
                        current.Layout.EquipmentPanel with
                            Visible = visible
                      }
                }
              | _ -> current.Layout

            config.Value <- { current with Layout = newLayout })

        member _.IsPanelVisible(panelName: string) =
          let cfg = AVal.force config

          match panelName with
          | "CharacterSheet" -> cfg.Layout.CharacterSheet.Visible
          | "EquipmentPanel" -> cfg.Layout.EquipmentPanel.Visible
          | "PlayerVitals" -> cfg.Layout.PlayerVitals.Visible
          | "ActionBar" -> cfg.Layout.ActionBar.Visible
          | "StatusEffects" -> cfg.Layout.StatusEffects.Visible
          | "TargetFrame" -> cfg.Layout.TargetFrame.Visible
          | "CastBar" -> cfg.Layout.CastBar.Visible
          | "MiniMap" -> cfg.Layout.MiniMap.Visible
          | _ -> false
    }

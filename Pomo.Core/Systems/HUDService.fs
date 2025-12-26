namespace Pomo.Core.Systems

open System
open System.IO
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open JDeck
open Pomo.Core.Domain.UI
open Pomo.Core.Domain.Entity

module HUDService =

  let private loadConfig(filePath: string) : HUDConfig =
    try
      let json =
        Path.Combine(AppContext.BaseDirectory, filePath) |> File.ReadAllText

      match Decoding.fromString(json, Serialization.HUDConfig.decoder) with
      | Ok config -> config
      | Error err ->
        printfn $"[HUDService] Failed to parse HUDConfig.json: {err}"
        HUDConfig.defaults
    with ex ->
      printfn $"[HUDService] Error loading HUDConfig.json: {ex.Message}"
      HUDConfig.defaults

  let create(contentPath: string) : IHUDService =
    let initialConfig = loadConfig contentPath
    let config = cval initialConfig

    { new IHUDService with
        member _.Config = config :> HUDConfig aval

        member _.GetFactionColor(faction: Faction) =
          let cfg = AVal.force config
          HUDColorPalette.getColorForFaction cfg.Theme.Colors faction

        member this.TogglePanelVisible(panelId: HUDPanelId) =
          let current = this.IsPanelVisible panelId
          this.SetPanelVisible panelId (not current)

        member _.SetPanelVisible (panelId: HUDPanelId) (visible: bool) =
          transact(fun () ->
            let current = config.Value

            let newConfig =
              match panelId with
              | HUDPanelId.PlayerVitals -> {
                  current with
                      Layout.PlayerVitals.Visible = visible
                }
              | HUDPanelId.ActionBar -> {
                  current with
                      Layout.ActionBar.Visible = visible
                }
              | HUDPanelId.StatusEffects -> {
                  current with
                      Layout.StatusEffects.Visible = visible
                }
              | HUDPanelId.TargetFrame -> {
                  current with
                      Layout.TargetFrame.Visible = visible
                }
              | HUDPanelId.CastBar -> {
                  current with
                      Layout.CastBar.Visible = visible
                }
              | HUDPanelId.CharacterSheet -> {
                  current with
                      Layout.CharacterSheet.Visible = visible
                }
              | HUDPanelId.EquipmentPanel -> {
                  current with
                      Layout.EquipmentPanel.Visible = visible
                }
              | HUDPanelId.MiniMap ->
                  {
                    current with
                        Layout.MiniMap.Visible = visible
                  }

            config.Value <- newConfig)

        member _.IsPanelVisible(panelId: HUDPanelId) =
          let cfg = AVal.force config

          match panelId with
          | HUDPanelId.PlayerVitals -> cfg.Layout.PlayerVitals.Visible
          | HUDPanelId.ActionBar -> cfg.Layout.ActionBar.Visible
          | HUDPanelId.StatusEffects -> cfg.Layout.StatusEffects.Visible
          | HUDPanelId.TargetFrame -> cfg.Layout.TargetFrame.Visible
          | HUDPanelId.CastBar -> cfg.Layout.CastBar.Visible
          | HUDPanelId.CharacterSheet -> cfg.Layout.CharacterSheet.Visible
          | HUDPanelId.EquipmentPanel -> cfg.Layout.EquipmentPanel.Visible
          | HUDPanelId.MiniMap -> cfg.Layout.MiniMap.Visible
    }

namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Myra.Graphics2D.UI
open Myra.Graphics2D
open FSharp.Data.Adaptive
open Pomo.Core.Domain.UI
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.World
open Pomo.Core.Systems.HUDAnimation

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
              let fillRect =
                Rectangle(bounds.X, bounds.Y, fillWidth, bounds.Height)

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

namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Item
open Pomo.Core.Stores

module DebugRender =
  open Pomo.Core

  type DebugDrawCommand =
    | DrawActiveEffect of effect: ActiveEffect * entityPosition: Vector2
    | DrawDerivedStats of
        ownerId: Guid<EntityId> *
        stats: DerivedStats *
        resources: Entity.Resource option *
        entityPosition: Vector2
    | DrawInventory of ownerId: Guid<EntityId> * inventory: HashSet<Item.ItemInstance> * entityPosition: Vector2
    | DrawEquipped of ownerId: Guid<EntityId> * equipped: HashMap<Item.Slot, Item.ItemInstance> * entityPosition: Vector2

  let private generateActiveEffectCommands(world: World.World) =
    Projections.UpdatedPositions world
    |> AMap.chooseA(fun entityId pos -> adaptive {
      let! effectsOpt = AMap.tryFind entityId world.ActiveEffects

      return
        effectsOpt
        |> Option.map(fun effects ->
          effects
          |> IndexList.map(fun effect -> DrawActiveEffect(effect, pos)))
    })
    |> AMap.fold
      (fun acc _ cmds -> IndexList.concat [ acc; cmds ])
      IndexList.empty

  let private generateDerivedStatsCommands
    (world: World.World, itemStore: ItemStore)
    (showStats: bool aval)
    =
    Projections.UpdatedPositions world
    |> AMap.chooseA(fun entityId pos -> adaptive {
      let! show = showStats

      if not show then
        return None
      else
        let! statsOpt =
          Projections.CalculateDerivedStats (world, itemStore) |> AMap.tryFind entityId
        let! resourcesOpt =
            world.Resources |> AMap.tryFind entityId

        return
          statsOpt
          |> Option.map(fun stats ->
            IndexList.single(DrawDerivedStats(entityId, stats, resourcesOpt, pos)))
    })
    |> AMap.fold
      (fun acc _ cmds -> IndexList.concat [ acc; cmds ])
      IndexList.empty

  let private generateInventoryCommands
    (world: World.World)
    (showInventory: bool aval)
    =
    Projections.UpdatedPositions world
    |> AMap.chooseA(fun entityId pos -> adaptive {
      let! show = showInventory
      if not show then
        return None
      else
        let! inventoryOpt =
          Projections.getInventory world |> AMap.tryFind entityId
        
        return
          inventoryOpt
          |> Option.map(fun inventory ->
            IndexList.single(DrawInventory(entityId, inventory, pos)))
    })
    |> AMap.fold
      (fun acc _ cmds -> IndexList.concat [ acc; cmds ])
      IndexList.empty

  let private generateEquippedCommands
    (world: World.World)
    (showInventory: bool aval)
    =
    Projections.UpdatedPositions world
    |> AMap.chooseA(fun entityId pos -> adaptive {
      let! show = showInventory
      if not show then
        return None
      else
        let! equippedOpt =
          Projections.getEquippedItems world |> AMap.tryFind entityId
        
        return
          equippedOpt
          |> Option.map(fun equipped ->
            IndexList.single(DrawEquipped(entityId, equipped, pos)))
    })
    |> AMap.fold
      (fun acc _ cmds -> IndexList.concat [ acc; cmds ])
      IndexList.empty

  let private generateDebugCommands
    (world: World.World, itemStore: ItemStore)
    (showStats: bool aval)
    (showInventory: bool aval)
    =
    adaptive {
      let! effectCmds = generateActiveEffectCommands world
      let! statsCmds = generateDerivedStatsCommands (world, itemStore) showStats
      let! inventoryCmds = generateInventoryCommands world showInventory
      let! equippedCmds = generateEquippedCommands world showInventory
      return IndexList.concat [ effectCmds; statsCmds; inventoryCmds; equippedCmds ]
    }

  type DebugRenderSystem(game: Game, playerId: Guid<EntityId>) =
    inherit DrawableGameComponent(game)

    let world: World.World = game.Services.GetService<World.World>()
    let itemStore: ItemStore = game.Services.GetService<ItemStore>()
    let spriteBatch = lazy (new SpriteBatch(game.GraphicsDevice))
    let mutable hudFont = Unchecked.defaultof<_>

    let showStats = cval false
    let showInventory = cval false
    let debugCommands = generateDebugCommands (world, itemStore) showStats showInventory

    let getRemainingDuration (totalGameTime: TimeSpan) (effect: ActiveEffect) =
      match effect.SourceEffect.Duration with
      | Duration.Timed totalDuration ->
        let elapsed = totalGameTime - effect.StartTime
        let remaining = totalDuration - elapsed

        if remaining.TotalSeconds > 0.0 then
          $" (%.1f{remaining.TotalSeconds}s)"
        else
          " (Expired)"
      | _ -> ""

    let drawActiveEffect(effect, entityPos: Vector2, yOffset, totalGameTime) =
      let durationText = getRemainingDuration totalGameTime effect

      let text =
        $"%s{effect.SourceEffect.Name} (%d{effect.StackCount})%s{durationText}"

      let textPosition = Vector2(entityPos.X, entityPos.Y + yOffset)
      struct (text, textPosition)

    let formatStats(stats: DerivedStats, resources: Entity.Resource option) =
      let sb = System.Text.StringBuilder()

      let currentHP = resources |> Option.map (fun r -> r.HP) |> Option.defaultValue stats.HP
      let currentMP = resources |> Option.map (fun r -> r.MP) |> Option.defaultValue stats.MP

      sb.AppendLine($"HP: %d{currentHP}/%d{stats.HP} | MP: %d{currentMP}/%d{stats.MP}") |> ignore
      sb.AppendLine($"AP: %d{stats.AP} | DP: %d{stats.DP} | AC: %d{stats.AC}") |> ignore
      sb.AppendLine($"MA: %d{stats.MA} | MD: %d{stats.MD} | DX: %d{stats.DX}") |> ignore
      sb.AppendLine($"WT: %d{stats.WT} | DA: %d{stats.DA} | HV: %d{stats.HV}") |> ignore
      sb.AppendLine($"LK: %d{stats.LK} | MS: %d{stats.MS}") |> ignore

      if not stats.ElementAttributes.IsEmpty then
          let attrStr =
              stats.ElementAttributes
              |> HashMap.toSeq
              |> Seq.map (fun (elem, value) -> $"%A{elem}: {value:P0}")
              |> String.concat " | "
          sb.AppendLine($"Attr: {attrStr}") |> ignore

      if not stats.ElementResistances.IsEmpty then
          let resStr =
              stats.ElementResistances
              |> HashMap.toSeq
              |> Seq.map (fun (elem, value) -> $"%A{elem}: {value:P0}")
              |> String.concat " | "
          sb.AppendLine($"Res: {resStr}") |> ignore

      sb.ToString()

    let playerActionStates =
      world.GameActionStates
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue HashMap.empty)

    let toggleSheetState = playerActionStates |> AVal.map (fun s -> s |> HashMap.tryFindV ToggleCharacterSheet)
    let toggleInventoryState = playerActionStates |> AVal.map (fun s -> s |> HashMap.tryFindV ToggleInventory)

    override _.Initialize() : unit =
      base.Initialize()
      hudFont <- game.Content.Load<SpriteFont>("Fonts/Hud")

    override _.Update gameTime =
      base.Update gameTime
      let sheetToggled = toggleSheetState |> AVal.force
      let inventoryToggled = toggleInventoryState |> AVal.force

      match sheetToggled with
      | ValueSome Pressed ->
        transact(fun () -> showStats.Value <- not showStats.Value)
      | _ -> ()

      match inventoryToggled with
      | ValueSome Pressed ->
        transact(fun () -> showInventory.Value <- not showInventory.Value)
      | _ -> ()

    override _.Draw gameTime =
      base.Draw gameTime

      let sb = spriteBatch.Value
      let commandsToExecute = AVal.force debugCommands
      let totalGameTime = world.Time |> AVal.map _.TotalGameTime |> AVal.force
      let mutable yOffsets = HashMap.empty<Guid<EntityId>, float32>

      sb.Begin()

      for command in commandsToExecute do
        match command with
        | DrawActiveEffect(effect, entityPos) ->
          let yOffset =
            yOffsets.TryFind effect.TargetEntity |> Option.defaultValue -20.0f

          yOffsets <-
            yOffsets |> HashMap.add effect.TargetEntity (yOffset - 15.0f)

          let struct (text, textPosition) =
            drawActiveEffect(effect, entityPos, yOffset, totalGameTime)

          sb.DrawString(hudFont, text, textPosition, Color.Yellow)

        | DrawDerivedStats(ownerId, stats, resources, entityPos) ->
          let yOffset = yOffsets.TryFind ownerId |> Option.defaultValue -20.0f
          let text = formatStats(stats, resources)
          let textPosition = Vector2(entityPos.X, entityPos.Y + yOffset)
          sb.DrawString(hudFont, text, textPosition, Color.Cyan)

        | DrawInventory(ownerId, inventory, entityPos) ->
          let yOffset = yOffsets.TryFind ownerId |> Option.defaultValue -20.0f
          let sb = System.Text.StringBuilder()
          sb.AppendLine("Inventory:") |> ignore
          for item in inventory do
            let itemName = itemStore.tryFind item.ItemId |> ValueOption.map (fun def -> def.Name) |> ValueOption.defaultValue "Unknown"
            sb.AppendLine($"- {itemName}") |> ignore
          let text = sb.ToString()
          let textPosition = Vector2(entityPos.X, entityPos.Y + yOffset + 150.0f)
          sb.DrawString(hudFont, text, textPosition, Color.White)

        | DrawEquipped(ownerId, equipped, entityPos) ->
          let yOffset = yOffsets.TryFind ownerId |> Option.defaultValue -20.0f
          let sb = System.Text.StringBuilder()
          sb.AppendLine("Equipped:") |> ignore
          for KeyValue(slot, item) in equipped do
            let itemName = itemStore.tryFind item.ItemId |> ValueOption.map (fun def -> def.Name) |> ValueOption.defaultValue "Unknown"
            sb.AppendLine($"- {slot}: {itemName}") |> ignore
          let text = sb.ToString()
          let textPosition = Vector2(entityPos.X, entityPos.Y + yOffset + 250.0f)
          sb.DrawString(hudFont, text, textPosition, Color.LightGreen)

      sb.End()

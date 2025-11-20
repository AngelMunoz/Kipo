namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.Map
open Pomo.Core.Stores
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.AI
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
    | DrawInventory of
      ownerId: Guid<EntityId> *
      inventory: HashSet<Item.ItemDefinition> *
      entityPosition: Vector2
    | DrawEquipped of
      ownerId: Guid<EntityId> *
      equipped: HashMap<Item.Slot, Item.ItemDefinition> *
      entityPosition: Vector2
    | DrawAIState of
      ownerId: Guid<EntityId> *
      state: AIState *
      entityPosition: Vector2
    | DrawMapObject of
      points: IndexList<Vector2> voption *
      position: Vector2 *
      width: float32 *
      height: float32 *
      rotation: float32 *
      color: Color

  let private generateActiveEffectCommands
    (world: World.World)
    (positions: amap<Guid<EntityId>, Vector2>)
    =
    positions
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
    (world: World.World)
    (positions: amap<Guid<EntityId>, Vector2>)
    (derivedStats: amap<Guid<EntityId>, DerivedStats>)
    (showStats: bool aval)
    =
    positions
    |> AMap.chooseA(fun entityId pos -> adaptive {
      let! show = showStats

      if not show then
        return None
      else
        let! statsOpt = derivedStats |> AMap.tryFind entityId

        let! resourcesOpt = world.Resources |> AMap.tryFind entityId

        return
          statsOpt
          |> Option.map(fun stats ->
            IndexList.single(
              DrawDerivedStats(entityId, stats, resourcesOpt, pos)
            ))
    })
    |> AMap.fold
      (fun acc _ cmds -> IndexList.concat [ acc; cmds ])
      IndexList.empty

  let private generateInventoryCommands
    (positions: amap<Guid<EntityId>, Vector2>)
    (inventory: amap<Guid<EntityId>, HashSet<Item.ItemDefinition>>)
    (showInventory: bool aval)
    =
    positions
    |> AMap.chooseA(fun entityId pos -> adaptive {
      let! show = showInventory

      if not show then
        return None
      else
        let! inventoryOpt = inventory |> AMap.tryFind entityId

        return
          inventoryOpt
          |> Option.map(fun inventory ->
            IndexList.single(DrawInventory(entityId, inventory, pos)))
    })
    |> AMap.fold
      (fun acc _ cmds -> IndexList.concat [ acc; cmds ])
      IndexList.empty

  let private generateEquippedCommands
    (positions: amap<Guid<EntityId>, Vector2>)
    (equippedItems:
      amap<Guid<EntityId>, HashMap<Item.Slot, Item.ItemDefinition>>)
    (showInventory: bool aval)
    =
    positions
    |> AMap.chooseA(fun entityId pos -> adaptive {
      let! show = showInventory

      if not show then
        return None
      else
        let! equippedOpt = equippedItems |> AMap.tryFind entityId

        return
          equippedOpt
          |> Option.map(fun equipped ->
            IndexList.single(DrawEquipped(entityId, equipped, pos)))
    })
    |> AMap.fold
      (fun acc _ cmds -> IndexList.concat [ acc; cmds ])
      IndexList.empty

  let private generateAIStateCommands
    (positions: amap<Guid<EntityId>, Vector2>)
    (aiControllers: amap<Guid<EntityId>, AIController>)
    =
    positions
    |> AMap.chooseA(fun entityId pos -> adaptive {
      let! controllerOpt = aiControllers |> AMap.tryFind entityId

      return
        controllerOpt
        |> Option.map(fun controller ->
          IndexList.single(
            DrawAIState(entityId, controller.currentState, pos)
          ))
    })
    |> AMap.fold
      (fun acc _ cmds -> IndexList.concat [ acc; cmds ])
      IndexList.empty

  let private generateMapObjectCommands(map: MapDefinition voption) =
    match map with
    | ValueSome map ->
      map.ObjectGroups
      |> IndexList.collect(fun group ->
        if group.Visible then
          group.Objects
          |> IndexList.map(fun obj ->
            let color =
              match obj.Type with
              | ValueSome Wall -> Color.Red
              | ValueSome Spawn -> Color.Green
              | ValueSome Zone ->
                if obj.Name.Contains("Void") then Color.Purple
                elif obj.Name.Contains("Slow") then Color.Yellow
                elif obj.Name.Contains("Speed") then Color.Cyan
                elif obj.Name.Contains("Healing") then Color.LimeGreen
                elif obj.Name.Contains("Damaging") then Color.Orange
                else Color.Blue
              | _ -> Color.White

            DrawMapObject(
              obj.Points,
              Vector2(obj.X, obj.Y),
              obj.Width,
              obj.Height,
              obj.Rotation,
              color
            ))
        else
          IndexList.empty)
    | ValueNone -> IndexList.empty

  let private generateDebugCommands
    (
      world: World.World,
      positions: amap<Guid<EntityId>, Vector2>,
      derivedStats: amap<Guid<EntityId>, DerivedStats>,
      inventory: amap<Guid<EntityId>, HashSet<Item.ItemDefinition>>,
      equippedItems:
        amap<Guid<EntityId>, HashMap<Item.Slot, Item.ItemDefinition>>,
      map: MapDefinition voption
    )
    (showStats: bool aval)
    (showInventory: bool aval)
    =
    adaptive {
      let! effectCmds = generateActiveEffectCommands world positions

      and! statsCmds =
        generateDerivedStatsCommands world positions derivedStats showStats

      and! inventoryCmds =
        generateInventoryCommands positions inventory showInventory

      and! equippedCmds =
        generateEquippedCommands positions equippedItems showInventory

      and! aiStateCmds = generateAIStateCommands positions world.AIControllers

      return
        IndexList.concat [
          effectCmds
          statsCmds
          inventoryCmds
          equippedCmds
          aiStateCmds
          generateMapObjectCommands map
        ]
    }

  let private drawLine
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (start: Vector2)
    (end': Vector2)
    (color: Color)
    =
    let edge = end' - start
    let angle = float32(Math.Atan2(float edge.Y, float edge.X))

    sb.Draw(
      pixel,
      Microsoft.Xna.Framework.Rectangle(
        int start.X,
        int start.Y,
        int(edge.Length()),
        1
      ),
      Nullable(),
      color,
      angle,
      Vector2.Zero,
      SpriteEffects.None,
      0.0f
    )

  let private rotate (point: Vector2) (radians: float32) =
    let c = float32(Math.Cos(float radians))
    let s = float32(Math.Sin(float radians))
    Vector2(point.X * c - point.Y * s, point.X * s + point.Y * c)

  let private drawPolygon
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (points: IndexList<Vector2>)
    (position: Vector2)
    (rotation: float32)
    (color: Color)
    =
    let count = points.Count
    let radians = MathHelper.ToRadians(rotation)

    for i in 0 .. count - 1 do
      let p1 = rotate points.[i] radians + position
      let p2 = rotate points.[(i + 1) % count] radians + position
      drawLine sb pixel p1 p2 color

  let private drawEllipse
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (position: Vector2)
    (width: float32)
    (height: float32)
    (rotation: float32)
    (color: Color)
    =
    let segments = 32
    let radiusX = width / 2.0f
    let radiusY = height / 2.0f

    let centerOffset = Vector2(radiusX, radiusY)
    let step = MathHelper.TwoPi / float32 segments
    let radians = MathHelper.ToRadians(rotation)

    for i in 0 .. segments - 1 do
      let theta1 = float32 i * step
      let theta2 = float32(i + 1) * step

      // Points relative to center of ellipse
      let localP1 = Vector2(radiusX * cos theta1, radiusY * sin theta1)
      let localP2 = Vector2(radiusX * cos theta2, radiusY * sin theta2)

      // Points relative to top-left (0,0)
      let p1Unrotated = localP1 + centerOffset
      let p2Unrotated = localP2 + centerOffset

      // Rotate around (0,0)
      let p1Rotated = rotate p1Unrotated radians
      let p2Rotated = rotate p2Unrotated radians

      // Translate to world position
      let p1 = p1Rotated + position
      let p2 = p2Rotated + position

      drawLine sb pixel p1 p2 color

  type DebugRenderSystem(game: Game, playerId: Guid<EntityId>, mapKey: string) =
    inherit DrawableGameComponent(game)

    let world: World.World = game.Services.GetService<World.World>()
    let itemStore: ItemStore = game.Services.GetService<ItemStore>()

    let projections: Projections.ProjectionService =
      game.Services.GetService<Projections.ProjectionService>()

    let mapStore = game.Services.GetService<MapStore>()

    let spriteBatch = lazy (new SpriteBatch(game.GraphicsDevice))
    let mutable pixel: Texture2D voption = ValueNone
    let mutable hudFont = Unchecked.defaultof<_>

    let showStats = cval false
    let showInventory = cval false

    let debugCommands =
      generateDebugCommands
        (world,
         projections.UpdatedPositions,
         projections.DerivedStats,
         projections.Inventories,
         projections.EquipedItems,
         mapStore.tryFind mapKey)
        showStats
        showInventory

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

      let currentHP =
        resources |> Option.map(fun r -> r.HP) |> Option.defaultValue stats.HP

      let currentMP =
        resources |> Option.map(fun r -> r.MP) |> Option.defaultValue stats.MP

      sb.AppendLine(
        $"HP: %d{currentHP}/%d{stats.HP} | MP: %d{currentMP}/%d{stats.MP}"
      )
      |> ignore

      sb.AppendLine($"AP: %d{stats.AP} | DP: %d{stats.DP} | AC: %d{stats.AC}")
      |> ignore

      sb.AppendLine($"MA: %d{stats.MA} | MD: %d{stats.MD} | DX: %d{stats.DX}")
      |> ignore

      sb.AppendLine($"WT: %d{stats.WT} | DA: %d{stats.DA} | HV: %d{stats.HV}")
      |> ignore

      sb.AppendLine($"LK: %d{stats.LK} | MS: %d{stats.MS}") |> ignore

      if not stats.ElementAttributes.IsEmpty then
        let attrStr =
          stats.ElementAttributes
          |> HashMap.toSeq
          |> Seq.map(fun (elem, value) -> $"%A{elem}: {value:P0}")
          |> String.concat " | "

        sb.AppendLine($"Attr: {attrStr}") |> ignore

      if not stats.ElementResistances.IsEmpty then
        let resStr =
          stats.ElementResistances
          |> HashMap.toSeq
          |> Seq.map(fun (elem, value) -> $"%A{elem}: {value:P0}")
          |> String.concat " | "

        sb.AppendLine($"Res: {resStr}") |> ignore

      sb.ToString()

    let playerActionStates =
      world.GameActionStates
      |> AMap.tryFind playerId
      |> AVal.map(Option.defaultValue HashMap.empty)

    let toggleSheetState =
      playerActionStates
      |> AVal.map(fun s -> s |> HashMap.tryFindV ToggleCharacterSheet)

    let toggleInventoryState =
      playerActionStates
      |> AVal.map(fun s -> s |> HashMap.tryFindV ToggleInventory)

    override _.Initialize() : unit =
      base.Initialize()
      hudFont <- game.Content.Load<SpriteFont>("Fonts/Hud")

      let p = new Texture2D(game.GraphicsDevice, 1, 1)
      p.SetData([| Color.White |])
      pixel <- ValueSome p

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
          let strBuilder = System.Text.StringBuilder()
          strBuilder.AppendLine("Inventory:") |> ignore

          for item in inventory do
            strBuilder.AppendLine $"- {item.Name}" |> ignore

          let text = strBuilder.ToString()

          let textPosition =
            Vector2(entityPos.X, entityPos.Y + yOffset + 150.0f)

          sb.DrawString(hudFont, text, textPosition, Color.White)

        | DrawEquipped(ownerId, equipped, entityPos) ->
          let yOffset = yOffsets.TryFind ownerId |> Option.defaultValue -20.0f
          let strBuilder = System.Text.StringBuilder()
          strBuilder.AppendLine("Equipped:") |> ignore

          for slot, item in equipped do
            strBuilder.AppendLine $"- {slot}: {item.Name}" |> ignore

          let text = strBuilder.ToString()

          let textPosition =
            Vector2(entityPos.X, entityPos.Y + yOffset + 250.0f)

          sb.DrawString(hudFont, text, textPosition, Color.LightGreen)

        | DrawAIState(ownerId, state, entityPos) ->
          let yOffset = yOffsets.TryFind ownerId |> Option.defaultValue -20.0f
          let text = $"AI: %A{state}"
          let textPosition = Vector2(entityPos.X, entityPos.Y + yOffset - 20.0f)

          yOffsets <- yOffsets |> HashMap.add ownerId (yOffset - 35.0f)

          yOffsets <- yOffsets |> HashMap.add ownerId (yOffset - 35.0f)

          sb.DrawString(hudFont, text, textPosition, Color.Red)

        | DrawMapObject(points, position, width, height, rotation, color) ->
          match pixel with
          | ValueSome px ->
            match points with
            | ValueSome pts -> drawPolygon sb px pts position rotation color
            | ValueNone ->
              drawEllipse sb px position width height rotation color
          | ValueNone -> ()

      sb.End()

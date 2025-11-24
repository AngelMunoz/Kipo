namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Pomo.Core.Domain.Map
open Pomo.Core.Domain.Spatial
open Pomo.Core.Stores
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Domain
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Skill
open Pomo.Core.Domain.Action
open Pomo.Core.Domain.Item
open Pomo.Core.Domain.AI
open Pomo.Core.Stores
open Pomo.Core.EventBus
open Pomo.Core.Systems
open Pomo.Core.Domain.Events

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
      isEllipse: bool *
      color: Color
    | DrawEntityBounds of position: Vector2
    | DrawSpatialGrid of grid: HashMap<GridCell, IndexList<Guid<EntityId>>>
    | DrawCone of
      origin: Vector2 *
      direction: Vector2 *
      angle: float32 *
      length: float32 *
      color: Color
    | DrawLineShape of
      start: Vector2 *
      end': Vector2 *
      width: float32 *
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
              obj.IsEllipse,
              color
            ))
        else
          IndexList.empty)
    | ValueNone -> IndexList.empty

  let private generateEntityBoundsCommands
    (positions: amap<Guid<EntityId>, Vector2>)
    =
    positions
    |> AMap.chooseA(fun _ pos -> adaptive {
      return Some(IndexList.single(DrawEntityBounds pos))
    })
    |> AMap.fold
      (fun acc _ cmds -> IndexList.concat [ acc; cmds ])
      IndexList.empty

  let private generateDebugCommands
    (
      world: World.World,
      positions: amap<Guid<EntityId>, Vector2>,
      derivedStats: amap<Guid<EntityId>, DerivedStats>,
      inventory: amap<Guid<EntityId>, HashSet<Item.ItemDefinition>>,
      equippedItems:
        amap<Guid<EntityId>, HashMap<Item.Slot, Item.ItemDefinition>>,
      map: MapDefinition voption,
      spatialGrid: amap<GridCell, IndexList<Guid<EntityId>>>
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

      and! entityBoundsCmds = generateEntityBoundsCommands positions

      let! spatialGrid = spatialGrid |> AMap.toAVal

      return
        IndexList.concat [
          effectCmds
          statsCmds
          inventoryCmds
          equippedCmds
          aiStateCmds
          entityBoundsCmds
          generateMapObjectCommands map
          IndexList.single(DrawSpatialGrid spatialGrid)
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

  let private drawCone
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (origin: Vector2)
    (direction: Vector2)
    (angle: float32)
    (length: float32)
    (color: Color)
    =
    let halfAngleRad = MathHelper.ToRadians(angle / 2.0f)
    let baseAngle = float32(Math.Atan2(float direction.Y, float direction.X))

    let leftAngle = baseAngle - halfAngleRad
    let rightAngle = baseAngle + halfAngleRad

    let leftPoint =
      origin
      + Vector2(
        length * float32(Math.Cos(float leftAngle)),
        length * float32(Math.Sin(float leftAngle))
      )

    let rightPoint =
      origin
      + Vector2(
        length * float32(Math.Cos(float rightAngle)),
        length * float32(Math.Sin(float rightAngle))
      )

    drawLine sb pixel origin leftPoint color
    drawLine sb pixel origin rightPoint color

    // Draw arc
    let segments = 10
    let angleStep = (rightAngle - leftAngle) / float32 segments

    for i in 0 .. segments - 1 do
      let a1 = leftAngle + float32 i * angleStep
      let a2 = leftAngle + float32(i + 1) * angleStep

      let p1 =
        origin
        + Vector2(
          length * float32(Math.Cos(float a1)),
          length * float32(Math.Sin(float a1))
        )

      let p2 =
        origin
        + Vector2(
          length * float32(Math.Cos(float a2)),
          length * float32(Math.Sin(float a2))
        )

      drawLine sb pixel p1 p2 color

  let private drawLineShape
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (start: Vector2)
    (end': Vector2)
    (width: float32)
    (color: Color)
    =
    let edge = end' - start
    let length = edge.Length()

    if length > 0.0f then
      let direction = Vector2.Normalize edge
      let perpendicular = Vector2(-direction.Y, direction.X) * (width / 2.0f)

      let p1 = start + perpendicular
      let p2 = start - perpendicular
      let p3 = end' - perpendicular
      let p4 = end' + perpendicular

      drawLine sb pixel p1 p2 color
      drawLine sb pixel p2 p3 color
      drawLine sb pixel p3 p4 color
      drawLine sb pixel p4 p1 color

  type DebugRenderSystem(game: Game, playerId: Guid<EntityId>, mapKey: string) =
    inherit DrawableGameComponent(game)

    let world: World.World = game.Services.GetService<World.World>()
    let itemStore: ItemStore = game.Services.GetService<ItemStore>()

    let projections: Projections.ProjectionService =
      game.Services.GetService<Projections.ProjectionService>()

    let mapStore = game.Services.GetService<MapStore>()

    // Performance counters and timing
    let mutable frameCount = 0
    let mutable lastFPSTime = TimeSpan.Zero
    let mutable fps = 0.0f

    // Stats for culling performance
    let mutable totalEntities = 0
    let mutable visibleEntities = 0

    let spriteBatch = lazy (new SpriteBatch(game.GraphicsDevice))
    let mutable pixel: Texture2D voption = ValueNone
    let mutable hudFont = Unchecked.defaultof<_>

    let showStats = cval false
    let showInventory = cval false

    let transientCommands = ResizeArray<struct (DebugDrawCommand * TimeSpan)>()
    let skillStore = game.Services.GetService<SkillStore>()
    let subscriptions = new System.Reactive.Disposables.CompositeDisposable()

    let debugCommands =
      generateDebugCommands
        (world,
         projections.UpdatedPositions,
         projections.DerivedStats,
         projections.Inventories,
         projections.EquipedItems,
         mapStore.tryFind mapKey,
         projections.SpatialGrid)
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
      p.SetData([| Color.White |])
      pixel <- ValueSome p

      let eventBus = game.Services.GetService<EventBus>()

      eventBus.GetObservableFor<SystemCommunications.AbilityIntent>()
      |> FSharp.Control.Reactive.Observable.subscribe(fun intent ->
        match skillStore.tryFind intent.SkillId with
        | ValueSome(Active skill) ->
          let casterPos =
            projections.UpdatedPositions
            |> AMap.tryFind intent.Caster
            |> AVal.force
            |> Option.defaultValue Vector2.Zero

          let targetPos =
            match intent.Target with
            | SystemCommunications.TargetPosition pos -> pos
            | SystemCommunications.TargetDirection pos -> pos
            | SystemCommunications.TargetEntity id ->
              projections.UpdatedPositions
              |> AMap.tryFind id
              |> AVal.force
              |> Option.defaultValue casterPos
            | _ -> casterPos

          let color = Color.Orange

          let command =
            match skill.Area with
            | Cone(angle, length, _) ->
              let direction =
                if Vector2.DistanceSquared(casterPos, targetPos) > 0.001f then
                  Vector2.Normalize(targetPos - casterPos)
                else
                  Vector2.UnitX

              Some(DrawCone(casterPos, direction, angle, length, color))
            | Line(width, length, _) ->
              let direction =
                if Vector2.DistanceSquared(casterPos, targetPos) > 0.001f then
                  Vector2.Normalize(targetPos - casterPos)
                else
                  Vector2.UnitX

              let endPoint = casterPos + direction * length
              Some(DrawLineShape(casterPos, endPoint, width, color))
            | Circle(radius, _) ->
              // Represent circle as two ellipses (cross) or just one
              // For now, let's use a simple way or add DrawCircle if needed.
              // We can reuse DrawMapObject's ellipse logic or just add DrawCircle.
              // Let's use DrawCone with 360 angle? No, DrawCone logic might fail.
              // Let's just skip Circle for now or add DrawCircle later.
              None
            | AdaptiveCone(length, _) -> // length and maxTargets remain
              let direction =
                if Vector2.DistanceSquared(casterPos, targetPos) > 0.001f then
                  Vector2.Normalize(targetPos - casterPos)
                else
                  Vector2.UnitX

              let referenceForward = Vector2.UnitY // Assuming caster's forward
              let angleFromForwardRad = MathF.Acos(Vector2.Dot(referenceForward, direction))
              let angleFromForwardDeg = MathHelper.ToDegrees(angleFromForwardRad)

              let apertureAngle = 
                  if angleFromForwardDeg <= 90.0f then
                      30.0f + (angleFromForwardDeg / 90.0f) * 150.0f
                  else 
                      180.0f 
              
              Some(
                DrawCone(
                  casterPos,
                  direction,
                  apertureAngle,
                  length, // Use skill's full length
                  color
                )
              )
            | _ -> None

          match command with
          | Some cmd ->
            transientCommands.Add
              struct (cmd, Core.Constants.Debug.TransientCommandDuration)
          | None -> ()
        | _ -> ())
      |> subscriptions.Add

      eventBus.GetObservableFor<SystemCommunications.ProjectileImpacted>()
      |> FSharp.Control.Reactive.Observable.subscribe(fun impact ->
        match skillStore.tryFind impact.SkillId with
        | ValueSome(Active skill) ->
          let casterPos =
            projections.UpdatedPositions
            |> AMap.tryFind impact.CasterId
            |> AVal.force
            |> Option.defaultValue Vector2.Zero

          let impactPos =
            projections.UpdatedPositions
            |> AMap.tryFind impact.TargetId
            |> AVal.force
            |> Option.defaultValue Vector2.Zero

          let color = Color.Red

          let command =
            match skill.Area with
            | Cone(angle, length, _) ->
              let direction =
                if Vector2.DistanceSquared(casterPos, impactPos) > 0.001f then
                  Vector2.Normalize(impactPos - casterPos)
                else
                  Vector2.UnitX

              Some(DrawCone(impactPos, direction, angle, length, color))
            | Line(width, length, _) ->
              let direction =
                if Vector2.DistanceSquared(casterPos, impactPos) > 0.001f then
                  Vector2.Normalize(impactPos - casterPos)
                else
                  Vector2.UnitX

              let endPoint = impactPos + direction * length
              Some(DrawLineShape(impactPos, endPoint, width, color))
            | AdaptiveCone _ -> None // Do not draw cone on impact for AdaptiveCone
            | _ -> None

          match command with
          | Some cmd ->
            transientCommands.Add(
              struct (cmd, Core.Constants.Debug.TransientCommandDuration)
            )
          | None -> ()
        | _ -> ())
      |> subscriptions.Add

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

      // Prune expired transient commands
      let activeTransient = ResizeArray()

      for struct (cmd, duration) in transientCommands do
        let newDuration = duration - gameTime.ElapsedGameTime

        if newDuration > TimeSpan.Zero then
          activeTransient.Add(struct (cmd, newDuration))

      transientCommands.Clear()
      transientCommands.AddRange activeTransient

      let mutable yOffsets = HashMap.empty<Guid<EntityId>, float32>

      let cameraService = game.Services.GetService<Core.CameraService>()
      let cameras = cameraService.GetAllCameras()

      for struct (playerId, camera) in cameras do
        // Reset yOffsets for each camera to ensure text stacks correctly per view
        yOffsets <- HashMap.empty

        let transform =
          Matrix.CreateTranslation(-camera.Position.X, -camera.Position.Y, 0.0f)
          * Matrix.CreateScale(camera.Zoom)
          * Matrix.CreateTranslation(
            float32 camera.Viewport.Width / 2.0f,
            float32 camera.Viewport.Height / 2.0f,
            0.0f
          )

        game.GraphicsDevice.Viewport <- camera.Viewport
        sb.Begin(transformMatrix = transform)

        for command in commandsToExecute do
          match command with
          | DrawActiveEffect(effect, entityPos) ->
            let yOffset =
              yOffsets.TryFind effect.TargetEntity
              |> Option.defaultValue Core.Constants.Debug.StatYOffset

            yOffsets <-
              yOffsets
              |> HashMap.add
                effect.TargetEntity
                (yOffset + Core.Constants.Debug.EffectYOffset)

            let struct (text, textPosition) =
              drawActiveEffect(effect, entityPos, yOffset, totalGameTime)

            sb.DrawString(hudFont, text, textPosition, Color.Yellow)

          | DrawDerivedStats(ownerId, stats, resources, entityPos) ->
            let yOffset =
              yOffsets.TryFind ownerId
              |> Option.defaultValue Core.Constants.Debug.StatYOffset

            let text = formatStats(stats, resources)
            let textPosition = Vector2(entityPos.X, entityPos.Y + yOffset)
            sb.DrawString(hudFont, text, textPosition, Color.Cyan)

          | DrawInventory(ownerId, inventory, entityPos) ->
            let yOffset =
              yOffsets.TryFind ownerId
              |> Option.defaultValue Core.Constants.Debug.StatYOffset

            let strBuilder = System.Text.StringBuilder()
            strBuilder.AppendLine("Inventory:") |> ignore

            for item in inventory do
              strBuilder.AppendLine($"- {item.Name}") |> ignore

            sb.DrawString(
              hudFont,
              strBuilder.ToString(),
              entityPos + Vector2(0.0f, yOffset),
              Color.White
            )

          | DrawEquipped(ownerId, equipped, entityPos) ->
            let yOffset =
              yOffsets.TryFind ownerId
              |> Option.defaultValue Core.Constants.Debug.StatYOffset

            let strBuilder = System.Text.StringBuilder()
            strBuilder.AppendLine("Equipped:") |> ignore

            for (slot, item) in equipped do
              strBuilder.AppendLine($"- %A{slot}: {item.Name}") |> ignore

            sb.DrawString(
              hudFont,
              strBuilder.ToString(),
              entityPos + Vector2(0.0f, yOffset + 100.0f),
              Color.White
            )

          | DrawAIState(ownerId, state, entityPos) ->
            let yOffset =
              yOffsets.TryFind ownerId
              |> Option.defaultValue Core.Constants.Debug.StatYOffset

            let text = $"AI: %A{state}"

            let textPosition =
              Vector2(entityPos.X, entityPos.Y + yOffset - 20.0f)

            sb.DrawString(hudFont, text, textPosition, Color.Magenta)

          | DrawMapObject(points,
                          position,
                          width,
                          height,
                          rotation,
                          isEllipse,
                          color) ->
            match pixel with
            | ValueSome px ->
              match points with
              | ValueSome pts -> drawPolygon sb px pts position rotation color
              | ValueNone ->
                if isEllipse then
                  drawEllipse sb px position width height rotation color
                else
                  let radians = MathHelper.ToRadians(rotation)
                  let halfWidth = width / 2.0f
                  let halfHeight = height / 2.0f
                  let centerOffset = Vector2(halfWidth, halfHeight)

                  let topLeftLocal = Vector2(-halfWidth, -halfHeight)
                  let topRightLocal = Vector2(halfWidth, -halfHeight)
                  let bottomRightLocal = Vector2(halfWidth, halfHeight)
                  let bottomLeftLocal = Vector2(-halfWidth, halfHeight)

                  let topLeftUnrotated = topLeftLocal + centerOffset
                  let topRightUnrotated = topRightLocal + centerOffset
                  let bottomRightUnrotated = bottomRightLocal + centerOffset
                  let bottomLeftUnrotated = bottomLeftLocal + centerOffset

                  let topLeftRotated = rotate topLeftUnrotated radians
                  let topRightRotated = rotate topRightUnrotated radians
                  let bottomRightRotated = rotate bottomRightUnrotated radians
                  let bottomLeftRotated = rotate bottomLeftUnrotated radians

                  let p1 = topLeftRotated + position
                  let p2 = topRightRotated + position
                  let p3 = bottomRightRotated + position
                  let p4 = bottomLeftRotated + position

                  drawLine sb px p1 p2 color
                  drawLine sb px p2 p3 color
                  drawLine sb px p3 p4 color
                  drawLine sb px p4 p1 color
            | ValueNone -> ()

          | DrawEntityBounds position ->
            match pixel with
            | ValueSome px ->
              let poly = Spatial.getEntityPolygon position
              drawPolygon sb px poly Vector2.Zero 0.0f Color.Red
            | ValueNone -> ()

          | DrawSpatialGrid grid ->
            match pixel with
            | ValueSome px ->
              for (cell, entities) in grid |> HashMap.toSeq do
                let cellSize = Core.Constants.Collision.GridCellSize
                let x = float32 cell.X * cellSize
                let y = float32 cell.Y * cellSize

                let rect =
                  Microsoft.Xna.Framework.Rectangle(
                    int x,
                    int y,
                    int cellSize,
                    int cellSize
                  )

                let color =
                  if entities.Count > 0 then Color.Red else Color.Gray * 0.5f

                drawLine sb px (Vector2(x, y)) (Vector2(x + cellSize, y)) color

                drawLine
                  sb
                  px
                  (Vector2(x + cellSize, y))
                  (Vector2(x + cellSize, y + cellSize))
                  color

                drawLine
                  sb
                  px
                  (Vector2(x + cellSize, y + cellSize))
                  (Vector2(x, y + cellSize))
                  color

                drawLine sb px (Vector2(x, y + cellSize)) (Vector2(x, y)) color

                if entities.Count > 0 then
                  let text = $"{entities.Count}"
                  let textPos = Vector2(x + 5.0f, y + 5.0f)
                  sb.DrawString(hudFont, text, textPos, Color.White)
            | ValueNone -> ()

          | DrawCone(origin, direction, angle, length, color) ->
            match pixel, mapStore.tryFind mapKey with
            | ValueSome px, ValueSome mapDef ->
              // ISOMETRIC CONE VISUALIZATION
              // 1. Convert to Grid Space
              let originGrid =
                Spatial.Isometric.screenToGrid mapDef (origin.X, origin.Y)

              let originPlusDir = origin + direction

              let originPlusDirGrid =
                Spatial.Isometric.screenToGrid
                  mapDef
                  (originPlusDir.X, originPlusDir.Y)

              let directionGrid =
                Vector2.Normalize(originPlusDirGrid - originGrid)

              // 2. Scale length to grid units
              let lengthGrid = length * 1.41421356f / float32 mapDef.TileWidth

              // 3. Calculate Cone Points in Grid Space
              let halfAngleRad = MathHelper.ToRadians(angle / 2.0f)

              let baseAngle =
                float32(
                  Math.Atan2(float directionGrid.Y, float directionGrid.X)
                )

              let leftAngle = baseAngle - halfAngleRad
              let rightAngle = baseAngle + halfAngleRad

              let leftPointGrid =
                originGrid
                + Vector2(
                  lengthGrid * float32(Math.Cos(float leftAngle)),
                  lengthGrid * float32(Math.Sin(float leftAngle))
                )

              let rightPointGrid =
                originGrid
                + Vector2(
                  lengthGrid * float32(Math.Cos(float rightAngle)),
                  lengthGrid * float32(Math.Sin(float rightAngle))
                )

              // 4. Convert Points back to Screen Space
              let leftPointScreen =
                Spatial.Isometric.gridToScreen
                  mapDef
                  (leftPointGrid.X, leftPointGrid.Y)

              let rightPointScreen =
                Spatial.Isometric.gridToScreen
                  mapDef
                  (rightPointGrid.X, rightPointGrid.Y)

              drawLine sb px origin leftPointScreen (color * 0.3f)
              drawLine sb px origin rightPointScreen (color * 0.3f)

              // Draw Arc
              let segments = 10
              let angleStep = (rightAngle - leftAngle) / float32 segments

              for i in 0 .. segments - 1 do
                let a1 = leftAngle + float32 i * angleStep
                let a2 = leftAngle + float32(i + 1) * angleStep

                let p1Grid =
                  originGrid
                  + Vector2(
                    lengthGrid * float32(Math.Cos(float a1)),
                    lengthGrid * float32(Math.Sin(float a1))
                  )

                let p2Grid =
                  originGrid
                  + Vector2(
                    lengthGrid * float32(Math.Cos(float a2)),
                    lengthGrid * float32(Math.Sin(float a2))
                  )

                let p1Screen =
                  Spatial.Isometric.gridToScreen mapDef (p1Grid.X, p1Grid.Y)

                let p2Screen =
                  Spatial.Isometric.gridToScreen mapDef (p2Grid.X, p2Grid.Y)

                drawLine sb px p1Screen p2Screen (color * 0.3f)

            | ValueSome px, ValueNone ->
              drawCone sb px origin direction angle length (color * 0.3f)
            | _ -> ()

          | DrawLineShape(start, end', width, color) ->
            match pixel, mapStore.tryFind mapKey with
            | ValueSome px, ValueSome mapDef ->
              // ISOMETRIC LINE VISUALIZATION
              let startGrid =
                Spatial.Isometric.screenToGrid mapDef (start.X, start.Y)

              let endGrid =
                Spatial.Isometric.screenToGrid mapDef (end'.X, end'.Y)

              let widthGrid = width * 1.41421356f / float32 mapDef.TileWidth

              let edge = endGrid - startGrid
              let length = edge.Length()

              if length > 0.0f then
                let direction = Vector2.Normalize edge

                let perpendicular =
                  Vector2(-direction.Y, direction.X) * (widthGrid / 2.0f)

                let p1Grid = startGrid + perpendicular
                let p2Grid = startGrid - perpendicular
                let p3Grid = endGrid - perpendicular
                let p4Grid = endGrid + perpendicular

                let p1Screen =
                  Spatial.Isometric.gridToScreen mapDef (p1Grid.X, p1Grid.Y)

                let p2Screen =
                  Spatial.Isometric.gridToScreen mapDef (p2Grid.X, p2Grid.Y)

                let p3Screen =
                  Spatial.Isometric.gridToScreen mapDef (p3Grid.X, p3Grid.Y)

                let p4Screen =
                  Spatial.Isometric.gridToScreen mapDef (p4Grid.X, p4Grid.Y)

                drawLine sb px p1Screen p2Screen (color * 0.3f)
                drawLine sb px p2Screen p3Screen (color * 0.3f)
                drawLine sb px p3Screen p4Screen (color * 0.3f)
                drawLine sb px p4Screen p1Screen (color * 0.3f)

            | ValueSome px, ValueNone ->
              drawLineShape sb px start end' width (color * 0.3f)
            | _ -> ()

        for struct (cmd, _) in transientCommands do
          match cmd with
          | DrawCone(origin, direction, angle, length, color) ->
            match pixel, mapStore.tryFind mapKey with
            | ValueSome px, ValueSome mapDef ->
              // ISOMETRIC CONE VISUALIZATION (Transient)
              let originGrid =
                Spatial.Isometric.screenToGrid mapDef (origin.X, origin.Y)

              let originPlusDir = origin + direction

              let originPlusDirGrid =
                Spatial.Isometric.screenToGrid
                  mapDef
                  (originPlusDir.X, originPlusDir.Y)

              let directionGrid =
                Vector2.Normalize(originPlusDirGrid - originGrid)

              let lengthGrid = length * 1.41421356f / float32 mapDef.TileWidth

              let halfAngleRad = MathHelper.ToRadians(angle / 2.0f)

              let baseAngle =
                float32(
                  Math.Atan2(float directionGrid.Y, float directionGrid.X)
                )

              let leftAngle = baseAngle - halfAngleRad
              let rightAngle = baseAngle + halfAngleRad

              let leftPointGrid =
                originGrid
                + Vector2(
                  lengthGrid * float32(Math.Cos(float leftAngle)),
                  lengthGrid * float32(Math.Sin(float leftAngle))
                )

              let rightPointGrid =
                originGrid
                + Vector2(
                  lengthGrid * float32(Math.Cos(float rightAngle)),
                  lengthGrid * float32(Math.Sin(float rightAngle))
                )

              let leftPointScreen =
                Spatial.Isometric.gridToScreen
                  mapDef
                  (leftPointGrid.X, leftPointGrid.Y)

              let rightPointScreen =
                Spatial.Isometric.gridToScreen
                  mapDef
                  (rightPointGrid.X, rightPointGrid.Y)

              drawLine sb px origin leftPointScreen (color * 0.5f)
              drawLine sb px origin rightPointScreen (color * 0.5f)

              let segments = 10
              let angleStep = (rightAngle - leftAngle) / float32 segments

              for i in 0 .. segments - 1 do
                let a1 = leftAngle + float32 i * angleStep
                let a2 = leftAngle + float32(i + 1) * angleStep

                let p1Grid =
                  originGrid
                  + Vector2(
                    lengthGrid * float32(Math.Cos(float a1)),
                    lengthGrid * float32(Math.Sin(float a1))
                  )

                let p2Grid =
                  originGrid
                  + Vector2(
                    lengthGrid * float32(Math.Cos(float a2)),
                    lengthGrid * float32(Math.Sin(float a2))
                  )

                let p1Screen =
                  Spatial.Isometric.gridToScreen mapDef (p1Grid.X, p1Grid.Y)

                let p2Screen =
                  Spatial.Isometric.gridToScreen mapDef (p2Grid.X, p2Grid.Y)

                drawLine sb px p1Screen p2Screen (color * 0.5f)

            | ValueSome px, ValueNone ->
              drawCone sb px origin direction angle length (color * 0.5f)
            | _ -> ()

          | DrawLineShape(start, end', width, color) ->
            match pixel, mapStore.tryFind mapKey with
            | ValueSome px, ValueSome mapDef ->
              // ISOMETRIC LINE VISUALIZATION (Transient)
              let startGrid =
                Spatial.Isometric.screenToGrid mapDef (start.X, start.Y)

              let endGrid =
                Spatial.Isometric.screenToGrid mapDef (end'.X, end'.Y)

              let widthGrid = width * 1.41421356f / float32 mapDef.TileWidth

              let edge = endGrid - startGrid
              let length = edge.Length()

              if length > 0.0f then
                let direction = Vector2.Normalize edge

                let perpendicular =
                  Vector2(-direction.Y, direction.X) * (widthGrid / 2.0f)

                let p1Grid = startGrid + perpendicular
                let p2Grid = startGrid - perpendicular
                let p3Grid = endGrid - perpendicular
                let p4Grid = endGrid + perpendicular

                let p1Screen =
                  Spatial.Isometric.gridToScreen mapDef (p1Grid.X, p1Grid.Y)

                let p2Screen =
                  Spatial.Isometric.gridToScreen mapDef (p2Grid.X, p2Grid.Y)

                let p3Screen =
                  Spatial.Isometric.gridToScreen mapDef (p3Grid.X, p3Grid.Y)

                let p4Screen =
                  Spatial.Isometric.gridToScreen mapDef (p4Grid.X, p4Grid.Y)

                drawLine sb px p1Screen p2Screen (color * 0.5f)
                drawLine sb px p2Screen p3Screen (color * 0.5f)
                drawLine sb px p3Screen p4Screen (color * 0.5f)
                drawLine sb px p4Screen p1Screen (color * 0.5f)

            | ValueSome px, ValueNone ->
              drawLineShape sb px start end' width (color * 0.5f)
            | _ -> ()

        sb.End()

      // Calculate FPS
      frameCount <- frameCount + 1

      if
        gameTime.TotalGameTime.TotalSeconds - lastFPSTime.TotalSeconds >= 1.0
      then
        fps <- float32 frameCount
        frameCount <- 0
        lastFPSTime <- gameTime.TotalGameTime

      // Count total entities for culling stats
      totalEntities <- AMap.count projections.UpdatedPositions |> AVal.force

      // Render performance stats overlay at top-left of screen
      let screenTransform = Matrix.Identity
      sb.Begin(transformMatrix = screenTransform)

      let statsText =
        System.Text
          .StringBuilder()
          .AppendLine($"FPS: %.1f{fps}")
          .AppendLine($"Entities: {totalEntities}")
          .AppendLine("Culling: 0% (TODO)")
          .ToString()

      sb.DrawString(hudFont, statsText, Vector2(10.0f, 10.0f), Color.White)
      sb.End()

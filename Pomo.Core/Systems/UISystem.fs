namespace Pomo.Core.Systems

open System
open Microsoft.Xna.Framework
open Myra
open Myra.Graphics2D
open Myra.Graphics2D.UI
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Events
open Pomo.Core.Domain.Map
open Pomo.Core.Systems
open Pomo.Core.Environment.Patterns
open Pomo.Core.EventBus
open Pomo.Core.Stores

module MainMenuUI =
  let build
    (game: Game)
    (eventBus: EventBus)
    (mapStore: MapStore)
    (random: Random)
    (playerId: Guid<EntityId>)
    =
    let panel = new VerticalStackPanel(Spacing = 16)
    panel.HorizontalAlignment <- HorizontalAlignment.Center
    panel.VerticalAlignment <- VerticalAlignment.Center
    panel.Widgets.Add(new Label(Text = "Selection Screen"))

    let addButton text onClick =
      let b = new Button(Width = 180)
      b.Content <- new Label(Text = text)
      b.Click.Add(onClick)
      panel.Widgets.Add(b)

    let getRandomPointInPolygon (poly: IndexList<Vector2>) (random: Random) =
      if poly.IsEmpty then
        Vector2.Zero
      else
        // Calculate bounding box
        let minX = poly |> IndexList.map(fun v -> v.X) |> Seq.min
        let maxX = poly |> IndexList.map(fun v -> v.X) |> Seq.max
        let minY = poly |> IndexList.map(fun v -> v.Y) |> Seq.min
        let maxY = poly |> IndexList.map(fun v -> v.Y) |> Seq.max

        let isPointInPolygon(p: Vector2) =
          let mutable inside = false
          let count = poly.Count
          let mutable j = count - 1

          for i = 0 to count - 1 do
            let pi = poly[i]
            let pj = poly[j]

            if
              ((pi.Y > p.Y) <> (pj.Y > p.Y))
              && (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
            then
              inside <- not inside

            j <- i

          inside

        // Rejection sampling (try up to 20 times)
        let rec findPoint attempts =
          if attempts <= 0 then
            // Fallback to first point or center of bbox
            Vector2((minX + maxX) / 2.0f, (minY + maxY) / 2.0f)
          else
            let x = minX + float32(random.NextDouble()) * (maxX - minX)
            let y = minY + float32(random.NextDouble()) * (maxY - minY)
            let p = Vector2(x, y)

            if isPointInPolygon p then p else findPoint(attempts - 1)

        findPoint 20

    addButton "New Game" (fun _ ->
      // Find spawn point in Lobby
      let mapDef = mapStore.find "Lobby"

      let spawnCandidates =
        mapDef.ObjectGroups
        |> IndexList.collect(fun group ->
          group.Objects
          |> IndexList.choose(fun obj ->
            match obj.Type with
            | ValueSome MapObjectType.Spawn ->
              let isPlayerSpawn =
                obj.Properties
                |> HashMap.tryFindV "PlayerSpawn"
                |> ValueOption.map(fun v -> v.ToLower() = "true")
                |> ValueOption.defaultValue false

              let pos =
                match obj.Points with
                | ValueSome points when not points.IsEmpty ->
                  // Use random point in polygon relative to object position
                  let offset = getRandomPointInPolygon points random
                  Vector2(obj.X + offset.X, obj.Y + offset.Y)
                | _ ->
                  // Otherwise use object position
                  Vector2(obj.X, obj.Y)

              Some(isPlayerSpawn, pos)
            | _ -> None))

      // Prioritize explicit player spawn, otherwise take the first spawn found
      let spawnPos =
        spawnCandidates
        |> IndexList.tryFind(fun _ (isPlayer, _) -> isPlayer)
        |> Option.orElse(IndexList.tryAt 0 spawnCandidates)
        |> Option.map snd
        |> Option.defaultValue Vector2.Zero

      let intent: SystemCommunications.SpawnEntityIntent = {
        EntityId = playerId
        Type = SystemCommunications.SpawnType.Player 0
        Position = spawnPos
      }

      eventBus.Publish intent)

    addButton "Settings" (fun _ -> ()) // Placeholder action
    addButton "Exit" (fun _ -> game.Exit())

    panel

module GameplayUI =
  let build (game: Game) (eventBus: EventBus) (playerId: Guid<EntityId>) =
    let panel = new Panel()

    let topPanel = new HorizontalStackPanel(Spacing = 8)
    topPanel.HorizontalAlignment <- HorizontalAlignment.Right
    topPanel.VerticalAlignment <- VerticalAlignment.Top
    topPanel.Padding <- Thickness(10)

    let backButton = new Button()
    backButton.Content <- new Label(Text = "Back to Main Menu")

    backButton.Click.Add(fun _ ->
      eventBus.Publish(EntityLifecycle(Removed playerId)))

    topPanel.Widgets.Add(backButton)
    panel.Widgets.Add(topPanel)

    panel

type UISystem
  (
    game: Game,
    env: Pomo.Core.Environment.PomoEnvironment,
    playerId: Guid<EntityId>
  ) =
  inherit DrawableGameComponent(game)

  let mutable desktop: Desktop = null
  let (Core core) = env.CoreServices
  let (Stores stores) = env.StoreServices

  let subs = new System.Reactive.Disposables.CompositeDisposable()

  override this.LoadContent() =
    MyraEnvironment.Game <- game

    // Initial UI is Main Menu
    let root =
      MainMenuUI.build game core.EventBus stores.MapStore core.Random playerId

    desktop <- new Desktop(Root = root)

    // Listen for Player Lifecycle events to switch UI
    core.EventBus.GetObservableFor<StateChangeEvent>()
    |> Observable.subscribe(fun event ->
      match event with
      | EntityLifecycle(Created snapshot) when snapshot.Id = playerId ->
        // Player created -> Switch to Gameplay UI
        let root = GameplayUI.build game core.EventBus playerId
        desktop.Root <- root
      | EntityLifecycle(Removed id) when id = playerId ->
        // Player removed -> Switch to Main Menu
        let root =
          MainMenuUI.build
            game
            core.EventBus
            stores.MapStore
            core.Random
            playerId

        desktop.Root <- root
      | _ -> ())
    |> subs.Add

  override this.Update(gameTime) =
    if not(isNull desktop) then
      core.UIService.SetMouseOverUI desktop.IsMouseOverGUI

    base.Update(gameTime)

  override this.Draw(gameTime) =
    if not(isNull desktop) then
      desktop.Render()

  override this.Dispose(disposing) =
    if disposing then
      subs.Dispose()

    base.Dispose(disposing)

namespace Pomo.Core.Editor

open System
open System.IO
open Microsoft.Xna.Framework
open System.Reactive.Disposables
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Scenes
open Pomo.Core.Systems
open Pomo.Core.Environment

module EditorScene =

  let create
    (game: Game)
    (stores: StoreServices)
    (uiService: IUIService)
    (sceneTransitionSubject: IObserver<Scene>)
    (mapKey: string voption)
    : struct (IGameComponent list * IDisposable) =

    let subs = new CompositeDisposable()

    let defaultMapKey = "NewMap"

    let tryLoadOrEmpty(key: string) =
      let path = $"Content/CustomMaps/{key}.json"

      match BlockMapLoader.load path with
      | Ok map -> map
      | Error _ -> BlockMap.createEmpty key 16 8 16

    let blockMap =
      match mapKey with
      | ValueSome key -> tryLoadOrEmpty key
      | ValueNone ->
        let map = tryLoadOrEmpty defaultMapKey

        if map.MapKey.IsSome then
          map
        else
          { map with MapKey = ValueSome map.Key }

    let state = EditorState.create blockMap

    if blockMap.Palette.Count = 0 then
      let addBlock id name model cat =
        blockMap.Palette.Add(
          id,
          {
            Id = id
            Name = name
            Model = model
            Category = cat
            CollisionType = BlockMap.Box
            Effect = ValueNone
          }
        )

      addBlock 1<BlockTypeId> "Stone" "Tiles/kaykit_blocks/stone" "Basic"
      addBlock 2<BlockTypeId> "Dirt" "Tiles/kaykit_blocks/dirt" "Basic"
      addBlock 3<BlockTypeId> "Grass" "Tiles/kaykit_blocks/grass" "Basic"
      addBlock 4<BlockTypeId> "Water" "Tiles/kaykit_blocks/water" "Basic"
      addBlock 5<BlockTypeId> "Wood" "Tiles/kaykit_blocks/wood" "Basic"

      addBlock
        6<BlockTypeId>
        "Dark Stone"
        "Tiles/kaykit_blocks/stone_dark"
        "Basic"

      transact(fun () ->
        state.SelectedBlockType.Value <- ValueSome 1<BlockTypeId>)

    // Use Square PPU for True 3D (1:1 Aspect Ratio)
    let pixelsPerUnit = Vector2(64f, 64f)

    let camera = EditorCameraState()
    camera.Zoom <- 2.0f // Initial zoom adjusted for 1:1 scale (blocks look taller now)

    // P key playtest callback
    let onPlaytest() =
      let currentMap = state.BlockMap |> AVal.force

      let path = $"Content/CustomMaps/{currentMap.Key}.json"
      BlockMapLoader.save path currentMap |> ignore

      sceneTransitionSubject.OnNext(BlockMapPlaytest currentMap)

    let inputSystem =
      EditorInput.createSystem
        game
        state
        camera
        uiService
        pixelsPerUnit
        onPlaytest

    let renderSystem =
      EditorRender.createSystem game state camera pixelsPerUnit game.Content

    let uiSystem = EditorUI.createSystem game state uiService camera

    let components: IGameComponent list = [
      inputSystem
      renderSystem
      uiSystem
    ]

    let disposable =
      { new IDisposable with
          member _.Dispose() = subs.Dispose()
      }

    struct (components, disposable)

namespace Pomo.Core

open System
open System.IO
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Control.Reactive
open System.Reactive.Disposables
open Myra.Graphics2D.UI

open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Scenes
open Pomo.Core.Domain.UI
open Pomo.Core.Stores
open Pomo.Core.Environment
open Pomo.Core.Systems

/// Global application scope containing stores and MonoGame services
type GlobalScope = {
  Stores: StoreServices
  MonoGame: MonoGameServices
  Random: Random
  UIService: IUIService
  HUDService: IHUDService
}

module CompositionRoot =

  /// Subject for scene transitions - used by scenes to trigger transitions
  let sceneTransitionSubject = Subject<Scene>.broadcast

  /// Creates the global scope with all stores and services
  let createGlobalScope(game: Game) =
    let deserializer = Serialization.create()
    let skillStore = Skill.create(JsonFileLoader.readSkills deserializer)
    let itemStore = Item.create(JsonFileLoader.readItems deserializer)

    let aiArchetypeStore =
      AIArchetype.create(JsonFileLoader.readAIArchetypes deserializer)

    let aiFamilyStore =
      AIFamily.create(JsonFileLoader.readAIFamilies deserializer)

    let aiEntityStore = AIEntity.create(JsonFileLoader.readAIEntities)

    let decisionTreeStore =
      DecisionTree.create(JsonFileLoader.readDecisionTrees)

    let modelStore = Model.create(JsonFileLoader.readModels deserializer)

    let animationStore =
      Animation.create(JsonFileLoader.readAnimations deserializer)

    let particleStore =
      Particle.create(JsonFileLoader.readParticles deserializer)

    let stores =
      { new StoreServices with
          member _.SkillStore = skillStore
          member _.ItemStore = itemStore
          member _.AIArchetypeStore = aiArchetypeStore
          member _.AIFamilyStore = aiFamilyStore
          member _.AIEntityStore = aiEntityStore
          member _.DecisionTreeStore = decisionTreeStore
          member _.ModelStore = modelStore
          member _.AnimationStore = animationStore
          member _.ParticleStore = particleStore
      }

    let monoGame =
      { new MonoGameServices with
          member _.GraphicsDevice = game.GraphicsDevice
          member _.Content = game.Content
      }

    let uiService = UIService.create()
    let hudService = HUDService.create "Content/HUDConfig.json"

    {
      Stores = stores
      MonoGame = monoGame
      Random = Random.Shared
      UIService = uiService
      HUDService = hudService
    }

  module SceneFactory =

    /// Creates the main menu scene
    let createMainMenu (game: Game) (scope: GlobalScope) =
      let mutable desktop: Desktop voption = ValueNone
      let subs = new CompositeDisposable()

      let publishGuiAction(action: GuiAction) =
        match action with
        | GuiAction.StartNewGame ->
          scope.HUDService.ShowLoadingOverlay()
          sceneTransitionSubject.OnNext(Scene.Gameplay("NewMap", ValueNone))
        | GuiAction.OpenMapEditor ->
          sceneTransitionSubject.OnNext(Scene.MapEditor ValueNone)
        | GuiAction.ExitGame -> game.Exit()
        | GuiAction.OpenSettings
        | GuiAction.BackToMainMenu
        | GuiAction.ToggleCharacterSheet
        | GuiAction.ToggleEquipment -> ()

      let uiComponent =
        { new DrawableGameComponent(game) with
            override _.LoadContent() =
              let root = Systems.MainMenuUI.build game publishGuiAction
              desktop <- ValueSome(new Desktop(Root = root))

            override _.Update(gameTime) =
              desktop
              |> ValueOption.iter(fun d ->
                scope.UIService.SetMouseOverUI d.IsMouseOverGUI)

            override _.Draw(gameTime) =
              desktop |> ValueOption.iter(fun d -> d.Render())
        }

      let disposable =
        { new IDisposable with
            member _.Dispose() =
              subs.Dispose()
              desktop |> ValueOption.iter(fun d -> d.Dispose())
        }

      struct ([ uiComponent :> IGameComponent ], disposable)

    /// Routes scene requests to appropriate factory
    let sceneLoader
      (game: Game)
      (scope: GlobalScope)
      (playerId: Guid<EntityId>)
      (scene: Scene)
      =
      let tryLoadBlockMap(mapKey: string) =
        let candidatePaths = [
          Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "CustomMaps",
            $"{mapKey}.json"
          )
          Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "CustomMaps",
            "NewMap.json"
          )
        ]

        let rec loop paths =
          match paths with
          | [] -> ValueNone
          | path :: rest ->
            match
              Systems.BlockMapLoader.load
                Systems.BlockMapLoader.Resolvers.runtime
                path
            with
            | Ok map -> ValueSome map
            | Error e ->
              printfn $"Error loading Map: {path} - {e}"
              loop rest

        loop candidatePaths

      match scene with
      | Scene.MainMenu -> createMainMenu game scope
      | Scene.Gameplay(mapKey, targetSpawn) ->
        match tryLoadBlockMap mapKey with
        | ValueSome blockMap ->
          Scenes.GameplayScene.create
            game
            scope.Stores
            scope.MonoGame
            scope.Random
            scope.UIService
            scope.HUDService
            sceneTransitionSubject
            playerId
            blockMap
        | ValueNone -> failwith $"Failed to load BlockMap for key '{mapKey}'"
      | Scene.MapEditor mapKey ->
        Pomo.Core.Editor.EditorScene.create
          game
          scope.Stores
          scope.UIService
          sceneTransitionSubject
          mapKey
      | Scene.BlockMapPlaytest blockMap ->
        Scenes.BlockMapScene.create
          game
          scope.Stores
          scope.MonoGame
          scope.Random
          scope.UIService
          scope.HUDService
          sceneTransitionSubject
          playerId
          blockMap

namespace Pomo.Core.MiboApp

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics3D

module Program =
  open Pomo.Core.Environment

  let private update
    (env: AppEnv)
    (msg: AppMsg)
    (model: AppModel)
    : struct (AppModel * Cmd<AppMsg>) =
    match msg with
    | Tick gt ->
      match model.CurrentScene with
      | Editor state ->
        let state = Editor.update env gt state
        { CurrentScene = Editor state }, Cmd.none
      | MainMenu -> model, Cmd.none
    | TransitionTo scene -> { CurrentScene = scene }, Cmd.none

  let private view
    (env: AppEnv)
    (ctx: GameContext)
    (model: AppModel)
    (buffer: RenderBuffer<RenderCmd3D>)
    =
    match model.CurrentScene with
    | Editor state -> Editor.view env ctx state buffer
    | MainMenu ->
      // TODO: Render Main Menu UI via Service
      env.UI.Rebuild(MainMenu)
      env.UI.Update()
      env.UI.Render()

      Draw3D.viewport ctx.GraphicsDevice.Viewport buffer
      Draw3D.clear (ValueSome Color.Black) true buffer

  let private init
    (env: AppEnv)
    (ctx: GameContext)
    : struct (AppModel * Cmd<AppMsg>) =
    let viewport = ctx.GraphicsDevice.Viewport
    // ensure myra is initialized
    Myra.MyraEnvironment.Game <- ctx.Game
    env.UI.Initialize ctx.Game

    let editorState =
      match Editor.loadMap viewport "Content/CustomMaps/NewMap.json" with
      | Ok state -> state
      | Error err ->
        printfn $"[MiboApp] Failed to load map: {err}"
        Editor.createEmpty viewport

    { CurrentScene = Editor editorState }, Cmd.none

  let createCoreServices() =
    let deserializer = Pomo.Core.Serialization.create()

    let skillStore =
      Pomo.Core.Stores.Skill.create(
        Pomo.Core.JsonFileLoader.readSkills deserializer
      )

    let itemStore =
      Pomo.Core.Stores.Item.create(
        Pomo.Core.JsonFileLoader.readItems deserializer
      )

    let aiArchetypeStore =
      Pomo.Core.Stores.AIArchetype.create(
        Pomo.Core.JsonFileLoader.readAIArchetypes deserializer
      )

    let aiFamilyStore =
      Pomo.Core.Stores.AIFamily.create(
        Pomo.Core.JsonFileLoader.readAIFamilies deserializer
      )

    let aiEntityStore =
      Pomo.Core.Stores.AIEntity.create(Pomo.Core.JsonFileLoader.readAIEntities)

    let decisionTreeStore =
      Pomo.Core.Stores.DecisionTree.create(
        Pomo.Core.JsonFileLoader.readDecisionTrees
      )

    let modelStore =
      Pomo.Core.Stores.Model.create(
        Pomo.Core.JsonFileLoader.readModels deserializer
      )

    let animationStore =
      Pomo.Core.Stores.Animation.create(
        Pomo.Core.JsonFileLoader.readAnimations deserializer
      )

    let particleStore =
      Pomo.Core.Stores.Particle.create(
        Pomo.Core.JsonFileLoader.readParticles deserializer
      )

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

    let uiService = Pomo.Core.Systems.UIService.create()

    let hudService =
      Pomo.Core.Systems.HUDService.create "Content/HUDConfig.json"

    {
      Stores = stores
      Random = System.Random.Shared
      UIService = uiService
      HUDService = hudService
    }

  let create() =
    let coreServices = createCoreServices()
    let uiService = UIService.create(coreServices.UIService)

    let env = { Core = coreServices; UI = uiService }

    // Partial application of Env
    let init = init env
    let update = update env
    let view = view env

    Program.mkProgram init update
    |> Program.withInput
    |> Program.withAssets
    |> Program.withRenderer(Batch3DRenderer.create view)
    |> Program.withTick Tick

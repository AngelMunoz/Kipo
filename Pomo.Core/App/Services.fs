namespace Pomo.Core.App

open System
open Pomo.Core.Stores
open Pomo.Core.Systems
open Pomo.Core.Serialization
open Pomo.Core.Environment
open Pomo.Core.Domain.UI

/// Application services that are independent of MonoGame's Game instance.
/// This allows initialization even before the Game is started.
type AppServices = {
  Stores: StoreServices
  Random: Random
  UIService: IUIService
  HUDService: IHUDService
}

module AppServices =
  open Pomo.Core

  /// Creates and initializes all pure application services.
  let create() =
    let deserializer = Pomo.Core.Serialization.create()

    // Load all the persistent data stores
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

    let uiService = UIService.create()
    let hudService = HUDService.create "Content/HUDConfig.json"

    {
      Stores = stores
      Random = Random.Shared
      UIService = uiService
      HUDService = hudService
    }

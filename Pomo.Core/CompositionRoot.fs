namespace Pomo.Core

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Content
open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.EventBus
open Pomo.Core.Domain
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Camera
open Pomo.Core.Stores
open Pomo.Core.Projections
open Pomo.Core.Systems.Targeting
open Pomo.Core.Systems.Effects
open Pomo.Core.Serialization
open Pomo.Core.Systems

open Pomo.Core.Environment

module CompositionRoot =

  let create(game: Game, playerId: Guid<EntityId>) =
    let eventBus = new EventBus()
    let random = Random.Shared
    let struct (mutableWorld, worldView) = World.create random

    let deserializer = Serialization.create()
    let skillStore = Skill.create(JsonFileLoader.readSkills deserializer)
    let itemStore = Item.create(JsonFileLoader.readItems deserializer)

    let aiArchetypeStore =
      AIArchetype.create(JsonFileLoader.readAIArchetypes deserializer)

    let mapStore = Map.create MapLoader.loadMap [ "Content/Maps/Proto.xml" ]

    let projections = Projections.create(itemStore, worldView)

    let cameraService =
      CameraSystem.create(game, projections, Array.singleton playerId)

    let targetingService = Targeting.create(eventBus, skillStore, projections)

    let effectApplication = EffectApplication.create(worldView, eventBus)

    let actionHandler =
      ActionHandler.create(
        worldView,
        eventBus,
        targetingService,
        projections,
        cameraService,
        playerId
      )

    let navigationService =
      Navigation.create(eventBus, mapStore, "Proto1", worldView)

    let inventoryService = Inventory.create(eventBus, itemStore, worldView)

    let equipmentService = Equipment.create worldView eventBus

    let pomoEnv =
      { new PomoEnvironment with
          member _.CoreServices: CoreServices =
            { new CoreServices with
                member _.EventBus = eventBus
                member _.World = worldView
                member _.Random = random
            }

          member _.GameplayServices: GameplayServices =
            { new GameplayServices with
                member _.Projections = projections
                member _.TargetingService = targetingService
                member _.CameraService = cameraService
            }

          member _.ListenerServices: ListenerServices =
            { new ListenerServices with
                member _.EffectApplication = effectApplication
                member _.ActionHandler = actionHandler
                member _.NavigationService = navigationService
                member _.InventoryService = inventoryService
                member _.EquipmentService = equipmentService
            }

          member _.MonoGameServices: MonoGameServices =
            { new MonoGameServices with
                member _.GraphicsDevice = game.GraphicsDevice
                member _.Content = game.Content
            }

          member _.StoreServices: StoreServices =
            { new StoreServices with
                member _.SkillStore = skillStore
                member _.ItemStore = itemStore
                member _.MapStore = mapStore
                member _.AIArchetypeStore = aiArchetypeStore
            }
      }



    struct (pomoEnv, mutableWorld)

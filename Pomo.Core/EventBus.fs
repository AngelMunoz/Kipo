namespace Pomo.Core

open System
open System.Reactive.Subjects
open Pomo.Core.Domain.Events

module EventBus =
  /// Simple event bus using a single Subject for all GameEvents.
  /// All subscribers receive all events and filter as needed.
  type EventBus() =
    let subject = new Subject<GameEvent>()

    member _.Publish(event: GameEvent) = subject.OnNext event

    /// Get the observable stream of all game events.
    /// Consumers should filter for the events they care about.
    member _.Observable: IObservable<GameEvent> = subject

    interface IDisposable with
      member _.Dispose() = subject.Dispose()

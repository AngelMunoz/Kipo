namespace Pomo.Core

open System
open System.Reactive.Subjects

module EventBus =
  [<AbstractClass; Sealed>]
  type Events<'T> =
    static let subject = new Subject<'T>()
    static member Publish(event: 'T) = subject.OnNext event
    static member Subscribe() : IObservable<'T> = subject

  type EventBus() =

    member _.Publish<'T>([<ParamArray>] events: 'T[]) =
      for event in events do
        Events<'T>.Publish event

    member _.GetObservableFor<'T>() = Events<'T>.Subscribe()

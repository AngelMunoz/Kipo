namespace Pomo.Core

open System
open Pomo.Core.Domain.Events

module EventBus =
  open System.Diagnostics

  /// Ring buffer-based event bus that implements IObservable directly.
  /// Eliminates Rx Subject dependency while maintaining Observable interface.
  type RingEventBus(initialCapacity: int) =
    let mutable buffer: GameEvent[] = Array.zeroCreate initialCapacity
    let mutable capacity = initialCapacity
    let mutable head = 0
    let mutable count = 0
    let mutable disposed = false
    let subscribers = ResizeArray<IObserver<GameEvent>>()

    /// Publish an event to the ring buffer.
    /// Near-zero allocation - just stores the struct in the array.
    member _.Publish(event: GameEvent) =
      if disposed then
        ()
      else

      if count >= capacity then
        // Auto-grow: double capacity
        let newCapacity = capacity * 2
#if DEBUG
        Debug.WriteLine
          $"[EventBus] Ring buffer grew from {capacity} to {newCapacity}"
#endif
        let newBuffer = Array.zeroCreate newCapacity
        // Copy with wrap-around handling
        let firstPart = capacity - head
        Array.blit buffer head newBuffer 0 firstPart
        Array.blit buffer 0 newBuffer firstPart head
        buffer <- newBuffer
        capacity <- newCapacity
        head <- 0

      let index = (head + count) % capacity
      buffer[index] <- event
      count <- count + 1

    /// Flush all buffered events to subscribers.
    /// Uses Span for cache-friendly iteration.
    /// Call once per frame in the game loop.
    member _.FlushToObservable() =
      if disposed then
        ()
      else

      while count > 0 do
        // Process contiguous segment from head to end of buffer or count
        let segmentLength = min count (capacity - head)
        let span = buffer.AsSpan(head, segmentLength)

        for i = 0 to span.Length - 1 do
          let evt = span[i]
          span[i] <- Unchecked.defaultof<_> // Clear reference for GC
          // Notify all subscribers
          for sub in subscribers do
            sub.OnNext(evt)

        head <- (head + segmentLength) % capacity
        count <- count - segmentLength

    /// Get the observable stream of all game events.
    /// Returns self since we implement IObservable directly.
    member this.Observable: IObservable<GameEvent> = this

    interface IObservable<GameEvent> with
      member _.Subscribe(observer: IObserver<GameEvent>) =
        subscribers.Add(observer)
        // Return disposable that removes the observer
        { new IDisposable with
            member _.Dispose() = subscribers.Remove(observer) |> ignore
        }

    interface IDisposable with
      member _.Dispose() =
        // Copy and clear first to avoid modification during enumeration
        let subs = subscribers.ToArray()
        subscribers.Clear()
        // Release buffer for GC
        disposed <- true
        buffer <- Array.empty
        capacity <- 0
        count <- 0
        head <- 0
        // Notify completion to all subscribers
        for sub in subs do
          sub.OnCompleted()

  /// Default EventBus with 1024 initial capacity.
  /// Large enough to handle scene initialization without growing.
  type EventBus() =
    inherit RingEventBus(1024)

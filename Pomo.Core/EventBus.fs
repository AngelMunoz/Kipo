namespace Pomo.Core

open System
open Pomo.Core.Domain.Events

module EventBus =
  /// Ring buffer-based event bus that implements IObservable directly.
  /// Eliminates Rx Subject dependency while maintaining Observable interface.
  type RingEventBus(initialCapacity: int) =
    let mutable buffer: GameEvent[] = Array.zeroCreate initialCapacity
    let mutable head = 0
    let mutable count = 0
    let subscribers = ResizeArray<IObserver<GameEvent>>()

    /// Publish an event to the ring buffer.
    /// Near-zero allocation - just stores the struct in the array.
    member _.Publish(event: GameEvent) =
      if count >= buffer.Length then
        // Auto-grow: double capacity
        let newCapacity = buffer.Length * 2
#if DEBUG
        printfn
          $"[EventBus] Ring buffer grew from {buffer.Length} to {newCapacity}"
#endif
        let newBuffer = Array.zeroCreate newCapacity
        // Copy with wrap-around handling
        let firstPart = buffer.Length - head
        Array.blit buffer head newBuffer 0 firstPart
        Array.blit buffer 0 newBuffer firstPart head
        buffer <- newBuffer
        head <- 0

      let index = (head + count) % buffer.Length
      buffer[index] <- event
      count <- count + 1

    /// Flush all buffered events to subscribers.
    /// Uses Span for cache-friendly iteration.
    /// Call once per frame in the game loop.
    member _.FlushToObservable() =
      while count > 0 do
        // Process contiguous segment from head to end of buffer or count
        let segmentLength = min count (buffer.Length - head)
        let span = buffer.AsSpan(head, segmentLength)

        for i = 0 to span.Length - 1 do
          let evt = span[i]
          span[i] <- Unchecked.defaultof<_> // Clear reference for GC
          // Notify all subscribers
          for sub in subscribers do
            sub.OnNext(evt)

        head <- (head + segmentLength) % buffer.Length
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
        // Notify completion to all subscribers
        for sub in subscribers do
          sub.OnCompleted()

        subscribers.Clear()

  /// Default EventBus with 1024 initial capacity.
  /// Suitable for most game scenarios; auto-grows if needed.
  type EventBus() =
    inherit RingEventBus(1024)

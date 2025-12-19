namespace Pomo.Core

open System
open Pomo.Core.Domain.Events

module EventBus =
  open System.Buffers

  type RingEventBus(initialCapacity: int) =
    let pool = ArrayPool<GameEvent>.Shared
    let mutable buffer = pool.Rent initialCapacity
    let mutable head = 0
    let mutable count = 0
    let mutable disposed = false
    let mutable lowUsageFrames = 0
    let subscribers = ResizeArray<IObserver<GameEvent>>()

    member _.Publish(event: GameEvent) =
      if disposed then
        ()
      else

      if count >= buffer.Length then
        let newSize = buffer.Length * 2
        let newBuffer = pool.Rent newSize
        let firstPart = buffer.Length - head
        Array.blit buffer head newBuffer 0 firstPart
        Array.blit buffer 0 newBuffer firstPart head
        pool.Return buffer
        buffer <- newBuffer
        head <- 0
        lowUsageFrames <- 0
#if DEBUG
        Console.WriteLine $"[EventBus] Ring buffer grew to {newSize}"
#endif

      let index = (head + count) % buffer.Length
      buffer[index] <- event
      count <- count + 1

    member _.FlushToObservable() =
      if disposed then
        ()
      else

      while count > 0 do
        let segmentLength = min count (buffer.Length - head)
        let span = buffer.AsSpan(head, segmentLength)

        for i = 0 to span.Length - 1 do
          let evt = span[i]

          for sub in subscribers do
            sub.OnNext(evt)

        head <- (head + segmentLength) % buffer.Length
        count <- count - segmentLength

      if buffer.Length > initialCapacity then
        lowUsageFrames <- lowUsageFrames + 1

        if lowUsageFrames > 60 then
          let smaller = pool.Rent(max initialCapacity (buffer.Length / 2))
          pool.Return buffer
          buffer <- smaller
          head <- 0
          lowUsageFrames <- 0
#if DEBUG
          Console.WriteLine $"[EventBus] Ring buffer shrunk to {buffer.Length}"
#endif
      else
        lowUsageFrames <- 0

    member this.Observable: IObservable<GameEvent> = this

    interface IObservable<GameEvent> with
      member _.Subscribe(observer: IObserver<GameEvent>) =
        subscribers.Add(observer)

        { new IDisposable with
            member _.Dispose() = subscribers.Remove(observer) |> ignore
        }

    interface IDisposable with
      member _.Dispose() =
        let subs = subscribers.ToArray()
        subscribers.Clear()
        disposed <- true
        pool.Return buffer
        buffer <- Array.empty
        count <- 0
        head <- 0

        for sub in subs do
          sub.OnCompleted()

  type EventBus() =
    inherit RingEventBus(512)

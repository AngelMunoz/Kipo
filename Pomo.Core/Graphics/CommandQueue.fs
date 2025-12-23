namespace Pomo.Core.Graphics

open System
open System.Buffers
open System.Collections.Generic

/// Interface for command queue operations
type ICommandQueue<'T when 'T: struct> =
  inherit IDisposable
  abstract Count: int
  abstract Add: inref<'T> -> unit
  abstract Clear: unit -> unit
  abstract Iterate: ('T -> unit) -> unit
  abstract Sort: IComparer<'T> -> unit
  abstract AsReadOnlySpan: unit -> ReadOnlySpan<'T>

module CommandQueue =
  let private shrinkThreshold = 120 // 2 seconds at 60fps

  /// Core add implementation - grows buffer if needed
  let inline private addImpl
    (buffer: byref<'T[]>)
    (count: byref<int>)
    (command: inref<'T>)
    =
    if count >= buffer.Length then
      let newBuffer = ArrayPool<'T>.Shared.Rent(buffer.Length * 2)
      Array.Copy(buffer, newBuffer, buffer.Length)
      ArrayPool<'T>.Shared.Return(buffer)
      buffer <- newBuffer

    buffer[count] <- command
    count <- count + 1

  /// Core clear implementation with auto-shrink logic
  let inline private clearImpl
    (initialCapacity: int)
    (buffer: byref<'T[]>)
    (count: byref<int>)
    (lastUsedCount: byref<int>)
    (lowUsageFrames: byref<int>)
    =
    lastUsedCount <- max lastUsedCount count
    count <- 0

    // Auto-shrink logic
    if lastUsedCount < buffer.Length / 4 then
      lowUsageFrames <- lowUsageFrames + 1

      if lowUsageFrames >= shrinkThreshold then
        let newCapacity = max initialCapacity (buffer.Length / 2)
        let newBuffer = ArrayPool<'T>.Shared.Rent(newCapacity)
        ArrayPool<'T>.Shared.Return(buffer)
        buffer <- newBuffer
        lowUsageFrames <- 0
        lastUsedCount <- 0
    else
      lowUsageFrames <- 0

  /// Inline iteration over queue contents
  let inline iter
    ([<InlineIfLambda>] action: 'T -> unit)
    (queue: ICommandQueue<'T>)
    =
    let span = queue.AsReadOnlySpan()

    for i in 0 .. span.Length - 1 do
      action span[i]

  /// Add an item to the queue
  let inline add (item: inref<'T>) (queue: ICommandQueue<'T>) = queue.Add(&item)

  /// Clear the queue
  let inline clear(queue: ICommandQueue<'T>) = queue.Clear()

  /// Factory function - returns thin object expression wrapping module functions
  let create<'T when 'T: struct>(initialCapacity: int) : ICommandQueue<'T> =
    let mutable buffer = ArrayPool<'T>.Shared.Rent initialCapacity
    let mutable count = 0
    let mutable lastUsedCount = 0
    let mutable lowUsageFrames = 0

    { new ICommandQueue<'T> with
        member _.Count = count
        member _.Add cmd = addImpl &buffer &count &cmd

        member _.Clear() =
          clearImpl
            initialCapacity
            &buffer
            &count
            &lastUsedCount
            &lowUsageFrames

        member _.Iterate action =
          for i in 0 .. count - 1 do
            action buffer[i]

        member _.Sort comparer = Array.Sort(buffer, 0, count, comparer)

        member _.AsReadOnlySpan() = ReadOnlySpan<'T>(buffer, 0, count)

      interface IDisposable with
        member _.Dispose() =
          ArrayPool<'T>.Shared.Return buffer
          buffer <- Array.empty
    }

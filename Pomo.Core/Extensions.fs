namespace Pomo.Core

open System.Collections.Generic
open FSharp.Data.Adaptive


module Dictionary =

  let inline tryFindV
    (key: 'Key)
    (dict: IReadOnlyDictionary<'Key, 'Value>)
    : 'Value voption =
    match dict.TryGetValue key with
    | true, value -> ValueSome value
    | false, _ -> ValueNone

  let inline toHashMap(dict: IReadOnlyDictionary<'Key, 'Value>) =
    let mutable acc = HashMap.empty

    for kv in dict do
      acc <- HashMap.add kv.Key kv.Value acc

    acc

module Array =

  let inline partition
    (predicate: 'T -> bool)
    (array: 'T array)
    : struct ('T array * 'T array) =
    let trueItems = ResizeArray<'T>()
    let falseItems = ResizeArray<'T>()

    for item in array do
      if predicate item then
        trueItems.Add(item)
      else
        falseItems.Add(item)

    struct (trueItems.ToArray(), falseItems.ToArray())

  let inline partitionMap
    (chooser: 'T -> Choice<'U1, 'U2>)
    (array: 'T array)
    : struct ('U1 array * 'U2 array) =
    let first = ResizeArray<'U1>()
    let second = ResizeArray<'U2>()

    for item in array do
      match chooser item with
      | Choice1Of2 v1 -> first.Add(v1)
      | Choice2Of2 v2 -> second.Add(v2)

    struct (first.ToArray(), second.ToArray())

[<AutoOpen>]
module DictionaryExtensions =

  type IReadOnlyDictionary<'Key, 'Value> with
    member this.TryFindV(key: 'Key) : 'Value voption =
      match this.TryGetValue key with
      | true, value -> ValueSome value
      | false, _ -> ValueNone

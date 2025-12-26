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
    dict |> Seq.map(fun kv -> struct (kv.Key, kv.Value)) |> HashMap.ofSeqV

  let inline toArrayV(dict: IReadOnlyDictionary<_, _>) = [|
    for KeyValue(key, value) in dict do
      struct (key, value)
  |]

  let inline filter
    ([<InlineIfLambda>] predicate: _ -> _ -> bool)
    (dict: IReadOnlyDictionary<_, _>)
    =
    let newd = Dictionary()

    for KeyValue(key, value) in dict do
      if predicate key value then
        newd.Add(key, value)

    newd

  let inline chooseV
    ([<InlineIfLambda>] chooser: _ -> _ -> 'T voption)
    (dict: IReadOnlyDictionary<_, _>)
    =
    let newd = Dictionary()

    for KeyValue(key, value) in dict do
      match chooser key value with
      | ValueSome v -> newd.Add(key, v)
      | ValueNone -> ()

    newd

  let inline ofSeq(seq: seq<_ * _>) =
    let d = Dictionary()

    for key, value in seq do
      d.Add(key, value)

    d

  let inline ofSeqV(seq: seq<struct (_ * _)>) =
    let d = Dictionary()

    for struct (key, value) in seq do
      d.Add(key, value)

    d

  let empty() = Dictionary()

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

  let inline chooseV
    ([<InlineIfLambda>] chooser: _ -> 'U voption)
    (array: 'T array)
    : 'U array =
    [|
      for item in array do
        match chooser item with
        | ValueSome v -> v
        | ValueNone -> ()
    |]

[<AutoOpen>]
module DictionaryExtensions =

  type IReadOnlyDictionary<'Key, 'Value> with
    member this.TryFindV(key: 'Key) : 'Value voption =
      match this.TryGetValue key with
      | true, value -> ValueSome value
      | false, _ -> ValueNone

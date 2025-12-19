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

[<AutoOpen>]
module DictionaryExtensions =

  type IReadOnlyDictionary<'Key, 'Value> with
    member this.TryFindV(key: 'Key) : 'Value voption =
      match this.TryGetValue key with
      | true, value -> ValueSome value
      | false, _ -> ValueNone

namespace Pomo.Lib

open System.Text.Json
open JDeck

[<Interface>]
type Serialization =
  abstract Options: JsonSerializerOptions
  abstract Serialize: 'T -> string
  abstract Deserialize: string -> 'T

[<Interface>]
type SerializationCap =
  abstract Serialization: Serialization

module SerializationService =
  let live(options: JsonSerializerOptions) : Serialization =
    { new Serialization with
        member _.Options = options

        member _.Serialize value =
          JsonSerializer.Serialize(value, options)

        member _.Deserialize json =
          JsonSerializer.Deserialize(json, options)
    }

  // Curried helpers
  let serialize (env: #SerializationCap) value =
    env.Serialization.Serialize value

  let deserialize (env: #SerializationCap) json =
    env.Serialization.Deserialize json

  let options(env: #SerializationCap) = env.Serialization.Options

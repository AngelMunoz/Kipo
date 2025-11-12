# F# and MonoGame Project Structure

This is a cross-platform game built with F# and MonoGame. The project is structured to share core logic across multiple platforms (Windows, DesktopGL, Android, iOS).

- The general guidelines of this project are in @AGENTS.md and @.agents/fsharp_conventions.md

## Code sugestions

When you're suggesting code to the user, your proposed code should avoid living in a single place.

- Large function bodies should be refactored into smaller functions.
- Logic that can be reused should be moved to module-level functions.
- Avoid putting large amounts of logic directly inside class methods.

**Game Systems and Drawable Game Systems:**

When implementing Sytems, whether they're a GameComponent (or our GameSystem class) or a DrawableGameComponent. The component class itself should be a slim wrapper that delegates all logic to module-level functions.

Data used for updates and draws should be stored in let bindings within the class to allow FSharp.Data.Adaptive to start tracking and caching data.
only at evaluation time (e.g. inside the Update or Draw methods) should we call `AVal.force` to get the current value.

```fsharp

module System =
   let nonAdaptiveLogic data currentValue =
      // non-adaptive logic here
      let transformedData =
         External.processData data currentValue
      transformedData

   let transformation (data: _ aval) (otherData: amap<_,_>): _ aval =
      otherData
      |> AMap.mapA(fun key value -> adaptive {
         let! data = data
         // adaptive logic here
         let transformedValue = nonAdaptiveLogic data value
         // use transformedValue in further adaptive computations
         let adaptiveResult = Module.adaptiveLogic transformedValue
         return! adaptiveResult
      })

   let publishEventsFromTransformation data eventBus =
      for data in data do
         eventBus.Publish(data)


   type System(game: Game) =
      inherit GameSystem(game) =

      let adaptiveData =
         transformation
            Projections.SomeAdaptiveValue this.World
            Projections.SomeAdaptiveMap this.World

      override _.Update(gameTime) =
         let currentValue = AVal.force adaptiveData
         // use currentValue for update logic

         // for example to trigger events:
         publishEventsFromTransformation currentValue this.EventBus
```

# Kipo - Agent Guidelines

> **Pomo** is an isometric ARPG engine using [Mibo](https://angelmunoz.github.io/Mibo/) (Elmish functional framework) with Data-Oriented Programming. New work goes in **Pomo.Lib** with high-performance patterns.

## Quick Start

- Read this file and [.agents/README.md](./.agents/README.md) first
- Use `gh` CLI for GitHub issue context
- Follow F# conventions and AppEnv DI pattern strictly
- No code comments unless requested

## Build Commands

```bash
dotnet build Pomo.slnx                    # Build solution
dotnet test Pomo.Core.Tests               # Run tests (legacy location)
dotnet test Pomo.Core.Tests --filter "FullyQualifiedName~CommandQueueTests"  # Single class
dotnet test Pomo.Core.Tests --filter "Name~create returns working queue"     # Single test
dotnet run --project Pomo.DesktopGL/Pomo.DesktopGL.fsproj  # Run DesktopGL
dotnet restore                            # Restore dependencies
dotnet workload restore                   # Restore mobile workloads
```

## Project Structure

```
Pomo.Lib/           # Core Library
Pomo.Lib/Gameplay/  # Gameplay systems
Pomo.DesktopGL/     # Linux/macOS/Windows runner
Pomo.WindowsDX/     # Windows DirectX runner
Pomo.Android/       # Android runner
Pomo.iOS/           # iOS scaffolding
```

## Dependency Injection: AppEnv Pattern

Based on [Bartosz Sypytkowski's pattern](https://www.bartoszsypytkowski.com/dealing-with-complex-dependency-injection-in-f/), using capability interfaces for type-safe DI:

```fsharp
// 1. Service interface (NO I prefix)
[<Interface>]
type Logger =
  abstract Debug: string -> unit
  abstract Error: string -> unit

// 2. Capability interface
[<Interface>]
type LoggerCap = abstract Logger: Logger

// 3. Factory module with `let live`
module Logger =
  let live: Logger =
    { new Logger with
        member _.Debug msg = printfn "[DEBUG] %s" msg
        member _.Error msg = printfn "[ERROR] %s" msg }

  // Curried helpers with generic constraints
  let debug (env: #LoggerCap) msg = env.Logger.Debug msg
  let error (env: #LoggerCap) msg = env.Logger.Error msg

// Composition root
[<Struct>]
type AppEnv = {
  LoggerService: Logger
  FileSystemService: FileSystem
} with
  interface LoggerCap with member this.Logger = this.LoggerService
  interface FileSystemCap with member this.FileSystem = this.FileSystemService
```

Functions declare only needed capabilities. Type inference unions constraints automatically.

## Pomo.Lib File Structure

```
1. namespace Declaration
2. open statements (external libs, then internal)
3. Custom Error types
4. Interface definitions (NO I prefix)
5. Capability interfaces
6. Factory module with `let live` + helpers
```

Example:

```fsharp
namespace Pomo.Lib.Gameplay
open System

type Error = | NotFound of id: string | Invalid of message: string

[<Interface>]
type EntityStore =
  abstract Get: id: string -> Result<Entity, Error>
  abstract Set: entity: Entity -> Result<unit, Error>

[<Interface>]
type EntityStoreCap = abstract EntityStore: EntityStore

module EntityStore =
  let live(): EntityStore =
    let store = ConcurrentDictionary<string, Entity>()
    { new EntityStore with
        member _.Get id =
          match store.TryGetValue id with
          | true, e -> Ok e | false, _ -> Error (NotFound id)
        member _.Set e = store.[e.Id] <- e; Ok () }

  let get (env: #EntityStoreCap) id = env.EntityStore.Get id
  let set (env: #EntityStoreCap) e = env.EntityStore.Set e
```

## Performance Patterns (Mibo)

### Level 1 — Structs

Use `[<Struct>]` for types under 16-24 bytes:

```fsharp
[<Struct>] type Position = { X: float32; Y: float32 }
[<Struct>] type GameMsg = | Move of id: int * delta: Position
```

### Level 2 — Value Tuples

Use `struct (a, b)` in hot loops (zero allocation vs heap tuples).

### Level 3 — Mutable Collections

Hide mutation in subsystems using `ResizeArray`:

```fsharp
type Model = { Entities: ResizeArray<Entity> }
let update dt (entities: ResizeArray<Entity>) =
  let mutable i = 0
  while i < entities.Count do
    let mutable e = entities.[i]
    e.Pos <- e.Pos + e.Vel * dt
    entities.[i] <- e
    i <- i + 1
```

### Level 4 — Buffer Pooling

Use `ArrayPool` for temporary buffers:

```fsharp
open System.Buffers
let buffer = ArrayPool<'T>.Shared.Rent count
try // ... use buffer
finally ArrayPool<'T>.Shared.Return buffer
```

### Level 5 — ByRef/InRef

Avoid copying large structs in physics:

```fsharp
let inline intersects (a: inref<BoundingBox>) (b: inref<BoundingBox>) = ...
let inline integrate (pos: byref<Vector3>) vel dt = pos.X <- pos.X + vel.X * dt
```

## Code Style

### Formatting

- **2 spaces** per level (never tabs)
- Max 80 characters per line
- LF endings, UTF-8, trim trailing whitespace

### Naming

- **PascalCase**: Types, modules, namespaces, fields, union cases
- **camelCase**: Functions, values, parameters
- **Interfaces**: NO I prefix (`Logger`, not `ILogger`)

### Types

- `[<Struct>]` for domain types and discriminated unions
- **ValueOption** over Option
- **Value tuples** (`struct(v1, v2)`) over reference tuples
- `[<RequireQualifiedAccess>]` on modules with common names
- FSharp.UMX for type-safe IDs: `int<EntityId>`

### Error Handling

- `Result<'T, 'TError>` for expected errors
- Exceptions only for unrecoverable errors
- Never return null; use Option

### State Management

- **Pomo.Lib.Gameplay**: Use mutable collections (ResizeArray) + snapshots for performance

### Functions

- Small, single-responsibility
- Data parameter last (pipeline compatible)
- No blank lines between match branches
- Match expressions: exhaustive, small bodies

### Comments

- **DO NOT add comments** unless asked
- `///` for public API docs only

### Testing

- MSTest: `[<TestClass>]` / `[<TestMethod>]`
- FsCheck for property-based tests
- Fake AppEnv implementations for unit tests

## Paradigm Priority

1. **Data Oriented** — Default (immutable data, pure functions)
2. **Interface Abstraction** — Service boundaries (capability interfaces)
3. **Imperative** — Performance sections (documented, justified)
4. **Mutable** — Exceptional only (self-contained, never exposed)

## Critical Rules

| DO NOT                         | DO                                   |
| ------------------------------ | ------------------------------------ |
| Use tabs                       | 2 spaces per level                   |
| Prefix interfaces with I       | PascalCase (`Logger`, not `ILogger`) |
| Expose mutable state           | Encapsulate in pure interfaces       |
| Use `[<AutoOpen>]` freely      | Only for computation builders        |
| Mix null and Option            | Choose Option consistently           |
| Exceptions for expected errors | Use Result                           |
| Deep nesting (>3 levels)       | Keep shallow                         |
| Return null                    | Return Option/Result                 |
| Add methods to records         | Use separate functions/modules       |
| Mutable by default             | Immutable by default                 |
| Reference tuples in hot paths  | Struct tuples                        |
| Standard Option in tight loops | ValueOption                          |

## References

- [.agents/fsharp_conventions.md](./.agents/fsharp_conventions.md) — Detailed F# conventions
- [Mibo Scaling](https://angelmunoz.github.io/Mibo/scaling.html) — Architecture levels
- [Mibo Performance](https://angelmunoz.github.io/Mibo/performance.html) — Optimization patterns
- Small, incremental changes only; update conductor docs for tracked features

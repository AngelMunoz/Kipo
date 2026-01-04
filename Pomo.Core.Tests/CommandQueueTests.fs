module CommandQueueTests

open Microsoft.VisualStudio.TestTools.UnitTesting
open Pomo.Core.Graphics
open FsCheck
open FsCheck.FSharp

// ============================================================================
// Integration Tests for ICommandQueue
// ============================================================================

[<TestClass>]
type IntegrationTests() =

  [<TestMethod>]
  member _.``create returns working queue``() =
    use queue = CommandQueue.create<int> 16
    Assert.AreEqual(0, queue.Count)

  [<TestMethod>]
  member _.``add and count work together``() =
    use queue = CommandQueue.create<int> 16
    let item1 = 10
    let item2 = 20
    queue.Add(&item1)
    queue.Add(&item2)
    Assert.AreEqual(2, queue.Count)

  [<TestMethod>]
  member _.``clear resets count``() =
    use queue = CommandQueue.create<int> 16
    let item = 42
    queue.Add(&item)
    queue.Add(&item)
    queue.Clear()
    Assert.AreEqual(0, queue.Count)

  [<TestMethod>]
  member _.``iterate visits all items``() =
    use queue = CommandQueue.create<int> 16
    let item1 = 10
    let item2 = 20
    let item3 = 30
    queue.Add(&item1)
    queue.Add(&item2)
    queue.Add(&item3)

    let mutable sum = 0
    queue.Iterate(fun x -> sum <- sum + x)

    Assert.AreEqual(60, sum)

  [<TestMethod>]
  member _.``module iter works with inline lambda``() =
    use queue = CommandQueue.create<int> 16
    let item1 = 5
    let item2 = 15
    queue.Add(&item1)
    queue.Add(&item2)

    let mutable sum = 0
    CommandQueue.iter (fun x -> sum <- sum + x) queue

    Assert.AreEqual(20, sum)

  [<TestMethod>]
  member _.``AsReadOnlySpan returns correct slice``() =
    use queue = CommandQueue.create<int> 16
    let item1 = 100
    let item2 = 200
    queue.Add(&item1)
    queue.Add(&item2)

    let span = queue.AsReadOnlySpan()

    Assert.AreEqual(2, span.Length)
    Assert.AreEqual(100, span.[0])
    Assert.AreEqual(200, span.[1])

  [<TestMethod>]
  member _.``sort orders items``() =
    use queue = CommandQueue.create<int> 16
    let item1 = 30
    let item2 = 10
    let item3 = 20
    queue.Add(&item1)
    queue.Add(&item2)
    queue.Add(&item3)

    queue.Sort(System.Collections.Generic.Comparer<int>.Default)

    let span = queue.AsReadOnlySpan()
    Assert.AreEqual(10, span.[0])
    Assert.AreEqual(20, span.[1])
    Assert.AreEqual(30, span.[2])

  [<TestMethod>]
  member _.``buffer grows when capacity exceeded``() =
    use queue = CommandQueue.create<int> 2

    for i in 1..10 do
      let mutable item = i
      queue.Add(&item)

    Assert.AreEqual(10, queue.Count)

// ============================================================================
// Property-Based Tests
// ============================================================================

[<TestClass>]
type PropertyTests() =

  let intListArb = Arb.list(Arb.fromGen(Gen.choose(-50, 50)))

  [<TestMethod>]
  member _.``count always equals number of adds``() =
    Prop.forAll intListArb (fun items ->
      if items.Length > 0 && items.Length < 100 then
        use queue = CommandQueue.create<int> 16

        for item in items do
          let mutable i = item
          queue.Add(&i)

        queue.Count = items.Length
      else
        true)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``iterate sum equals list sum``() =
    Prop.forAll intListArb (fun items ->
      if items.Length > 0 && items.Length < 100 then
        use queue = CommandQueue.create<int> 16

        for item in items do
          let mutable i = item
          queue.Add(&i)

        let mutable sum = 0
        queue.Iterate(fun x -> sum <- sum + x)
        sum = List.sum items
      else
        true)
    |> Check.QuickThrowOnFailure

  [<TestMethod>]
  member _.``clear always results in zero count``() =
    Prop.forAll intListArb (fun items ->
      if items.Length > 0 && items.Length < 100 then
        use queue = CommandQueue.create<int> 16

        for item in items do
          let mutable i = item
          queue.Add(&i)

        queue.Clear()
        queue.Count = 0
      else
        true)
    |> Check.QuickThrowOnFailure

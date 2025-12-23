module CommandQueueTests

open Xunit
open FsCheck.Xunit
open Pomo.Core.Graphics

// ============================================================================
// Integration Tests for ICommandQueue
// ============================================================================

module Integration =

  [<Fact>]
  let ``create returns working queue``() =
    use queue = CommandQueue.create<int> 16
    Assert.Equal(0, queue.Count)

  [<Fact>]
  let ``add and count work together``() =
    use queue = CommandQueue.create<int> 16
    let item1 = 10
    let item2 = 20
    queue.Add(&item1)
    queue.Add(&item2)
    Assert.Equal(2, queue.Count)

  [<Fact>]
  let ``clear resets count``() =
    use queue = CommandQueue.create<int> 16
    let item = 42
    queue.Add(&item)
    queue.Add(&item)
    queue.Clear()
    Assert.Equal(0, queue.Count)

  [<Fact>]
  let ``iterate visits all items``() =
    use queue = CommandQueue.create<int> 16
    let item1 = 10
    let item2 = 20
    let item3 = 30
    queue.Add(&item1)
    queue.Add(&item2)
    queue.Add(&item3)

    let mutable sum = 0
    queue.Iterate(fun x -> sum <- sum + x)

    Assert.Equal(60, sum)

  [<Fact>]
  let ``module iter works with inline lambda``() =
    use queue = CommandQueue.create<int> 16
    let item1 = 5
    let item2 = 15
    queue.Add(&item1)
    queue.Add(&item2)

    let mutable sum = 0
    CommandQueue.iter (fun x -> sum <- sum + x) queue

    Assert.Equal(20, sum)

  [<Fact>]
  let ``AsReadOnlySpan returns correct slice``() =
    use queue = CommandQueue.create<int> 16
    let item1 = 100
    let item2 = 200
    queue.Add(&item1)
    queue.Add(&item2)

    let span = queue.AsReadOnlySpan()

    Assert.Equal(2, span.Length)
    Assert.Equal(100, span.[0])
    Assert.Equal(200, span.[1])

  [<Fact>]
  let ``sort orders items``() =
    use queue = CommandQueue.create<int> 16
    let item1 = 30
    let item2 = 10
    let item3 = 20
    queue.Add(&item1)
    queue.Add(&item2)
    queue.Add(&item3)

    queue.Sort(System.Collections.Generic.Comparer<int>.Default)

    let span = queue.AsReadOnlySpan()
    Assert.Equal(10, span.[0])
    Assert.Equal(20, span.[1])
    Assert.Equal(30, span.[2])

  [<Fact>]
  let ``buffer grows when capacity exceeded``() =
    use queue = CommandQueue.create<int> 2

    for i in 1..10 do
      let mutable item = i
      queue.Add(&item)

    Assert.Equal(10, queue.Count)

// ============================================================================
// Property-Based Tests
// ============================================================================

module Properties =

  [<Property>]
  let ``count always equals number of adds``(items: int list) =
    if items.Length > 0 && items.Length < 1000 then
      use queue = CommandQueue.create<int> 16

      for item in items do
        let mutable i = item
        queue.Add(&i)

      queue.Count = items.Length
    else
      true // skip edge cases

  [<Property>]
  let ``iterate sum equals list sum``(items: int list) =
    if items.Length > 0 && items.Length < 1000 then
      use queue = CommandQueue.create<int> 16

      for item in items do
        let mutable i = item
        queue.Add(&i)

      let mutable sum = 0
      queue.Iterate(fun x -> sum <- sum + x)
      sum = List.sum items
    else
      true

  [<Property>]
  let ``clear always results in zero count``(items: int list) =
    if items.Length > 0 && items.Length < 1000 then
      use queue = CommandQueue.create<int> 16

      for item in items do
        let mutable i = item
        queue.Add(&i)

      queue.Clear()
      queue.Count = 0
    else
      true

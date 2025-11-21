namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units

module Spatial =

  [<Struct>]
  type GridCell = { X: int; Y: int }

  let getGridCell (cellSize: float32) (position: Vector2) : GridCell = {
    X = int(position.X / cellSize)
    Y = int(position.Y / cellSize)
  }

  // SAT Helpers
  let getAxes(points: IndexList<Vector2>) =
    points
    |> IndexList.pairwise
    |> IndexList.map(fun (p1, p2) ->
      let edge = p2 - p1
      Vector2(-edge.Y, edge.X) |> Vector2.Normalize)
    |> fun axes ->
        // Add last edge (closing the loop)
        match IndexList.tryLast points, IndexList.tryFirst points with
        | Some pLast, Some pFirst ->
          let edge = pFirst - pLast
          let lastAxis = Vector2(-edge.Y, edge.X) |> Vector2.Normalize
          IndexList.add lastAxis axes
        | _ -> axes

  let project (axis: Vector2) (points: IndexList<Vector2>) =
    let dots = points |> IndexList.map(fun p -> Vector2.Dot(p, axis))
    // IndexList doesn't have min/max directly, so convert to seq or fold
    let min = dots |> Seq.min
    let max = dots |> Seq.max
    min, max

  let overlap (minA, maxA) (minB, maxB) = not(maxA < minB || maxB < minA)

  let intersects (polyA: IndexList<Vector2>) (polyB: IndexList<Vector2>) =
    let axesA = getAxes polyA
    let axesB = getAxes polyB
    let allAxes = IndexList.append axesA axesB

    allAxes
    |> IndexList.forall(fun _ axis ->
      let projA = project axis polyA
      let projB = project axis polyB
      overlap projA projB)

  let intersectsMTV
    (polyA: IndexList<Vector2>)
    (polyB: IndexList<Vector2>)
    : Vector2 voption =
    let axesA = getAxes polyA
    let axesB = getAxes polyB
    let allAxes = IndexList.append axesA axesB

    let mutable minOverlap = Single.MaxValue
    let mutable mtvAxis = Vector2.Zero
    let mutable separated = false

    for axis in allAxes do
      if not separated then
        let minA, maxA = project axis polyA
        let minB, maxB = project axis polyB

        if not(overlap (minA, maxA) (minB, maxB)) then
          separated <- true
        else
          let o = Math.Min(maxA, maxB) - Math.Max(minA, minB)

          if o < minOverlap then
            minOverlap <- o
            mtvAxis <- axis

    if separated then
      ValueNone
    else
      // Calculate centers for direction
      let centerA =
        polyA |> Seq.fold (+) Vector2.Zero |> (fun s -> s / float32 polyA.Count)

      let centerB =
        polyB |> Seq.fold (+) Vector2.Zero |> (fun s -> s / float32 polyB.Count)

      let direction = centerA - centerB

      if Vector2.Dot(direction, mtvAxis) < 0.0f then
        mtvAxis <- -mtvAxis

      ValueSome(mtvAxis * minOverlap)

  let getEntityPolygon(pos: Vector2) =
    IndexList.ofList [
      Vector2(pos.X - 16.0f, pos.Y - 16.0f)
      Vector2(pos.X + 16.0f, pos.Y - 16.0f)
      Vector2(pos.X + 16.0f, pos.Y + 16.0f)
      Vector2(pos.X - 16.0f, pos.Y + 16.0f)
    ]

  let rotate (v: Vector2) (radians: float32) =
    let cos = MathF.Cos radians
    let sin = MathF.Sin radians
    Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos)

  let getMapObjectPolygon(obj: Pomo.Core.Domain.Map.MapObject) =
    let radians = MathHelper.ToRadians(obj.Rotation)
    let pos = Vector2(obj.X, obj.Y)

    match obj.Points with
    | ValueSome points ->
      points |> IndexList.map(fun p -> rotate p radians + pos)
    | ValueNone ->
      // Rectangle
      let w = obj.Width
      let h = obj.Height

      let corners =
        IndexList.ofList [
          Vector2.Zero
          Vector2(w, 0.0f)
          Vector2(w, h)
          Vector2(0.0f, h)
        ]

      corners |> IndexList.map(fun p -> rotate p radians + pos)

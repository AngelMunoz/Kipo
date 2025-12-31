namespace Pomo.Core.Domain

open System
open Microsoft.Xna.Framework
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core

/// 3D spatial types and targeting for skills in true 3D space
module Spatial3D =

  // ============================================================================
  // 3D Shapes (equivalents of 2D Circle, Cone, Line)
  // ============================================================================

  [<Struct>]
  type Sphere = {
    Center: WorldPosition
    Radius: float32
  }

  [<Struct>]
  type Cone3D = {
    Origin: WorldPosition
    Direction: Vector3
    AngleDegrees: float32
    Length: float32
  }

  [<Struct>]
  type Cylinder = {
    Base: WorldPosition
    Height: float32
    Radius: float32
  }

  // ============================================================================
  // Point-in-Shape Tests
  // ============================================================================

  let inline isPointInSphere (sphere: Sphere) (point: WorldPosition) : bool =
    let dx = point.X - sphere.Center.X
    let dy = point.Y - sphere.Center.Y
    let dz = point.Z - sphere.Center.Z
    dx * dx + dy * dy + dz * dz <= sphere.Radius * sphere.Radius

  let inline isPointInCone3D (cone: Cone3D) (point: WorldPosition) : bool =
    let dx = point.X - cone.Origin.X
    let dy = point.Y - cone.Origin.Y
    let dz = point.Z - cone.Origin.Z
    let distSq = dx * dx + dy * dy + dz * dz

    // Outside length
    if distSq > cone.Length * cone.Length then
      false
    // Point at origin is always in cone
    elif distSq < 0.0001f then
      true
    else
      // Check angle
      let toPoint = Vector3(dx, dy, dz) |> Vector3.Normalize
      let dir = cone.Direction |> Vector3.Normalize
      let dot = Vector3.Dot(dir, toPoint)
      let halfAngleRad = MathHelper.ToRadians(cone.AngleDegrees / 2.0f)
      dot >= MathF.Cos halfAngleRad

  let inline isPointInCylinder (cyl: Cylinder) (point: WorldPosition) : bool =
    // Cylinder extends from Base.Y to Base.Y + Height
    // Check height bounds
    if point.Y < cyl.Base.Y || point.Y > cyl.Base.Y + cyl.Height then
      false
    else
      // Check XZ distance from cylinder axis
      let dx = point.X - cyl.Base.X
      let dz = point.Z - cyl.Base.Z
      dx * dx + dz * dz <= cyl.Radius * cyl.Radius

  // ============================================================================
  // Search Context and Requests
  // ============================================================================

  /// Context for 3D spatial searches
  type SearchContext3D = {
    /// Get entities near a world position within radius
    GetNearbyEntities:
      WorldPosition
        -> float32
        -> IndexList<struct (Guid<EntityId> * WorldPosition)>
  }

  [<Struct>]
  type SphereSearchRequest = {
    CasterId: Guid<EntityId>
    Sphere: Sphere
    MaxTargets: int
  }

  [<Struct>]
  type Cone3DSearchRequest = {
    CasterId: Guid<EntityId>
    Cone: Cone3D
    MaxTargets: int
  }

  [<Struct>]
  type CylinderSearchRequest = {
    CasterId: Guid<EntityId>
    Cylinder: Cylinder
    MaxTargets: int
  }

  // ============================================================================
  // Search Functions (module functions, GC-friendly)
  // ============================================================================

  module Search =

    let findTargetsInSphere
      (ctx: SearchContext3D)
      (request: SphereSearchRequest)
      : IndexList<Guid<EntityId>> =
      let nearby =
        ctx.GetNearbyEntities request.Sphere.Center request.Sphere.Radius

      let targets =
        nearby
        |> IndexList.filter(fun struct (id, pos) ->
          id <> request.CasterId && isPointInSphere request.Sphere pos)
        |> IndexList.sortBy(fun struct (_, pos) ->
          let dx = pos.X - request.Sphere.Center.X
          let dy = pos.Y - request.Sphere.Center.Y
          let dz = pos.Z - request.Sphere.Center.Z
          dx * dx + dy * dy + dz * dz)
        |> IndexList.map(fun struct (id, _) -> id)

      if request.MaxTargets >= IndexList.count targets then
        targets
      else
        targets |> IndexList.take request.MaxTargets

    let findTargetsInCone3D
      (ctx: SearchContext3D)
      (request: Cone3DSearchRequest)
      : IndexList<Guid<EntityId>> =
      let nearby = ctx.GetNearbyEntities request.Cone.Origin request.Cone.Length

      let targets =
        nearby
        |> IndexList.filter(fun struct (id, pos) ->
          id <> request.CasterId && isPointInCone3D request.Cone pos)
        |> IndexList.sortBy(fun struct (_, pos) ->
          let dx = pos.X - request.Cone.Origin.X
          let dy = pos.Y - request.Cone.Origin.Y
          let dz = pos.Z - request.Cone.Origin.Z
          dx * dx + dy * dy + dz * dz)
        |> IndexList.map(fun struct (id, _) -> id)

      if request.MaxTargets >= IndexList.count targets then
        targets
      else
        targets |> IndexList.take request.MaxTargets

    let findTargetsInCylinder
      (ctx: SearchContext3D)
      (request: CylinderSearchRequest)
      : IndexList<Guid<EntityId>> =
      // Broad phase: search using max of radius and height/2 from center
      let broadRadius =
        max request.Cylinder.Radius (request.Cylinder.Height / 2.0f)

      let centerY = request.Cylinder.Base.Y + request.Cylinder.Height / 2.0f

      let searchCenter: WorldPosition = {
        X = request.Cylinder.Base.X
        Y = centerY
        Z = request.Cylinder.Base.Z
      }

      let nearby = ctx.GetNearbyEntities searchCenter broadRadius

      let targets =
        nearby
        |> IndexList.filter(fun struct (id, pos) ->
          id <> request.CasterId && isPointInCylinder request.Cylinder pos)
        |> IndexList.sortBy(fun struct (_, pos) ->
          let dx = pos.X - request.Cylinder.Base.X
          let dz = pos.Z - request.Cylinder.Base.Z
          dx * dx + dz * dz) // Sort by XZ distance from axis
        |> IndexList.map(fun struct (id, _) -> id)

      if request.MaxTargets >= IndexList.count targets then
        targets
      else
        targets |> IndexList.take request.MaxTargets

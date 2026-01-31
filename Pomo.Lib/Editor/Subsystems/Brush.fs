namespace Pomo.Lib.Editor.Subsystems

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish
open Mibo.Rendering
open Mibo.Rendering.Graphics3D
open Pomo.Lib
open Pomo.Lib.Editor
open FSharp.UMX

module Brush =
  [<Struct>]
  type BrushModel = {
    Mode: BrushMode
    SelectedBlockId: int<BlockTypeId>
    Rotation: Quaternion
    CollisionEnabled: bool
    IsDragging: bool
    DragStartCell: Vector3 voption
    LastPlacedCell: Vector3 voption
  }

  type Msg =
    | SetMode of BrushMode
    | SetBlockId of int<BlockTypeId>
    | RotateY of degrees: float32
    | ToggleCollision
    | SetDragging of bool * startCell: Vector3 voption
    | SetLastPlacedCell of Vector3 voption
    | ResetRotation

  let init() : BrushModel = {
    Mode = BrushMode.Place
    SelectedBlockId = UMX.tag 1
    Rotation = Quaternion.Identity
    CollisionEnabled = false
    IsDragging = false
    DragStartCell = ValueNone
    LastPlacedCell = ValueNone
  }

  let update (msg: Msg) (model: BrushModel) : struct (BrushModel * Cmd<Msg>) =
    match msg with
    | SetMode m -> { model with Mode = m }, Cmd.none
    | SetBlockId id -> { model with SelectedBlockId = id }, Cmd.none
    | RotateY deg ->
      let up = Vector3.Up
      let rot = Quaternion.CreateFromAxisAngle(up, MathHelper.ToRadians deg)

      {
        model with
            Rotation = rot * model.Rotation
      },
      Cmd.none
    | ToggleCollision ->
      {
        model with
            CollisionEnabled = not model.CollisionEnabled
      },
      Cmd.none
    | SetDragging(dragging, cell) ->
      {
        model with
            IsDragging = dragging
            DragStartCell = if dragging then cell else ValueNone
      },
      Cmd.none
    | SetLastPlacedCell cell -> { model with LastPlacedCell = cell }, Cmd.none
    | ResetRotation ->
      {
        model with
            Rotation = Quaternion.Identity
      },
      Cmd.none

  let view
    _
    (model: BrushModel)
    (cursorCell: Vector3 voption)
    (buffer: RenderBuffer<unit, RenderCommand>)
    =
    match cursorCell with
    | ValueNone -> ()
    | ValueSome cell ->
      match model.Mode with
      | BrushMode.Place ->
        let color = Color(1f, 1f, 1f, 0.3f)
        let min = Vector3(cell.X, cell.Y, cell.Z)
        let max = Vector3(cell.X + 1f, cell.Y + 1f, cell.Z + 1f)

        let verts = [|
          VertexPositionColor(Vector3(min.X, min.Y, min.Z), color)
          VertexPositionColor(Vector3(max.X, min.Y, min.Z), color)
          VertexPositionColor(Vector3(max.X, min.Y, min.Z), color)
          VertexPositionColor(Vector3(max.X, min.Y, max.Z), color)
          VertexPositionColor(Vector3(max.X, min.Y, max.Z), color)
          VertexPositionColor(Vector3(min.X, min.Y, max.Z), color)
          VertexPositionColor(Vector3(min.X, min.Y, max.Z), color)
          VertexPositionColor(Vector3(min.X, min.Y, min.Z), color)
          VertexPositionColor(Vector3(min.X, max.Y, min.Z), color)
          VertexPositionColor(Vector3(max.X, max.Y, min.Z), color)
          VertexPositionColor(Vector3(max.X, max.Y, min.Z), color)
          VertexPositionColor(Vector3(max.X, max.Y, max.Z), color)
          VertexPositionColor(Vector3(max.X, max.Y, max.Z), color)
          VertexPositionColor(Vector3(min.X, max.Y, max.Z), color)
          VertexPositionColor(Vector3(min.X, max.Y, max.Z), color)
          VertexPositionColor(Vector3(min.X, max.Y, min.Z), color)
          VertexPositionColor(Vector3(min.X, min.Y, min.Z), color)
          VertexPositionColor(Vector3(min.X, max.Y, min.Z), color)
          VertexPositionColor(Vector3(max.X, min.Y, min.Z), color)
          VertexPositionColor(Vector3(max.X, max.Y, min.Z), color)
          VertexPositionColor(Vector3(max.X, min.Y, max.Z), color)
          VertexPositionColor(Vector3(max.X, max.Y, max.Z), color)
          VertexPositionColor(Vector3(min.X, min.Y, max.Z), color)
          VertexPositionColor(Vector3(min.X, max.Y, max.Z), color)
        |]

        buffer.Lines(verts, 12) |> ignore
      | BrushMode.Erase -> ()
      | BrushMode.Select -> ()

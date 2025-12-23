namespace Pomo.Core.Domain

module Cursor =

  [<Struct>]
  type CursorType =
    | Arrow
    | Hand
    | Attack
    | Move
    | Targeting


  type CursorService =
    abstract member SetCursor: CursorType -> unit
    abstract member GetCurrentCursor: unit -> CursorType

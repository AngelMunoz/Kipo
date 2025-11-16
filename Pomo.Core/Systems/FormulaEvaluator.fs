namespace Pomo.Core.Systems

open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Entity

module FormulaEvaluator =

  let private getValue (stats: Entity.DerivedStats) (varId: Formula.VarId) =
    match varId with
    | Formula.VarId.AP -> float stats.AP
    | Formula.VarId.AC -> float stats.AC
    | Formula.VarId.DX -> float stats.DX
    | Formula.VarId.MP -> float stats.MP
    | Formula.VarId.MA -> float stats.MA
    | Formula.VarId.MD -> float stats.MD
    | Formula.VarId.WT -> float stats.WT
    | Formula.VarId.DA -> float stats.DA
    | Formula.VarId.LK -> float stats.LK
    | Formula.VarId.HP -> float stats.HP
    | Formula.VarId.DP -> float stats.DP
    | Formula.VarId.HV -> float stats.HV
    | Formula.VarId.Fire ->
      stats.ElementAttributes |> HashMap.tryFind Fire |> Option.defaultValue 0.0
    | Formula.VarId.Water ->
      stats.ElementAttributes
      |> HashMap.tryFind Water
      |> Option.defaultValue 0.0
    | Formula.VarId.Earth ->
      stats.ElementAttributes
      |> HashMap.tryFind Earth
      |> Option.defaultValue 0.0
    | Formula.VarId.Air ->
      stats.ElementAttributes |> HashMap.tryFind Air |> Option.defaultValue 0.0
    | Formula.VarId.Lightning ->
      stats.ElementAttributes
      |> HashMap.tryFind Lightning
      |> Option.defaultValue 0.0
    | Formula.VarId.Light ->
      stats.ElementAttributes
      |> HashMap.tryFind Light
      |> Option.defaultValue 0.0
    | Formula.VarId.Dark ->
      stats.ElementAttributes |> HashMap.tryFind Dark |> Option.defaultValue 0.0
    // Resistances are defender stats, not used in raw damage calculation.
    | Formula.VarId.FireRes
    | Formula.VarId.WaterRes
    | Formula.VarId.EarthRes
    | Formula.VarId.AirRes
    | Formula.VarId.LightningRes
    | Formula.VarId.LightRes
    | Formula.VarId.DarkRes -> 0.0
    | Formula.VarId.Unknown _ -> 0.0

  let rec private evaluate'
    (attackerStats: Entity.DerivedStats)
    (expr: Formula.MathExpr)
    =
    match expr with
    | Formula.MathExpr.Const c -> c
    | Formula.MathExpr.Var varId -> getValue attackerStats varId
    | Formula.MathExpr.Add(l, r) ->
      evaluate' attackerStats l + evaluate' attackerStats r
    | Formula.MathExpr.Sub(l, r) ->
      evaluate' attackerStats l - evaluate' attackerStats r
    | Formula.MathExpr.Mul(l, r) ->
      evaluate' attackerStats l * evaluate' attackerStats r
    | Formula.MathExpr.Div(l, r) ->
      let denominator = evaluate' attackerStats r

      if denominator = 0.0 then
        0.0
      else
        evaluate' attackerStats l / denominator
    | Formula.MathExpr.Pow(l, r) ->
      System.Math.Pow(evaluate' attackerStats l, evaluate' attackerStats r)
    | Formula.MathExpr.Log l -> System.Math.Log(evaluate' attackerStats l)
    | Formula.MathExpr.Log10 l -> System.Math.Log10(evaluate' attackerStats l)

  let evaluate (attackerStats: Entity.DerivedStats) (expr: Formula.MathExpr) =
    evaluate' attackerStats expr

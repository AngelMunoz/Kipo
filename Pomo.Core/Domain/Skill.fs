namespace Pomo.Core.Domain

open System
open FSharp.UMX
open Pomo.Core.Domain.Units
open Pomo.Core.Domain.Core
open Pomo.Core.Domain.Entity
open Pomo.Core.Domain.Projectile


module Formula =

  [<Struct>]
  type VarId =
    | AP
    | AC
    | DX
    | MP
    | MA
    | MD
    | WT
    | DA
    | LK
    | HP
    | DP
    | HV
    | Fire
    | FireRes
    | Water
    | WaterRes
    | Earth
    | EarthRes
    | Air
    | AirRes
    | Lightning
    | LightningRes
    | Light
    | LightRes
    | Dark
    | DarkRes
    | Unknown of string

  type MathExpr =
    | Const of _const: float
    | Var of VarId
    | Add of addLeft: MathExpr * addRight: MathExpr
    | Sub of subLeft: MathExpr * subRight: MathExpr
    | Mul of mulLeft: MathExpr * mulRight: MathExpr
    | Div of divLeft: MathExpr * divRight: MathExpr
    | Pow of powLeft: MathExpr * powRight: MathExpr
    | Log of logExpr: MathExpr
    | Log10 of log10Expr: MathExpr

  // Error Handling
  type FormulaError =
    | InvalidToken of string * int
    | UnexpectedToken of expected: string * found: string * int
    | UnexpectedEndOfInput of int
    | UnknownVariable of string // Evaluation error, no position info
    | DivisionByZero // Evaluation error, no position info
    | UnmatchedParentheses of int

  exception FormulaException of FormulaError


module Skill =

  [<Struct>]
  type EffectKind =
    | Buff
    | Debuff
    | DamageOverTime
    | ResourceOverTime
    | Stun
    | Silence
    | Taunt

  [<Struct>]
  type StackingRule =
    | NoStack
    | RefreshDuration
    | AddStack of int

  [<Struct>]
  type Duration =
    | Instant
    | Timed of TimeSpan
    | Loop of TimeSpan * TimeSpan
    | PermanentLoop of TimeSpan
    | Permanent

  [<Struct>]
  type DamageSource =
    | Physical
    | Magical

  [<Struct>]
  type EffectModifier =
    | StaticMod of StatModifier
    // e.g. The more MA, the more HP heals as the result of the effect
    | DynamicMod of expression: Formula.MathExpr * target: Stat
    // e.g. Hellfire residual burn damage based on ability power and element
    | AbilityDamageMod of
      abilityDamageValue: Formula.MathExpr *
      element: Element voption
    | ResourceChange of resource: ResourceType * amount: Formula.MathExpr

  [<Struct>]
  type Effect = {
    Name: string
    Kind: EffectKind
    DamageSource: DamageSource
    Stacking: StackingRule
    Duration: Duration
    Modifiers: EffectModifier[]
  }

  [<Struct>]
  type ActiveEffect = {
    Id: Guid<EffectId>
    SourceEffect: Effect
    SourceEntity: Guid<EntityId>
    TargetEntity: Guid<EntityId>
    StartTime: TimeSpan
    StackCount: int
  }

  [<Struct>]
  type SkillIntent =
    | Offensive
    | Supportive

  [<Struct>]
  type PassiveSkill = {
    Id: int<SkillId>
    Name: string
    Description: string
    Effects: Effect[]
  }

  [<Struct>]
  type ResourceCost = {
    ResourceType: ResourceType
    Amount: int voption
  }

  [<Struct>]
  type GroundAreaKind =
    | Circle of radius: float32
    | Square of sideLength: float32
    | Cone of angle: float32 * length: float32
    | Rectangle of width: float32 * length: float32

  [<Struct>]
  type Targeting =
    | Self
    | TargetEntity
    | TargetPosition
    | TargetDirection

  [<Struct>]
  type SkillArea =
    | Point
    | Circle of radius: float32 * maxCircleTargets: int
    | Cone of angle: float32 * length: float32 * maxConeTargets: int
    | Line of width: float32 * length: float32 * maxLineTargets: int
    | MultiPoint of radius: float32 * maxPointTargets: int
    | AdaptiveCone of length: float32 * maxTargets: int

  [<Struct>]
  type CastOrigin =
    | Caster
    | CasterOffset of struct (float32 * float32)
    | TargetOffset of struct (float32 * float32)

  [<Struct>]
  type Delivery =
    | Instant
    | Projectile of projectile: ProjectileInfo

  [<Struct>]
  type ElementFormula = {
    Element: Element
    Formula: Formula.MathExpr
  }


  [<Struct>]
  type ActiveSkill = {
    Id: int<SkillId>
    Name: string
    Description: string
    Intent: SkillIntent
    DamageSource: DamageSource
    Cost: ResourceCost voption
    Cooldown: TimeSpan voption
    CastingTime: TimeSpan voption
    Targeting: Targeting
    Range: float32 voption
    Delivery: Delivery
    Area: SkillArea
    Formula: Formula.MathExpr voption
    ElementFormula: ElementFormula voption
    Effects: Effect[]
    Origin: CastOrigin
  }

  [<Struct>]
  type Skill =
    | Passive of passive: PassiveSkill
    | Active of active: ActiveSkill


  module private FormulaParser =
    open Formula
    #nowarn 3391

    // Helper for comparing a ReadOnlySpan<char> with a string without allocation
    let inline spanEquals (span: ReadOnlySpan<char>) (s: ReadOnlySpan<char>) =
      span.Equals(s, StringComparison.OrdinalIgnoreCase)

    let inline classifyVar(token: ReadOnlySpan<char>) : VarId =
      // Match by length first to reduce comparisons
      match token.Length with
      | 2 ->
        if spanEquals token "AP" then AP
        elif spanEquals token "AC" then AC
        elif spanEquals token "DX" then DX
        elif spanEquals token "MP" then MP
        elif spanEquals token "MA" then MA
        elif spanEquals token "MD" then MD
        elif spanEquals token "WT" then WT
        elif spanEquals token "DA" then DA
        elif spanEquals token "LK" then LK
        elif spanEquals token "HP" then HP
        elif spanEquals token "DP" then DP
        elif spanEquals token "HV" then HV
        else Unknown(token.ToString())
      | 5 ->
        if spanEquals token "FireA" then Fire
        elif spanEquals token "FireR" then FireRes
        elif spanEquals token "WaterA" then Water
        elif spanEquals token "WaterR" then WaterRes
        elif spanEquals token "EarthA" then Earth
        elif spanEquals token "EarthR" then EarthRes
        elif spanEquals token "LightA" then Light
        elif spanEquals token "LightR" then LightRes
        elif spanEquals token "DarkA" then Dark
        elif spanEquals token "DarkR" then DarkRes
        else Unknown(token.ToString())
      | 4 ->
        if spanEquals token "AirA" then Air
        elif spanEquals token "AirR" then AirRes
        else Unknown(token.ToString())
      | 10 ->
        if spanEquals token "LightningA" then Lightning
        elif spanEquals token "LightningR" then LightningRes
        else Unknown(token.ToString())
      | _ -> Unknown(token.ToString())

    // Internal module to handle token-level operations on the input span
    module internal SpanToken =
      let inline isOperator(c: char) =
        c = '('
        || c = ')'
        || c = '+'
        || c = '-'
        || c = '*'
        || c = '/'
        || c = '^'

      let inline skipWhitespace (span: ReadOnlySpan<char>) (i: ref<int>) =
        while i.Value < span.Length && Char.IsWhiteSpace(span[i.Value]) do
          i.Value <- i.Value + 1

      let inline peek
        (i: ref<int>)
        (span: ReadOnlySpan<char>)
        : ReadOnlySpan<char> =
        skipWhitespace span i

        if i.Value >= span.Length then
          ReadOnlySpan.Empty
        else
          let c = span[i.Value]
          let startIndex = i.Value
          let mutable endIndex = i.Value + 1

          if isOperator c then
            () // Operator is a single char
          elif Char.IsLetter(c) then // Variable or function
            while endIndex < span.Length && Char.IsLetterOrDigit(span[endIndex]) do
              endIndex <- endIndex + 1
          elif Char.IsDigit(c) || c = '.' then // Number
            while endIndex < span.Length
                  && (Char.IsDigit(span[endIndex]) || span[endIndex] = '.') do
              endIndex <- endIndex + 1
          else
            raise(FormulaException(InvalidToken(string c, i.Value)))

          span.Slice(startIndex, endIndex - startIndex)

      let next (i: ref<int>) (span: ReadOnlySpan<char>) : ReadOnlySpan<char> =
        let tokenSlice = peek i span

        if not tokenSlice.IsEmpty then
          i.Value <- i.Value + tokenSlice.Length

        tokenSlice

      let consume (i: ref<int>) (span: ReadOnlySpan<char>) : unit =
        let tokenSlice = peek i span

        if not tokenSlice.IsEmpty then
          i.Value <- i.Value + tokenSlice.Length

    let rec parseExpr (i: ref<int>) (span: ReadOnlySpan<char>) : MathExpr =
      let mutable left = parseTerm i span
      let mutable looping = true

      while looping do
        let nextToken = SpanToken.peek i span

        if spanEquals nextToken "+" then
          SpanToken.consume i span
          let right = parseTerm i span

          left <- Add(left, right)
        elif spanEquals nextToken "-" then
          SpanToken.consume i span
          let right = parseTerm i span

          left <- Sub(left, right)
        else
          looping <- false

      left

    and parseTerm (i: ref<int>) (span: ReadOnlySpan<char>) : MathExpr =
      let mutable left = parsePower i span
      let mutable looping = true

      while looping do
        let nextToken = SpanToken.peek i span

        if spanEquals nextToken "*" then
          SpanToken.consume i span
          let right = parsePower i span

          left <- Mul(left, right)
        elif spanEquals nextToken "/" then
          SpanToken.consume i span
          let right = parsePower i span

          left <- Div(left, right)
        else
          looping <- false

      left

    and parsePower (i: ref<int>) (span: ReadOnlySpan<char>) : MathExpr =
      let left = parseFactor i span
      let nextToken = SpanToken.peek i span

      if spanEquals nextToken "^" then
        SpanToken.consume i span

        let right = parsePower i span // Right-associative

        Pow(left, right)
      else
        left

    and parseFactor (i: ref<int>) (span: ReadOnlySpan<char>) : MathExpr =
      let startPos = i.Value
      let token = SpanToken.next i span

      if token.IsEmpty then
        raise(FormulaException(UnexpectedEndOfInput startPos))

      let firstChar = token[0]

      if Char.IsDigit firstChar || firstChar = '.' then
        match Double.TryParse(token) with
        | true, value -> Const value
        | false, _ ->
          raise(FormulaException(InvalidToken(token.ToString(), startPos)))
      elif Char.IsLetter firstChar then
        if spanEquals token "log" then
          parseFunction Log i span
        elif spanEquals token "log10" then
          parseFunction Log10 i span
        else
          Var(classifyVar token)
      elif spanEquals token "(" then
        let expr = parseExpr i span
        let closingParenPos = i.Value
        let closingToken = SpanToken.next i span

        if spanEquals closingToken ")" then
          expr
        else
          raise(
            FormulaException(
              UnexpectedToken(")", closingToken.ToString(), closingParenPos)
            )
          )
      else
        raise(FormulaException(InvalidToken(token.ToString(), startPos)))

    and parseFunction
      nodeConstructor
      (i: ref<int>)
      (span: ReadOnlySpan<char>)
      : MathExpr =
      let startPos = i.Value
      let openParen = SpanToken.next i span

      if not(spanEquals openParen "(") then
        raise(
          FormulaException(UnexpectedToken("(", openParen.ToString(), startPos))
        )

      let expr = parseExpr i span

      let closeParen = SpanToken.next i span

      if not(spanEquals closeParen ")") then
        raise(
          FormulaException(
            UnexpectedToken(")", closeParen.ToString(), startPos)
          )
        )

      nodeConstructor expr

    let parse(formula: string) : MathExpr =
      let span = formula.AsSpan()
      let i = ref 0
      let expr = parseExpr i span
      SpanToken.skipWhitespace span i

      if i.Value < span.Length then
        raise(
          FormulaException(
            UnexpectedToken(
              "end of input",
              span.Slice(i.Value).ToString(),
              i.Value
            )
          )
        )

      expr

    #warnon 3391

  module Serialization =
    open System.Text.Json
    open System.Text.Json.Serialization
    open JDeck
    open JDeck.Decode
    open Pomo.Core.Domain.Projectile.Serialization

    type DecodeBuilder with

      member inline this.TryWith(body, handler) =
        try
          this.ReturnFrom(body())
        with ex ->
          handler ex


    module Formula =
      /// Examples
      /// "AP * 0.5 + 10"
      /// "(MA ^ 2) / (MD + 1)"
      /// "log(10)"
      /// "log10(MP)"
      let decoder: Decoder<Formula.MathExpr> =
        fun json -> decode {
          let! formulaStr = Required.string json

          try
            return FormulaParser.parse formulaStr
          with ex ->
            return!
              DecodeError.ofError(json.Clone(), "Failed to parse formula")
              |> DecodeError.withException ex
              |> Error
        }

    module ElementFormula =
      /// Examples
      /// { "Element": "Fire", "Formula": "FireA * 2.0" }
      let decoder: Decoder<ElementFormula> =
        fun json -> decode {
          let! element =
            Required.Property.get
              ("Element", Serialization.Element.decoder)
              json

          and! formula = Required.Property.get ("Formula", Formula.decoder) json

          return { Element = element; Formula = formula }
        }

    module EffectKind =
      let decoder: Decoder<EffectKind> =
        fun json -> decode {
          let! kindStr = Required.string json

          match kindStr.ToLowerInvariant() with
          | "buff" -> return Buff
          | "debuff" -> return Debuff
          | "dot"
          | "damageovertime" -> return DamageOverTime
          | "rot"
          | "resourceovertime" -> return ResourceOverTime
          | "stun" -> return Stun
          | "silence" -> return Silence
          | "taunt" -> return Taunt
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown EffectKind: {kindStr}"
              )
              |> Error
        }

    module StackingRule =
      /// Examples
      /// { "Type": "NoStack" }
      /// { "Type": "RefreshDuration" }
      /// { "Type": "AddStack", "StackCount": 3 }
      let decoder: Decoder<StackingRule> =
        fun json -> decode {
          let! type' = Required.Property.get ("Type", Required.string) json

          match type'.ToLowerInvariant() with
          | "nostack" -> return NoStack
          | "refreshduration" -> return RefreshDuration
          | "addstack" ->
            let! stackCount =
              Required.Property.get ("StackCount", Required.int) json

            return AddStack stackCount
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown StackingRule: {type'}"
              )
              |> Error
        }

    module Duration =
      /// Examples
      /// { "Type": "Instant" }
      /// { "Type": "Timed", "Seconds": 10.0 }
      /// { "Type": "Loop", "Interval": 2.0, "Duration": 20.0 }
      /// { "Type": "PermanentLoop", "Interval": 5.0 }
      /// { "Type": "Permanent" }
      let decoder: Decoder<Duration> =
        fun json -> decode {
          let! type' = Required.Property.get ("Type", Required.string) json

          match type'.ToLowerInvariant() with
          | "instant" -> return Duration.Instant
          | "timed" ->
            let! timeSeconds =
              Required.Property.get ("Seconds", Required.float) json

            return Timed(timeSeconds |> TimeSpan.FromSeconds)
          | "loop" ->
            let! intervalSeconds =
              Required.Property.get ("Interval", Required.float) json

            let! durationSeconds =
              Required.Property.get ("Duration", Required.float) json

            return
              Loop(
                intervalSeconds |> TimeSpan.FromSeconds,
                durationSeconds |> TimeSpan.FromSeconds
              )
          | "permanentloop" ->
            let! intervalSeconds =
              Required.Property.get ("Interval", Required.float) json

            return PermanentLoop(intervalSeconds |> TimeSpan.FromSeconds)
          | "permanent" -> return Permanent
          | _ ->
            return!
              DecodeError.ofError(json.Clone(), $"Unknown Duration: {type'}")
              |> Error
        }

    module DamageSource =
      let decoder: Decoder<DamageSource> =
        fun json -> decode {
          let! damageSource = Required.string json

          match damageSource.ToLowerInvariant() with
          | "physical" -> return Physical
          | "magical" -> return Magical
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown DamageType: {damageSource}"
              )
              |> Error
        }

    module EffectModifier =
      /// Examples
      ///
      /// { "Type": "StaticMod", "StatModifier": { "type": "Multiplicative", "stat": "AP", "value": 1.2 } }
      ///
      /// { "Type": "DynamicMod", "Expression": "AP * 1.5", "TargetStat": "HP" }
      ///
      /// { "Type": "AbilityDamageMod", "AbilityDamageValue": "MA * 10" }
      ///
      /// { "Type": "AbilityDamageMod", "AbilityDamageValue": "FireA * 10", "Element": "Fire" }
      ///
      /// { "Type": "ResourceChange", "Resource": "MP", "Amount": "20 }
      let decoder: Decoder<EffectModifier> =
        fun json -> decode {
          let! modifierType =
            Required.Property.get ("Type", Required.string) json

          match modifierType.ToLowerInvariant() with
          | "staticmod" ->
            let! statModifier =
              Required.Property.get
                ("StatModifier", Serialization.StatModifier.decoder)
                json

            return StaticMod statModifier
          | "dynamicmod" ->
            let! expression =
              Required.Property.get ("Expression", Formula.decoder) json

            let! targetStat =
              Required.Property.get
                ("TargetStat", Serialization.Stat.decoder)
                json

            return DynamicMod(expression, targetStat)
          | "abilitydamagemod" ->
            let! abilityDamageValue =
              Required.Property.get ("AbilityDamageValue", Formula.decoder) json

            and! element =
              VOptional.Property.get
                ("Element", Serialization.Element.decoder)
                json

            return AbilityDamageMod(abilityDamageValue, element)
          | "resourcechange" ->
            let! resource =
              Required.Property.get
                ("Resource", Serialization.ResourceType.decoder)
                json

            and! amount = Required.Property.get ("Amount", Formula.decoder) json

            return ResourceChange(resource, amount)
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown EffectModifier type: {modifierType}"
              )
              |> Error
        }

    module Effect =
      let decoder: Decoder<Effect> =
        fun json -> decode {
          let! name = Required.Property.get ("Name", Required.string) json
          and! kind = Required.Property.get ("Kind", EffectKind.decoder) json

          and! damageSource =
            Required.Property.get ("DamageSource", DamageSource.decoder) json

          and! stacking =
            Required.Property.get ("Stacking", StackingRule.decoder) json

          and! duration =
            Required.Property.get ("Duration", Duration.decoder) json

          and! modifiers =
            Required.Property.array ("Modifiers", EffectModifier.decoder) json

          return {
            Name = name
            Kind = kind
            DamageSource = damageSource
            Stacking = stacking
            Duration = duration
            Modifiers = modifiers
          }
        }

    module SkillIntent =
      let decoder: Decoder<SkillIntent> =
        fun json -> decode {
          let! intentStr = Required.string json

          match intentStr.ToLowerInvariant() with
          | "offensive" -> return Offensive
          | "supportive" -> return Supportive
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown SkillIntent: {intentStr}"
              )
              |> Error
        }

    module ResourceCost =
      let decoder: Decoder<ResourceCost> =
        fun json -> decode {
          let! resourceType =
            Required.Property.get
              ("Type", Serialization.ResourceType.decoder)
              json

          and! amount = VOptional.Property.get ("Amount", Required.int) json

          return {
            ResourceType = resourceType
            Amount = amount
          }
        }

    module GroundAreaKind =
      /// Examples
      ///
      /// { "Type": "Circle", "Radius": 5.0 }
      ///
      /// { "Type": "Square", "SideLength": 4.0 }
      ///
      /// { "Type": "Cone", "Angle": 45.0, "Length": 10.0 }
      ///
      /// { "Type": "Rectangle", "Width": 3.0, "Length": 8.0 }
      let decoder: Decoder<GroundAreaKind> =
        fun json -> decode {
          let! type' = Required.Property.get ("Type", Required.string) json

          match type'.ToLowerInvariant() with
          | "circle" ->
            let! radius = Required.Property.get ("Radius", Required.float) json
            return GroundAreaKind.Circle(float32 radius)
          | "square" ->
            let! sideLength =
              Required.Property.get ("SideLength", Required.float) json

            return GroundAreaKind.Square(float32 sideLength)
          | "cone" ->
            let! angle = Required.Property.get ("Angle", Required.float) json
            and! length = Required.Property.get ("Length", Required.float) json
            return GroundAreaKind.Cone(float32 angle, float32 length)
          | "rectangle" ->
            let! width = Required.Property.get ("Width", Required.float) json
            and! length = Required.Property.get ("Length", Required.float) json
            return GroundAreaKind.Rectangle(float32 width, float32 length)
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown GroundAreaKind: {type'}"
              )
              |> Error
        }

    module Targeting =
      let decoder: Decoder<Targeting> =
        fun json -> decode {
          let! targetingStr = Required.string json

          match targetingStr.ToLowerInvariant() with
          | "self" -> return Self
          | "targetentity" -> return TargetEntity
          | "targetposition" -> return TargetPosition
          | "targetdirection" -> return TargetDirection
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown Targeting type: {targetingStr}"
              )
              |> Error
        }

    module SkillArea =
      let decoder: Decoder<SkillArea> =
        fun json ->
          let simpleDecoder =
            fun json -> decode {
              let! areaStr = Required.string json

              match areaStr.ToLowerInvariant() with
              | "point" -> return Point
              | _ ->
                return!
                  DecodeError.ofError(
                    json.Clone(),
                    $"Unknown simple SkillArea type: {areaStr}"
                  )
                  |> Error
            }

          let complexDecoder =
            fun json -> decode {
              let! type' = Required.Property.get ("Type", Required.string) json

              match type'.ToLowerInvariant() with
              | "circle" ->
                let! radius =
                  Required.Property.get ("Radius", Required.float) json

                and! maxTargets =
                  VOptional.Property.get ("MaxTargets", Required.int) json
                  |> Result.map(ValueOption.defaultValue 1)

                return Circle(float32 radius, maxTargets)
              | "cone" ->
                let! angle =
                  Required.Property.get ("Angle", Required.float) json

                and! length =
                  Required.Property.get ("Length", Required.float) json

                and! maxTargets =
                  VOptional.Property.get ("MaxTargets", Required.int) json
                  |> Result.map(ValueOption.defaultValue 1)

                return Cone(float32 angle, float32 length, maxTargets)
              | "line" ->
                let! width =
                  Required.Property.get ("Width", Required.float) json

                and! length =
                  Required.Property.get ("Length", Required.float) json

                and! maxTargets =
                  VOptional.Property.get ("MaxTargets", Required.int) json
                  |> Result.map(ValueOption.defaultValue 1)

                return Line(float32 width, float32 length, maxTargets)
              | "multipoint" ->
                let! radius =
                  Required.Property.get ("Radius", Required.float) json

                and! count = Required.Property.get ("Count", Required.int) json
                return MultiPoint(float32 radius, count)
              | "adaptivecone" ->
                let! length =
                  Required.Property.get ("Length", Required.float) json

                and! maxTargets =
                  Required.Property.get ("MaxTargets", Required.int) json

                return AdaptiveCone(float32 length, maxTargets)
              | _ ->
                return!
                  DecodeError.ofError(
                    json.Clone(),
                    $"Unknown complex SkillArea type: {type'}"
                  )
                  |> Error
            }

          Decode.oneOf [ simpleDecoder; complexDecoder ] json

    module CastOrigin =
      let private simpleDecoder: Decoder<CastOrigin> =
        fun json -> decode {
          let! originStr = Required.string json

          match originStr.ToLowerInvariant() with
          | "caster" -> return Caster
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown simple CastOrigin: {originStr}"
              )
              |> Error
        }

      let private complexDecoder: Decoder<CastOrigin> =
        fun json -> decode {
          let! offset =
            VOptional.Property.array ("CasterOffset", Required.float) json

          match offset with
          | ValueSome [| x; y |] ->
            return CasterOffset struct (float32 x, float32 y)
          | ValueSome _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                "CasterOffset requires exactly 2 float values"
              )
              |> Error
          | ValueNone ->
            let! targetOffset =
              VOptional.Property.array ("TargetOffset", Required.float) json

            match targetOffset with
            | ValueSome [| x; y |] ->
              return TargetOffset struct (float32 x, float32 y)
            | ValueSome _ ->
              return!
                DecodeError.ofError(
                  json.Clone(),
                  "TargetOffset requires exactly 2 float values"
                )
                |> Error
            | ValueNone ->
              return!
                DecodeError.ofError(
                  json.Clone(),
                  "Expected 'Caster', 'CasterOffset', or 'TargetOffset'"
                )
                |> Error
        }

      let decoder: Decoder<CastOrigin> =
        Decode.oneOf [ simpleDecoder; complexDecoder ]

    module PassiveSkill =
      let decoder: Decoder<PassiveSkill> =
        fun json -> decode {
          let! id = Required.Property.get ("Id", Required.int) json
          and! name = Required.Property.get ("Name", Required.string) json

          and! description =
            Required.Property.get ("Description", Required.string) json

          and! effects =
            Required.Property.array ("Effects", Effect.decoder) json

          return {
            Id = UMX.tag id
            Name = name
            Description = description
            Effects = effects
          }
        }

    module Delivery =
      /// Examples
      ///
      /// { "Type": "Instant" }
      ///
      /// { "Type": "Projectile",  "Speed": 150.0, "CollisionMode": "IgnoreTerrain" }
      let decoder: Decoder<Delivery> =
        fun json -> decode {
          let! type' = Required.Property.get ("Type", Required.string) json

          match type'.ToLowerInvariant() with
          | "instant" -> return Instant
          | "projectile" ->
            let! projectile = ProjectileInfo.decoder json
            return Projectile projectile
          | _ ->
            return!
              DecodeError.ofError(
                json.Clone(),
                $"Unknown Delivery type: {type'}"
              )
              |> Error
        }

    module ActiveSkill =
      let decoder: Decoder<ActiveSkill> =
        fun json -> decode {
          let! id = Required.Property.get ("Id", Required.int) json
          and! name = Required.Property.get ("Name", Required.string) json

          and! description =
            Required.Property.get ("Description", Required.string) json

          and! intent =
            Required.Property.get ("Intent", SkillIntent.decoder) json

          and! damageSource =
            Required.Property.get ("DamageSource", DamageSource.decoder) json

          and! cost = VOptional.Property.get ("Cost", ResourceCost.decoder) json

          and! cooldownOpt =
            VOptional.Property.get ("Cooldown", Required.float) json

          and! castingTimeOpt =
            VOptional.Property.get ("CastingTime", Required.float) json

          and! targeting =
            Required.Property.get ("Targeting", Targeting.decoder) json

          and! area = Required.Property.get ("Area", SkillArea.decoder) json

          and! rangeOpt =
            VOptional.Property.array ("Range", Required.float) json
            |> Result.bind(fun arr ->
              match arr with
              | ValueSome [| value; size |] ->
                float32(value * size) |> ValueSome |> Ok
              | ValueSome [| value |] -> float32 value |> ValueSome |> Ok
              | ValueSome arr ->
                let joined = String.Join(", ", arr)

                DecodeError.ofError(
                  json.Clone(),
                  $"Range array must have either one or two float values: {joined}"
                )
                |> Error
              | ValueNone -> Ok ValueNone)

          and! formula =
            VOptional.Property.get ("Formula", Formula.decoder) json

          and! delivery =
            Required.Property.get ("Delivery", Delivery.decoder) json

          and! elementFormula =
            VOptional.Property.get
              ("ElementFormula", ElementFormula.decoder)
              json

          and! effects =
            Required.Property.array ("Effects", Effect.decoder) json

          and! origin =
            Required.Property.get ("Origin", CastOrigin.decoder) json

          return {
            Id = UMX.tag id
            Name = name
            Description = description
            Intent = intent
            DamageSource = damageSource
            Cost = cost
            Cooldown = cooldownOpt |> ValueOption.map TimeSpan.FromSeconds
            CastingTime = castingTimeOpt |> ValueOption.map TimeSpan.FromSeconds
            Targeting = targeting
            Area = area
            Range = rangeOpt
            Delivery = delivery
            Formula = formula
            ElementFormula = elementFormula
            Effects = effects
            Origin = origin
          }
        }

    module Skill =
      let decoder: Decoder<Skill> =
        fun json -> decode {
          let! kind = Required.Property.get ("Kind", Required.string) json

          match kind.ToLowerInvariant() with
          | "passive" ->
            let! passive = PassiveSkill.decoder json
            return Passive passive
          | "active" ->
            let! active = ActiveSkill.decoder json
            return Active active
          | _ ->
            return!
              DecodeError.ofError(json.Clone(), $"Unknown Skill kind: {kind}")
              |> Error
        }

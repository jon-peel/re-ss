module ReSS.Tests.DripCalculatorTests

open System
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open ReSS.Domain.Types
open ReSS.Domain.DripCalculator

let private clock (d: DateOnly) : Clock = fun () -> d

let private today = DateOnly(2025, 6, 15)

// ---- 5.1 unit tests ----

[<Fact>]
let ``future start date returns ShowItems 0`` () =
    let result = calculate (clock today) (DateOnly(2025, 7, 1)) 3<articles/day> 100<articles>
    Assert.Equal(ShowItems 0<articles>, result)

[<Fact>]
let ``start date is today returns ShowItems perDay`` () =
    let result = calculate (clock today) today 5<articles/day> 100<articles>
    Assert.Equal(ShowItems 5<articles>, result)

[<Fact>]
let ``partial progress returns correct count`` () =
    // 10 days ago → daysElapsed = 11 (day-1 inclusive) × 3 = 33 items, total = 100
    let start  = today.AddDays(-10)
    let result = calculate (clock today) start 3<articles/day> 100<articles>
    Assert.Equal(ShowItems 33<articles>, result)

[<Fact>]
let ``exactly caught up returns RedirectToSource`` () =
    // 9 days ago → daysElapsed = 10 (day-1 inclusive) × 10 = 100 = total
    let start  = today.AddDays(-9)
    let result = calculate (clock today) start 10<articles/day> 100<articles>
    Assert.Equal(RedirectToSource, result)

[<Fact>]
let ``over-elapsed returns RedirectToSource`` () =
    // 20 days × 10 per day = 200 > 50 total
    let start  = today.AddDays(-20)
    let result = calculate (clock today) start 10<articles/day> 50<articles>
    Assert.Equal(RedirectToSource, result)

// ---- R-2 overflow regression ----

[<Fact>]
let ``very old start date with high perDay returns RedirectToSource without overflow`` () =
    let veryOldStart = DateOnly(1900, 1, 1)
    let result = calculate (clock today) veryOldStart 1000<articles/day> 100<articles>
    Assert.Equal(RedirectToSource, result)

// ---- 5.3 FsCheck properties ----

type DripInputs = { Start: DateOnly; PerDay: int; Total: int; TodayOffset: int }

type DripGenerators =
    static member Inputs() =
        gen {
            let! daysBack   = Gen.choose (-30, 365)  // negative = future start
            let! perDay     = Gen.choose (1, 50)
            let! totalItems = Gen.choose (1, 500)
            let startDate = today.AddDays(-daysBack)
            return { Start = startDate; PerDay = perDay; Total = totalItems; TodayOffset = daysBack }
        } |> Arb.fromGen

[<Fact>]
let ``unlocked is always in [0, total] (property)`` () =
    let prop (inp: DripInputs) =
        let result = calculate (clock today) inp.Start (inp.PerDay * 1<articles/day>) (inp.Total * 1<articles>)
        match result with
        | ShowItems n -> n >= 0<articles> && n <= inp.Total * 1<articles>
        | RedirectToSource -> true
    Prop.forAll (DripGenerators.Inputs()) prop |> Check.QuickThrowOnFailure

[<Fact>]
let ``result is always a valid DU case (property)`` () =
    let prop (inp: DripInputs) =
        let result = calculate (clock today) inp.Start (inp.PerDay * 1<articles/day>) (inp.Total * 1<articles>)
        match result with
        | ShowItems _ | RedirectToSource -> true
    Prop.forAll (DripGenerators.Inputs()) prop |> Check.QuickThrowOnFailure

[<Fact>]
let ``RedirectToSource iff unlocked >= total (property)`` () =
    let prop (inp: DripInputs) =
        let daysElapsed = if today < inp.Start then 0 else today.DayNumber - inp.Start.DayNumber + 1
        let unlocked    = min (daysElapsed * inp.PerDay) inp.Total
        let result = calculate (clock today) inp.Start (inp.PerDay * 1<articles/day>) (inp.Total * 1<articles>)
        match result with
        | RedirectToSource -> unlocked >= inp.Total
        | ShowItems n      -> unlocked < inp.Total && int n = unlocked
    Prop.forAll (DripGenerators.Inputs()) prop |> Check.QuickThrowOnFailure

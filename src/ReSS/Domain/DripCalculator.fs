module ReSS.Domain.DripCalculator

open System
open ReSS.Domain.Types

let calculate (clock: Clock) (startDate: DateOnly) (perDay: int<articles/day>) (total: int<articles>) : DripResult =
    let today       = clock ()
    let daysElapsed = if today < startDate then 0 else today.DayNumber - startDate.DayNumber + 1
    let unlocked    = min (daysElapsed * int perDay * 1<articles>) total
    if unlocked >= total then RedirectToSource
    else ShowItems unlocked

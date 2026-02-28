module ReSS.Domain.DripCalculator

open System
open ReSS.Domain.Types

let calculate (clock: Clock) (startDate: DateOnly) (perDay: int<articles/day>) (total: int<articles>) : DripResult =
    let today        = clock ()
    let daysElapsed  = if today < startDate then 0 else today.DayNumber - startDate.DayNumber + 1
    let unlockedRaw  = min (int64 daysElapsed * int64 (int perDay)) (int64 (int total))
    let unlocked     = int unlockedRaw * 1<articles>
    if unlocked >= total then RedirectToSource
    else ShowItems unlocked

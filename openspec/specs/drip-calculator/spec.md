## ADDED Requirements

### Requirement: Calculate the number of unlocked articles based on elapsed days
The system SHALL compute `unlocked = min(daysElapsed × perDay, totalItems)` where `daysElapsed = max(0, today − startDate)`, using F# units of measure to make the arithmetic dimensionally type-safe.

#### Scenario: Start date in the future yields zero unlocked articles
- **WHEN** `calculate` is called with a start date after today
- **THEN** it returns `ShowItems 0`

#### Scenario: Start date is today yields one day's worth of articles
- **WHEN** `calculate` is called with a start date equal to today and perDay = n
- **THEN** it returns `ShowItems n` (assuming n ≤ totalItems)

#### Scenario: Partial progress yields correct unlocked count
- **WHEN** `calculate` is called with elapsed days × perDay < totalItems
- **THEN** it returns `ShowItems (daysElapsed × perDay)`

#### Scenario: Unlocked never exceeds total items
- **WHEN** `calculate` is called with arbitrary valid inputs
- **THEN** the unlocked count in `ShowItems` is always ≤ totalItems (FsCheck property)

#### Scenario: Unlocked is always non-negative
- **WHEN** `calculate` is called with any inputs including a future start date
- **THEN** the unlocked count is always ≥ 0 (FsCheck property)

---

### Requirement: Signal redirect when the reader is fully caught up
The system SHALL return `DripResult.RedirectToSource` when the computed unlocked count is greater than or equal to totalItems.

#### Scenario: Exactly caught up returns RedirectToSource
- **WHEN** `calculate` is called and `daysElapsed × perDay = totalItems`
- **THEN** it returns `RedirectToSource`

#### Scenario: Over-elapsed returns RedirectToSource
- **WHEN** `calculate` is called and `daysElapsed × perDay > totalItems`
- **THEN** it returns `RedirectToSource`

#### Scenario: RedirectToSource iff unlocked ≥ total
- **WHEN** `calculate` is called with arbitrary valid inputs
- **THEN** `RedirectToSource` is returned if and only if `min(daysElapsed × perDay, totalItems) ≥ totalItems` (FsCheck property)

---

### Requirement: Result is always one of two DU cases
The system SHALL always return either `ShowItems` or `RedirectToSource` — no partial or undefined results.

#### Scenario: Result is always a valid DU case
- **WHEN** `calculate` is called with arbitrary valid inputs
- **THEN** the result is either `ShowItems _` or `RedirectToSource` (FsCheck property)

---

### Requirement: Clock is injected — domain is free of `DateTime.Today`
The system SHALL accept a `Clock = unit -> DateOnly` parameter and use it exclusively to determine today's date, keeping the domain pure and trivially testable.

#### Scenario: Deterministic output for a fixed clock
- **WHEN** `calculate` is called with a fixed clock returning a known date
- **THEN** it produces the same result on every call (pure function)

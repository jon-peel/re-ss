# QA Review — add-tracing-and-metrics

**Date:** 2026-03-01
**Reviewer:** Quinn (QA Engineer)
**Commits reviewed:** `569ecdf`, `13a6cc0`, `729d64f`
**Verdict:** ❌ QA BLOCKED — resolve item #1 (or confirm intent) before merge.

---

## Issues for the Developer

### #1 — Exception capture on spans (FR-25) 🔴 BLOCKING / Clarification required

**File:** `src/ReSS/Handlers.fs` — `withSpan` and `withSpanAsync` helpers

**Problem:**
Both span helpers record errors only when the `body` function returns a `Result.Error` value. If `body` throws an exception instead, the exception propagates without being recorded on the active span. The span will close without an error status, making the failure invisible in the trace UI.

The design spec (FR-25) says: *"Capture exceptions on spans."* It is unclear whether this refers to:
- **(A)** `Result.Error` values only (current behaviour), or
- **(B)** Thrown exceptions as well.

**What to do — Option A (Result errors only, no change needed):**
Update `design.md` and `tasks.md` to explicitly state that FR-25 refers to `Result`-based errors only and that thrown exceptions are handled by ASP.NET Core's built-in exception middleware. Close this item.

**What to do — Option B (exceptions must also be captured):**
Wrap the `body` invocation in a `try/with` block inside both helpers and call `activity.RecordException(ex)` (and `SetStatus Error`) before re-raising:

```fsharp
// withSpan — add try/with around body()
let inline private withSpan
    (name: string)
    (isError: 'a -> bool)
    (errorMsg: 'a -> string)
    (body: unit -> 'a)
    : 'a =
    use activity = activitySource.StartActivity(name)
    try
        let result = body ()
        if activity <> null && isError result then
            activity.SetStatus(ActivityStatusCode.Error, errorMsg result) |> ignore
        result
    with ex ->
        if activity <> null then
            activity.SetStatus(ActivityStatusCode.Error, ex.Message) |> ignore
            activity.RecordException(ex) |> ignore
        reraise ()

// withSpanAsync — same pattern inside the async CE
let inline private withSpanAsync
    (name: string)
    (isError: 'a -> bool)
    (errorMsg: 'a -> string)
    (body: unit -> Async<'a>)
    : Async<'a> =
    async {
        use activity = activitySource.StartActivity(name)
        try
            let! result = body ()
            if activity <> null && isError result then
                activity.SetStatus(ActivityStatusCode.Error, errorMsg result) |> ignore
            return result
        with ex ->
            if activity <> null then
                activity.SetStatus(ActivityStatusCode.Error, ex.Message) |> ignore
                activity.RecordException(ex) |> ignore
            return raise ex
    }
```

> **Note:** `activity.RecordException` is available from `OpenTelemetry.Trace` via the `ActivityExtensions` class. Ensure `open OpenTelemetry.Trace` is present in `Handlers.fs`.

---

### #2 — Package versions in design.md do not match implementation 🟡 Recommended

**File:** `openspec/changes/add-tracing-and-metrics/design.md` — "Package Versions" table

**Problem:**
The design spec lists OTel packages at version `1.15.0`. The actual pinned versions in `Directory.Packages.props` are `1.11.x`. One of these is wrong.

| Package | Spec version | Actual version |
|---|---|---|
| `OpenTelemetry.Extensions.Hosting` | 1.15.0 | **1.11.2** |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.15.0 | **1.11.1** |
| `OpenTelemetry.Instrumentation.Http` | 1.15.0 | **1.11.1** |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.15.0 | **1.11.2** |

**What to do:**
Update the "Package Versions" table in `openspec/changes/add-tracing-and-metrics/design.md` to reflect the versions actually used (`1.11.x`), and add a brief note explaining why (e.g. 1.15.0 does not exist; latest stable at time of implementation was 1.11.x).

---

### #3 — NU1510 build warning on `Microsoft.Extensions.Caching.Memory` 🟡 Recommended

**File:** `src/ReSS/ReSS.fsproj`

**Problem:**
The build emits a `NU1510` warning:

```
warning NU1510: PackageReference Microsoft.Extensions.Caching.Memory will not be pruned.
Consider removing this package from your dependencies, as it is likely unnecessary.
```

This means `Microsoft.Extensions.Caching.Memory` is already available transitively via the ASP.NET Core SDK and the explicit `PackageReference` is redundant. Build warnings erode signal quality over time.

**What to do:**
1. Remove the `<PackageReference Include="Microsoft.Extensions.Caching.Memory" />` line from `src/ReSS/ReSS.fsproj`.
2. Run `dotnet build` and confirm the warning is gone.
3. Run `dotnet test tests/ReSS.Tests` and confirm all 85 tests still pass.
4. If removing it causes a compile error (i.e. the type is not available without the explicit reference), keep the reference and instead suppress the warning with a `NoWarn` entry — but investigate why the transitive resolution is not working.

---

### #4 — `ASPIRE_ALLOW_UNSECURED_TRANSPORT` in the `https` launch profile 🟢 Low / Cosmetic

**File:** `src/ReSS.AppHost/Properties/launchSettings.json`

**Problem:**
`ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` is set in both the `http` and `https` profiles. This flag is only meaningful for the `http` profile (it suppresses the Aspire warning about running without TLS). Its presence in the `https` profile is harmless but misleading.

**What to do:**
Remove `ASPIRE_ALLOW_UNSECURED_TRANSPORT` from the `https` profile:

```json
"https": {
  "commandName": "Project",
  "dotnetRunMessages": true,
  "launchBrowser": true,
  "applicationUrl": "https://localhost:17201;http://localhost:15197",
  "environmentVariables": {
    "ASPNETCORE_ENVIRONMENT": "Development",
    "DOTNET_ENVIRONMENT": "Development",
    "DOTNET_DASHBOARD_OTLP_ENDPOINT_URL": "http://localhost:18889"
  }
}
```

---

## Re-submission Checklist

Before re-submitting for QA sign-off, confirm the following:

- [ ] **#1** — FR-25 scope confirmed: design.md updated (Option A) **or** `withSpan`/`withSpanAsync` updated with `RecordException` (Option B)
- [ ] **#2** — `design.md` package version table updated to match `Directory.Packages.props`
- [ ] **#3** — `NU1510` warning resolved (removed or suppressed with rationale); tests still pass
- [ ] **#4** — `ASPIRE_ALLOW_UNSECURED_TRANSPORT` removed from `https` profile
- [ ] Manual Aspire dashboard verification (tasks 8.1–8.4) completed and noted

> **E2E test failures** (`ReSS.E2E` — 16 failures) are a pre-existing infrastructure issue (`libglib-2.0.so.0` missing from this environment) and are **not caused by this change**. They do not block merge of this feature. Track separately.

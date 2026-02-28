# Implementation Plan: Observability — Tracing & Metrics

**Change:** Add OpenTelemetry tracing and metrics to the RSS Catch-Up Feed Generator
**Date:** 2026-02-28
**Status:** Ready for implementation

---

## Delivery approach

This is a single, self-contained change with no phasing required. It adds instrumentation to a working application without touching any existing functional behaviour. All tasks can be completed sequentially by one developer in one sitting.

---

## Work breakdown

### Phase 1 — Dependencies

**1.1 Add OTEL NuGet packages to `src/ReSS`**

Add to `src/ReSS/ReSS.fsproj`:
- `OpenTelemetry.Extensions.Hosting` 1.15.0
- `OpenTelemetry.Instrumentation.AspNetCore` 1.15.0
- `OpenTelemetry.Instrumentation.Http` 1.15.0
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.0

**1.2 Create `src/ReSS.AppHost` (C#, Aspire 13.1.2)**

- `dotnet new aspire-apphost -o src/ReSS.AppHost`
- Add project reference to `src/ReSS`
- Add `src/ReSS.AppHost` to `re-ss.slnx`

---

### Phase 2 — Instrumentation primitives

**2.1 Create `src/ReSS/Telemetry.fs`**

Define module `ReSS.Telemetry` containing:
- `activitySource : ActivitySource` — `new ActivitySource("ReSS")`
- `meter : Meter` — `new Meter("ReSS")`
- `feedUrlsCreated : Counter<int>` — `meter.CreateCounter<int>("feed.urls_created")`
- `feedRequests : Counter<int>` — `meter.CreateCounter<int>("feed.requests")`
- `feedSourceUrlRequests : Counter<int>` — `meter.CreateCounter<int>("feed.source_url_requests")`

**2.2 Register `Telemetry.fs` in compile order**

In `ReSS.fsproj`, add `<Compile Include="Telemetry.fs" />` before `Handlers.fs`.

---

### Phase 3 — OTEL wiring in `Program.fs`

**3.1 Wire OTEL at startup**

After `let builder = ...`, add:

```fsharp
builder.Services.AddOpenTelemetry()
    .WithTracing(fun b ->
        b.AddSource("ReSS")
         .AddAspNetCoreInstrumentation()
         .AddHttpClientInstrumentation()
         .AddOtlpExporter()
        |> ignore)
    .WithMetrics(fun b ->
        b.AddMeter("ReSS")
         .AddAspNetCoreInstrumentation()
         .AddOtlpExporter()
        |> ignore)
|> ignore
```

No endpoint or headers are hardcoded — the OTLP exporter reads `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_HEADERS` automatically.

---

### Phase 4 — Metrics in `POST /` handler

**4.1 Increment `feedUrlsCreated` on successful feed URL generation**

In `postIndexHandler`, after the feed URL is successfully constructed and before the response is returned, add:

```fsharp
Telemetry.feedUrlsCreated.Add(1)
```

---

### Phase 5 — Metrics & tracing in `GET /feed/{blob}` handler

**5.1 Increment `feedRequests` at handler entry**

At the top of `getFeedHandler`, unconditionally:

```fsharp
Telemetry.feedRequests.Add(1)
```

**5.2 Increment `feedSourceUrlRequests` after successful decode**

After `UrlCodec.decode` succeeds:

```fsharp
Telemetry.feedSourceUrlRequests.Add(1, [| KeyValuePair("source_url", sourceUrl) |])
```

**5.3–5.7 Wrap each pipeline step in a child span**

For each domain operation, use the pattern:

```fsharp
use activity = Telemetry.activitySource.StartActivity("ress.<name>")
```

On error, before returning:

```fsharp
activity |> Option.iter (fun a ->
    a.SetStatus(ActivityStatusCode.Error, errorMessage)
    a.RecordException(exn))  // if an exception is available
```

Spans to add:

| Step | Span name | Error recorded? |
|---|---|---|
| `UrlCodec.decode` | `ress.url_decode` | Yes |
| `UrlGuard.validateUrl` | `ress.url_guard` | Yes |
| `FeedFetcher.fetchFeed` | `ress.feed_fetch` | Yes |
| `DripCalculator.calculate` | `ress.drip_calculate` | No (pure, no errors) |
| `FeedBuilder.buildFeed` | `ress.feed_build` | No (pure, no errors) |

---

### Phase 6 — Tracing in `POST /` handler

**6.1–6.3 Wrap pipeline steps in child spans**

Same pattern as Phase 5. Spans to add:

| Step | Span name | Error recorded? |
|---|---|---|
| `UrlGuard.validateUrl` | `ress.url_guard` | Yes |
| `FeedFetcher.fetchFeed` | `ress.feed_fetch` | Yes |
| `DripCalculator.calculate` | `ress.drip_calculate` | No |

---

### Phase 7 — Aspire AppHost

**7.1 Implement `src/ReSS.AppHost/Program.cs`**

```csharp
var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.ReSS>("ress");
builder.Build().Run();
```

Aspire will inject `OTEL_EXPORTER_OTLP_ENDPOINT` and related vars automatically when launching the app via the AppHost.

**7.2 Verify locally**

- Run via `dotnet run --project src/ReSS.AppHost`
- Open Aspire dashboard (default: `http://localhost:15888`)
- Confirm the ReSS service appears under resources

---

## Testing approach

### Existing tests
No changes to existing unit or integration tests are required. Telemetry primitives (`ActivitySource`, `Meter`) are no-ops when no listener is registered, so test runs are unaffected.

### Manual verification checklist (Phase 8)

| # | Check | How |
|---|---|---|
| 8.1 | `feed.urls_created` increments | Submit form → check Aspire metrics view |
| 8.2 | `feed.requests` increments | Poll `/feed/{blob}` → check Aspire metrics view |
| 8.3 | `feed.source_url_requests` increments with correct `source_url` tag | Poll `/feed/{blob}` → check tag in Aspire metrics view |
| 8.4 | Traces appear for all three endpoints with correct child spans | Submit form + poll feed → check Aspire traces view |
| 8.5 | Errors recorded on spans | Submit with unreachable URL / malformed blob → check span status in Aspire |
| 8.6 | Existing tests still pass | `dotnet test` — no regressions |

---

## CI/CD

No pipeline changes required. The AppHost project is excluded from the production `Dockerfile` (it is not referenced by the main project, only the reverse). Existing CI build and test steps are unaffected.

For production deployment: set `OTEL_EXPORTER_OTLP_ENDPOINT` (and optionally `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_SERVICE_NAME`) in the deployment environment. The application will begin exporting immediately on next start.

---

## Dependencies and risks

| Item | Detail |
|---|---|
| Aspire workload | Must be installed locally (`dotnet workload install aspire`). Not needed in CI. |
| OTEL backend (prod) | Not selected — deferred. No blocker for this change. |
| `source_url` cardinality | Noted in TECH_SPEC. No mitigation needed at current scale. |
| .NET 10 + Aspire 13 | Aspire 13 targets .NET 8 but hosts .NET 10 apps without issue. Verify locally on first run. |

---

## Open questions

None. Requirements are complete and all design decisions are recorded in `openspec/changes/add-tracing-and-metrics/design.md`.

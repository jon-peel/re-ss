## Context

F# / .NET 9 / ASP.NET Core / Giraffe web application. Single process, stateless, no database. Three endpoints: `GET /`, `POST /`, `GET /feed/{blob}`. Domain pipeline: decode → guard → fetch → calculate → build. All instrumentation must use OpenTelemetry. Export destination is configurable. Aspire used for local dev.

## Goals / Non-Goals

**Goals:**
- Emit three metrics counters (feed URLs created, feed requests, per-source-URL usage).
- Produce distributed traces for all inbound requests with child spans per domain step.
- Capture exceptions on spans.
- Wire OTEL export as configurable — no hardcoded backend.
- Add an Aspire AppHost project for local dev that wires up OTEL automatically.

**Non-Goals:**
- Selecting or provisioning a production OTEL backend.
- Alerting, dashboards, or SLO definitions.
- Per-user or per-subscriber analytics.
- Changing any existing functional behaviour.

## Decisions

### D1 — Use .NET Activity API for tracing; `System.Diagnostics.Metrics` for metrics
**Decision:** Use `ActivitySource` (from `System.Diagnostics`) for tracing and `Meter` / `Counter<T>` (from `System.Diagnostics.Metrics`) for metrics. Both are the standard .NET instrumentation primitives that OTEL picks up automatically.

**Rationale:** No OTEL SDK types leak into domain or handler code. The OTEL SDK is wired only at the composition root (`Program.fs`). If the OTEL dependency is ever swapped, the instrumentation code remains unchanged.

**Alternative considered:** Inject OTEL `Tracer`/`Meter` interfaces directly into handlers. Rejected — couples handlers to the OTEL SDK and makes testing harder.

---

### D2 — Single `ActivitySource` and `Meter` for the application, registered as singletons
**Decision:** One `ActivitySource` named `"ReSS"` and one `Meter` named `"ReSS"` registered in DI and resolved by handlers and domain helpers.

**Rationale:** Keeps naming consistent and avoids multiple source registrations. The OTEL SDK is configured to listen to `"ReSS"` by name.

---

### D3 — Metrics counters: three instruments
**Decision:**
- `feed.urls_created` (`Counter<int>`) — incremented on successful `POST /` (feed URL generated).
- `feed.requests` (`Counter<int>`) — incremented on every `GET /feed/{blob}` request.
- `feed.source_url_requests` (`Counter<int>`) — incremented on every `GET /feed/{blob}` with tag `source_url` set to the decoded source URL.

**Rationale:** Directly maps to FR-20, FR-21, FR-22. Tags on `feed.source_url_requests` allow filtering/aggregation per source in any OTEL backend.

---

### D4 — Child spans created in handlers, wrapping each domain step
**Decision:** Handlers create child `Activity` spans around each domain operation: guard, fetch, calculate, build. Decode is also spanned in `getFeedHandler`. Span names follow the pattern `"ress.<operation>"` (e.g. `"ress.url_guard"`, `"ress.feed_fetch"`, `"ress.drip_calculate"`, `"ress.feed_build"`).

**Rationale:** Domain modules remain pure/untouched — no OTEL types inside domain code. Handlers already coordinate the pipeline, so they are the natural place to add spans.

**Alternative considered:** Wrap domain functions in a separate telemetry decorator layer. Rejected as over-engineering for this scale.

---

### D5 — Exceptions recorded on the active span using `Activity.SetStatus` and `RecordException`
**Decision:** Any caught exception or error result in the handler pipeline sets the span status to `Error` and records the exception details on the span via `activity.SetStatus(ActivityStatusCode.Error, message)`.

**Rationale:** Makes errors visible in trace UIs without relying solely on log correlation. Satisfies FR-25.

---

### D6 — OTEL export configured via environment variables; Aspire wires dev automatically
**Decision:** `Program.fs` calls `AddOpenTelemetry()` and configures OTLP export. The OTLP endpoint and headers are read from standard OTEL environment variables (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS`). In production, operators set these vars. In dev, Aspire injects them automatically when the app is run via the AppHost.

**Rationale:** Zero-config for local dev via Aspire; fully configurable for production without code changes. Follows the 12-factor config principle already established in the project.

---

### D7 — Aspire AppHost as a new project, dev-only
**Decision:** Add `src/ReSS.AppHost` as a .NET Aspire AppHost project. It references `src/ReSS` and configures OTEL collection (Jaeger or Aspire dashboard). Not deployed to production — `Dockerfile` is unchanged.

**Rationale:** Aspire provides a zero-friction local observability dashboard with no manual Jaeger/collector setup. Keeps dev and prod concerns cleanly separated.

## Package Versions

Pinned at time of spec (2026-02-28):

| Package | Version |
|---|---|
| `OpenTelemetry.Extensions.Hosting` | 1.15.0 |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.15.0 |
| `OpenTelemetry.Instrumentation.Http` | 1.15.0 |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.15.0 |
| `Aspire.Hosting.AppHost` | 13.1.2 |
| `Aspire.Hosting` | 13.1.2 |

AppHost project language: **C#** (Aspire convention; keeps the AppHost minimal and idiomatic).

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| `source_url` tag on `feed.source_url_requests` may cause high cardinality in some backends | Acceptable at this scale — a small number of source feeds are expected. Document as a known trade-off. |
| Aspire AppHost adds a new project to the solution | Dev-only; does not affect production build or Dockerfile. |
| Activity/Meter singletons need careful DI registration in tests | Tests that don't care about telemetry can use no-op implementations; existing tests require no changes. |

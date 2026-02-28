# Technical Specification: Observability — Tracing & Metrics

**Change:** Add OpenTelemetry tracing and metrics to the RSS Catch-Up Feed Generator
**Date:** 2026-02-28
**Status:** Approved for implementation

---

## 1. Overview

This change adds OpenTelemetry (OTEL) instrumentation to the existing ReSS application. No existing functional behaviour is changed. Three metrics counters are added, full distributed traces are produced for every inbound request, and the export destination is kept configurable so a production backend can be wired in later. Local development uses the .NET Aspire dashboard for zero-friction trace and metric visualisation.

---

## 2. Tech Stack (additions only)

The existing stack is F# / .NET 10 / ASP.NET Core / Giraffe. The following is added:

| Concern | Package | Version |
|---|---|---|
| OTEL hosting integration | `OpenTelemetry.Extensions.Hosting` | 1.15.0 |
| ASP.NET Core auto-instrumentation | `OpenTelemetry.Instrumentation.AspNetCore` | 1.15.0 |
| HttpClient auto-instrumentation | `OpenTelemetry.Instrumentation.Http` | 1.15.0 |
| OTLP exporter | `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.15.0 |
| Local dev dashboard | `Aspire.Hosting.AppHost` | 13.1.2 |
| Aspire hosting core | `Aspire.Hosting` | 13.1.2 |

**All OTEL packages are added to `src/ReSS` only. The AppHost project is dev-only and is not deployed.**

---

## 3. Architecture

### 3.1 Instrumentation primitives

The .NET standard `System.Diagnostics` primitives are used — **not** OTEL SDK types directly:

- **Tracing:** `System.Diagnostics.ActivitySource` — one instance named `"ReSS"`, registered as a singleton in DI.
- **Metrics:** `System.Diagnostics.Metrics.Meter` — one instance named `"ReSS"`, registered as a singleton in DI.

The OTEL SDK is wired once at the composition root (`Program.fs`) to listen to the `"ReSS"` source. No OTEL types appear in domain modules or handlers — they remain decoupled from the telemetry vendor.

### 3.2 New file: `Telemetry.fs`

A single new F# module `ReSS.Telemetry` is added to `src/ReSS`. It defines:

```
ActivitySource  — named "ReSS"
Meter           — named "ReSS"

Counters (all Counter<int>):
  feed.urls_created         — incremented on successful POST / (FR-20)
  feed.requests             — incremented on every GET /feed/{blob} (FR-21)
  feed.source_url_requests  — incremented on every GET /feed/{blob},
                              tagged with source_url=<decoded URL> (FR-22)
```

This module is compiled before `Handlers.fs` in the `.fsproj` order.

### 3.3 OTEL wiring (`Program.fs`)

`builder.Services.AddOpenTelemetry()` is called at startup, configuring:

- **Tracing:** listen to `"ReSS"` ActivitySource + ASP.NET Core instrumentation + HttpClient instrumentation; OTLP exporter enabled.
- **Metrics:** listen to `"ReSS"` Meter + ASP.NET Core metrics; OTLP exporter enabled.

The OTLP endpoint and headers are read from standard environment variables:

| Variable | Purpose |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP collector endpoint URL |
| `OTEL_EXPORTER_OTLP_HEADERS` | Auth headers (e.g. for cloud backends) |
| `OTEL_SERVICE_NAME` | Service name shown in traces (default: `"ReSS"`) |

In local dev, Aspire injects these automatically. In production, operators set them in the deployment environment.

### 3.4 Spans per endpoint

All child spans are created in handlers. Domain modules are untouched.

**`GET /feed/{blob}`**

| Span name | Wraps |
|---|---|
| `ress.url_decode` | `UrlCodec.decode` |
| `ress.url_guard` | `UrlGuard.validateUrl` |
| `ress.feed_fetch` | `FeedFetcher.fetchFeed` |
| `ress.drip_calculate` | `DripCalculator.calculate` |
| `ress.feed_build` | `FeedBuilder.buildFeed` |

**`POST /`**

| Span name | Wraps |
|---|---|
| `ress.url_guard` | `UrlGuard.validateUrl` |
| `ress.feed_fetch` | `FeedFetcher.fetchFeed` |
| `ress.drip_calculate` | `DripCalculator.calculate` |

`GET /` has no bespoke child spans — the ASP.NET Core instrumentation covers the request.

### 3.5 Error recording

For any caught exception or error result in the handler pipeline:

1. `activity.SetStatus(ActivityStatusCode.Error, message)` — marks the span as failed.
2. `activity.RecordException(exn)` — attaches exception details to the span (FR-25).

### 3.6 Local dev: Aspire AppHost

A new project `src/ReSS.AppHost` (C#, `Aspire.Hosting.AppHost` 13.1.2) is added to the solution. It:

- References `src/ReSS` as an Aspire resource.
- Starts the Aspire developer dashboard, which includes a built-in OTEL collector and UI for traces and metrics.
- Is **not** included in the production `Dockerfile` or any deployment artefact.

---

## 4. Data model / storage

No change. Telemetry data is exported out-of-process to an OTEL collector. Nothing is stored in the application.

---

## 5. Security considerations

- The `source_url` tag on `feed.source_url_requests` exposes upstream RSS URLs in telemetry. This is acceptable given the private/trusted-user nature of the system, but operators should treat the OTEL backend as a sensitive destination.
- No new HTTP endpoints are added to the production application.

---

## 6. Non-functional requirements

| Requirement | Approach |
|---|---|
| Standard | All instrumentation via OpenTelemetry |
| Dev experience | Aspire dashboard — zero manual setup |
| Configurable export | Standard OTEL env vars, no hardcoded backend |
| Overhead | Negligible at expected load; `ActivitySource`/`Meter` are no-ops when no listener is attached |

---

## 7. Known constraints and trade-offs

| Item | Detail |
|---|---|
| `source_url` tag cardinality | Could cause high-cardinality metric series in some backends. Acceptable at this scale — a handful of source feeds expected. |
| Aspire AppHost targets .NET 8 | Aspire 13.x targets .NET 8 as baseline but runs fine hosting a .NET 10 project. No incompatibility in practice. |
| OTEL SDK in `src/ReSS` | Adds ~4 NuGet packages to the main project. Kept to a minimum; only the packages needed for OTLP export are included. |
| Domain modules remain pure | Spans are created in handlers only. This is a deliberate trade-off: domain code stays testable and OTEL-free, at the cost of slightly more verbose handler code. |

## Why

The application currently has no observability. When something goes wrong — a slow request, a failed upstream fetch, an unexpected exception — there is no way to see where in the pipeline the problem occurred. As usage grows, even informally, this gap makes debugging and performance investigation difficult.

## What Changes

- **Metrics instrumentation**: Three counters added to the request pipeline — feed URLs created, feed requests served, and per-source-URL usage counts.
- **Distributed tracing**: Every inbound request across all three endpoints produces a trace. Each meaningful pipeline step (decode, SSRF guard, feed fetch, drip calculate, feed build) becomes a child span, with exceptions captured on the relevant span.
- **OTEL wiring**: All instrumentation is exported via OpenTelemetry. The export destination is configurable via environment/configuration — no backend is hardcoded. Local development uses Aspire's built-in OTEL support.

No existing functional behaviour is changed. No new endpoints are added (except whatever Aspire wires up in dev).

## Capabilities

### New Capabilities

- `metrics`: Three counters exposed via OTEL — feed URLs created (`feed.urls_created`), feed requests (`feed.requests`), and per-source-URL usage (`feed.source_url_requests` with a `source_url` dimension).
- `tracing`: Full pipeline traces for all endpoints with child spans per domain operation and exception recording.
- `otel-export`: Configurable OTEL export (endpoint, protocol, headers) via environment variables. Compatible with Aspire for local dev.

### Modified Capabilities

- `web-handlers`: Handlers updated to emit metrics counters and participate in trace context propagation.
- `domain pipeline`: Each domain step wrapped in a child span for tracing.

## Impact

- New OTEL-related NuGet dependencies (SDK, exporters).
- No changes to existing functional requirements (FR-01 through FR-19).
- No database, no new endpoints in production, no storage.
- Aspire project added for local development only — not deployed to production.

## 1. NuGet Dependencies

- [x] 1.1 Add OTEL NuGet packages to `src/ReSS`: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`
- [x] 1.2 Create `src/ReSS.AppHost` as a .NET Aspire AppHost project; add to `re-ss.slnx`; add project reference to `src/ReSS`

## 2. Instrumentation Primitives

- [x] 2.1 Create `src/ReSS/Telemetry.fs` — define a single `ActivitySource` named `"ReSS"` and a single `Meter` named `"ReSS"` as module-level values; define the three counters: `feedUrlsCreated`, `feedRequests`, `feedSourceUrlRequests`
- [x] 2.2 Register `Telemetry.fs` in `src/ReSS/ReSS.fsproj` compile order — before `Handlers.fs`

## 3. OTEL Wiring in Program.fs

- [x] 3.1 In `Program.fs`, call `builder.Services.AddOpenTelemetry()` — configure tracing to listen to `"ReSS"` ActivitySource plus ASP.NET Core and HttpClient instrumentation; configure metrics to listen to `"ReSS"` Meter; add OTLP exporter reading endpoint/headers from `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_EXPORTER_OTLP_HEADERS` environment variables

## 4. Metrics — POST / Handler

- [x] 4.1 In `postIndexHandler`, after successfully generating the feed URL, increment `feedUrlsCreated` counter (FR-20)

## 5. Metrics & Tracing — GET /feed/{blob} Handler

- [x] 5.1 In `getFeedHandler`, at the top of the handler, increment `feedRequests` counter (FR-21)
- [x] 5.2 After successful decode, increment `feedSourceUrlRequests` counter with tag `source_url` set to the decoded source URL string (FR-22)
- [x] 5.3 Wrap the `decode` call in a child span `"ress.url_decode"`; record error on span if decode fails
- [x] 5.4 Wrap the `validateUrl` call in a child span `"ress.url_guard"`; record error on span if guard fails
- [x] 5.5 Wrap the `fetchFeed` call in a child span `"ress.feed_fetch"`; record error on span if fetch fails
- [x] 5.6 Wrap the `calculate` call in a child span `"ress.drip_calculate"`
- [x] 5.7 Wrap the `buildFeed` call in a child span `"ress.feed_build"`

## 6. Tracing — POST / Handler

- [x] 6.1 Wrap the `validateUrl` call in a child span `"ress.url_guard"`; record error on span if guard fails
- [x] 6.2 Wrap the `fetchFeed` call in a child span `"ress.feed_fetch"`; record error on span if fetch fails
- [x] 6.3 Wrap the `calculate` call in a child span `"ress.drip_calculate"`

## 7. Aspire AppHost

- [x] 7.1 In `src/ReSS.AppHost/Program.cs` (or `.fs`), add the ReSS project as an Aspire resource and enable the Aspire dashboard with OTEL collection
- [x] 7.2 Verify local run via AppHost shows traces and metrics in the Aspire dashboard

## 8. Verification

- [ ] 8.1 Run the app via AppHost locally; submit the form and confirm `feed.urls_created` increments in the Aspire dashboard
- [ ] 8.2 Poll a `/feed/{blob}` URL and confirm `feed.requests` and `feed.source_url_requests` increment
- [ ] 8.3 Confirm traces appear for `GET /`, `POST /`, and `GET /feed/{blob}` with correct child spans visible
- [ ] 8.4 Trigger an error (malformed blob, unreachable feed) and confirm the error is recorded on the relevant span
- [x] 8.5 Confirm existing unit and integration tests still pass — no regressions

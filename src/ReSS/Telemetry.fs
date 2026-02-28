module ReSS.Telemetry

open System.Diagnostics
open System.Diagnostics.Metrics

/// The application name used as the name for both ActivitySource and Meter.
[<Literal>]
let SourceName = "ReSS"

/// ActivitySource for distributed tracing spans.
let activitySource = new ActivitySource(SourceName)

/// Meter for metrics instruments.
let meter = new Meter(SourceName)

// ---- Counters ----

/// Incremented once per successfully generated feed URL (FR-20).
let feedUrlsCreated : Counter<int> =
    meter.CreateCounter<int>("feed.urls_created", description = "Number of catch-up feed URLs generated")

/// Incremented on every GET /feed/{blob} request (FR-21).
let feedRequests : Counter<int> =
    meter.CreateCounter<int>("feed.requests", description = "Number of feed requests served")

/// Incremented on every GET /feed/{blob} request after successful decode,
/// tagged with the decoded source URL (FR-22).
let feedSourceUrlRequests : Counter<int> =
    meter.CreateCounter<int>("feed.source_url_requests", description = "Number of feed requests per source URL")

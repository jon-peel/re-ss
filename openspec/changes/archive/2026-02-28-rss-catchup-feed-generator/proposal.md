## Why

There is no existing implementation — this is a greenfield build of the RSS Catch-Up Feed Generator. The product solves a specific problem: RSS readers deliver every article in a feed immediately, overwhelming users who subscribe late to a back-catalogue-heavy feed. This generator produces a personalised drip-feed URL that unlocks articles at a configurable daily rate, starting from a chosen date, so readers can catch up at a comfortable pace.

## What Changes

- **New web application** (`src/ReSS`): A stateless ASP.NET Core / Giraffe server exposing two endpoints — a web form (`GET /`, `POST /`) and a feed endpoint (`GET /feed/{blob}`).
- **New domain library** (within `src/ReSS/Domain/`): Five pure or minimally-effectful modules — `UrlCodec`, `UrlGuard`, `DripCalculator`, `FeedFetcher`, `FeedBuilder` — with all shared types in `Types.fs`.
- **New unit + integration test project** (`tests/ReSS.Tests`): xUnit + FsCheck covering all business rules, error branches, and HTTP handler behaviour via `TestServer`.
- **New E2E test project** (`tests/ReSS.E2E`): Playwright browser automation covering the full user-facing stack.
- **New solution file** (`re-ss.sln`) and project files wiring the above together.
- **Docker image**: Multi-stage `Dockerfile` for production deployment.

No existing code is modified — this is a net-new codebase.

## Capabilities

### New Capabilities

- `url-codec`: Encode/decode opaque feed parameter blobs (Base64url, versioned `v1::` prefix). Pure, no I/O.
- `url-guard`: SSRF protection — validate user-supplied URLs against scheme rules and DNS-resolved IP ranges (loopback, link-local, RFC-1918 private). Async due to DNS.
- `drip-calculator`: Pure business logic mapping (startDate, perDay, totalItems, today) → (ShowItems n | RedirectToSource). Dimensionally type-safe via F# units of measure.
- `feed-fetcher`: Fetch and parse upstream RSS feeds via `HttpClient` with a 15-minute `IMemoryCache` TTL. Returns `Result<SyndicationFeed, FetchError>`.
- `feed-builder`: Construct a valid RSS 2.0 XML response from a `SyndicationFeed` slice, injecting an `n/t` progress indicator into the feed title. Oldest-first ordering.
- `web-handlers`: Giraffe HTTP handlers composing the domain pipeline for both the form flow and the `/feed/{blob}` feed-poll flow.
- `web-views`: Giraffe.ViewEngine server-rendered HTML for the form, result, and error states.

### Modified Capabilities

*(None — greenfield project, no existing specs.)*

## Impact

- **New NuGet dependencies** (see TECH_SPEC.md §1): Giraffe, Giraffe.ViewEngine, FSharp.Plus, Microsoft.Extensions.Caching.Memory, FsCheck.Xunit, Microsoft.AspNetCore.Mvc.Testing, Microsoft.Playwright.
- **Runtime**: .NET 9 / ASP.NET Core / Kestrel — no database, no background workers, no session state.
- **Security surface**: All user-supplied URLs pass through `UrlGuard` before any outbound HTTP request. No raw HTML output (Giraffe.ViewEngine encodes by default).
- **Deployment**: Single `dotnet publish` artifact; Docker image for containerised deployments.

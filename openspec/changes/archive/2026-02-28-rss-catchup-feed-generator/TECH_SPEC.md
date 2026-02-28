# Technical Specification: RSS Catch-Up Feed Generator

**Version:** 1.0
**Date:** 2026-02-27
**Status:** Approved

---

## 1. Tech Stack

| Concern | Choice | Rationale |
|---|---|---|
| Language | **F#** | Strongly-typed, FP-first, excellent Result/Option primitives |
| Runtime | **.NET 9** | Current stable release; good performance, broad library support |
| Web Framework | **Giraffe** | Idiomatic F# HTTP handler composition over ASP.NET Core |
| HTML Rendering | **Giraffe.ViewEngine** | Pure F# DSL for server-rendered HTML; no templating engine needed |
| FP Utilities | **FSharp.Plus** | Railway-oriented programming (`>>=`, `<!>`, `Validation`, `Result`) |
| RSS Parsing | **System.ServiceModel.Syndication** | Built-in .NET; RSS 2.0 coverage is sufficient; zero extra dependency |
| In-Memory Cache | **Microsoft.Extensions.Caching.Memory** (`IMemoryCache`) | Native .NET; TTL-based eviction; no infrastructure needed |
| Unit/Integration Tests | **xUnit + FsCheck** | Conventional tooling + property-based testing for edge cases |
| E2E Tests | **Microsoft.Playwright** (.NET bindings) | Full browser automation; tests against the running server |
| TDD Discipline | **Red-Green-Refactor** | All business rule modules built test-first |

---

## 2. Architecture Overview

The system is a **single-process, stateless web application** hosted on ASP.NET Core + Kestrel. There is no database, no session state, and no background workers. All feed configuration is carried in the URL itself.

```
┌─────────────────────────────────────────────────────┐
│                  ASP.NET Core / Kestrel             │
│                                                     │
│  ┌──────────────┐        ┌────────────────────────┐ │
│  │  GET /       │        │  GET /feed/{blob}      │ │
│  │  Web Form    │        │  Feed Endpoint         │ │
│  │  (ViewEngine)│        │  (RSS XML output)      │ │
│  └──────┬───────┘        └──────────┬─────────────┘ │
│         │                           │               │
│         ▼                           ▼               │
│  ┌─────────────────────────────────────────────┐    │
│  │              Domain / Business Logic         │    │
│  │                                             │    │
│  │  UrlCodec      FeedFetcher    DripCalculator│    │
│  │  (encode/      (HTTP +        (article      │    │
│  │   decode)       IMemoryCache)  slicing)     │    │
│  └─────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────┘
```

### Request flows

**Form submission (`POST /`)**
1. Validate form inputs (URL format, articles-per-day > 0, date parseable).
2. `UrlGuard.validateUrl` — reject private/loopback addresses with inline error.
3. Fetch + parse the upstream RSS feed via `FeedFetcher`.
4. On failure → return form with inline error (FR-05).
5. On success → encode parameters via `UrlCodec`, compute unlocked count, render result (FR-06, FR-07, FR-08).

**Feed poll (`GET /feed/{blob}`)**
1. Decode `blob` via `UrlCodec`; return 400 on malformed input (FR-17).
2. `UrlGuard.validateUrl` — reject private/loopback addresses with 400.
3. Fetch + parse upstream feed via `FeedFetcher` (cache hit or live fetch).
4. Compute unlocked article count via `DripCalculator`.
5. If fully caught up → 301 redirect to original URL (FR-14).
6. Otherwise → slice items oldest-first, build RSS XML, return 200.

---

## 3. Module Breakdown

### 3.1 `UrlCodec` module

Responsible for encoding and decoding the opaque feed parameter blob.

**Codec format:**
```
v1 :: urlencode(sourceUrl) :: articlesPerDay :: startDate(YYYY-MM-DD)
```
encoded as Base64url with `=` padding stripped.

**Example (decoded):**
```
v1::https%3A%2F%2Fexample.com%2Ffeed::3::2026-02-27
```

**Functions:**
```fsharp
encode : sourceUrl: string -> perDay: int<articles/day> -> startDate: StartDate -> string
decode : blob: string -> Result<FeedParams, DecodeError>
```

`decode` is the primary error surface — all parse/validation failures become `Result.Error`. This is a pure module with no I/O; ideal for property-based testing.

> ℹ️ **Versioning:** The `v1::` prefix allows future codec versions to be introduced without breaking existing subscriber URLs. The decoder checks the version segment first and returns `DecodeError.UnsupportedVersion` for unknown versions.

---

### 3.2 `UrlGuard` module

Validates a user-supplied URL before any HTTP request is made. Prevents SSRF by rejecting private, loopback, and link-local addresses.

**Functions:**
```fsharp
validateUrl : string -> Async<Result<string, UrlGuardError>>
```

Async because DNS resolution is required — hostname-only checks are insufficient (a public hostname could resolve to a private IP).

**Validation pipeline (in order):**
1. **Scheme** — reject anything that is not `http` or `https`
2. **Parse** — reject malformed URLs
3. **DNS resolution** — resolve the hostname to one or more IP addresses
4. **IP range check** — reject if any resolved address falls within:

| Range | Description |
|---|---|
| `127.0.0.0/8`, `::1` | Loopback |
| `169.254.0.0/16`, `fe80::/10` | Link-local (incl. cloud metadata endpoint) |
| `10.0.0.0/8` | Private class A |
| `172.16.0.0/12` | Private class B |
| `192.168.0.0/16` | Private class C |
| `0.0.0.0/8` | Unspecified |

> ⚠️ **DNS resolution is mandatory.** Checking the hostname string alone is bypassed trivially (e.g. `http://evil.com` resolving to `127.0.0.1`). The guard resolves first, then checks the resulting IPs.

> ℹ️ All resolved IPs are checked — if a hostname resolves to multiple addresses and any one is private, the request is rejected.

This module is pure logic around `Dns.GetHostAddressesAsync` with no HTTP side effects. Ideal for property-based testing: generate arbitrary addresses from each blocked range and assert rejection; generate public addresses and assert acceptance.

---

### 3.3 `FeedFetcher` module

Responsible for fetching and parsing an upstream RSS feed.

**Functions:**
```fsharp
fetchFeed : httpClient: HttpClient -> cache: IMemoryCache -> url: string
          -> Async<Result<SyndicationFeed, FetchError>>
```

- Checks `IMemoryCache` first (keyed by source URL, TTL = 15 minutes).
- On cache miss: HTTP GET the URL, validate content-type is XML, parse with `SyndicationFeed.Load`.
- Errors are wrapped into a discriminated union `FetchError` (UnreachableUrl | NotXml | ParseFailure | HttpError of int).
- `HttpClient` is injected (registered as a singleton via DI) to enable mocking in tests.

---

### 3.3 `DripCalculator` module

Pure business logic. No I/O. All functions are total and deterministic.

**Types:**
```fsharp
[<Measure>] type articles
[<Measure>] type day

type StartDate = StartDate of DateOnly

type DripResult =
    | ShowItems of count: int<articles>
    | RedirectToSource
```

**Functions:**
```fsharp
calculate : clock: Clock -> StartDate -> perDay: int<articles/day>
          -> totalItems: int<articles> -> DripResult
```

**Logic:**
```
daysElapsed = max(0, clock() - startDate)   // int<day>
unlocked    = min(daysElapsed × perDay, totalItems)  // int<articles>
result      = if unlocked >= totalItems then RedirectToSource
              else ShowItems unlocked
```

The units of measure make the arithmetic dimensionally type-safe — the compiler enforces that `int<day> × int<articles/day>` yields `int<articles>`, and that `unlocked` and `totalItems` are comparable only because they share the same unit.

> ℹ️ **Clock:** `Today` does not appear in the domain signature. Instead a `Clock` (see §4) is injected at the composition root. Production wires `fun () -> DateOnly.FromDateTime(DateTime.Today)`; tests supply a fixed date. The domain stays clean of test concerns.

---

### 3.4 `FeedBuilder` module

Constructs a valid RSS 2.0 XML response from a `SyndicationFeed` and a slice of items.

**Functions:**
```fsharp
buildFeed : feed: SyndicationFeed -> items: SyndicationItem list
          -> unlockedCount: int<articles> -> totalCount: int<articles>
          -> string  // RSS XML string
```

- Copies metadata from source feed (title, description, link, language, etc.).
- Appends `n/t` progress indicator to feed title (FR-16).
- Items are provided pre-sliced and pre-ordered (oldest-first) by the caller.
- Returns a well-formed RSS 2.0 XML string.

---

### 3.5 Web Handlers (Giraffe)

Two HTTP handlers wired via Giraffe's `choose` router:

| Route | Method | Handler |
|---|---|---|
| `/` | GET | Render empty form |
| `/` | POST | Validate → fetch → encode → render result or errors |
| `/feed/{blob}` | GET | Decode → fetch → calculate → redirect or return XML |

HTML is rendered via **Giraffe.ViewEngine** — pure F# functions, no `.cshtml` files.

---

## 4. Data Model

No persistent data. All runtime data is transient:

### Measures and value types
```fsharp
// Units of measure — erased at runtime, enforced at compile time
[<Measure>] type articles
[<Measure>] type day

// Single-case DU — prevents StartDate/DateOnly confusion at call sites
type StartDate = StartDate of DateOnly

// Clock abstraction — injected at composition root, keeps test concerns out of domain
type Clock = unit -> DateOnly
```

### `FeedParams` (decoded from URL blob)
```fsharp
type FeedParams = {
    SourceUrl  : string
    PerDay     : int<articles/day>
    StartDate  : StartDate
}
```

### `UrlGuardError`
```fsharp
type UrlGuardError =
    | NonHttpScheme
    | MalformedUrl
    | PrivateOrLoopbackAddress of resolvedIp: string
```

### `FetchError` (discriminated union)
```fsharp
type FetchError =
    | UnreachableUrl
    | NotXml
    | ParseFailure of message: string
    | HttpError    of statusCode: int
```

### `DecodeError`
```fsharp
type DecodeError =
    | InvalidBase64
    | MalformedSegments
    | UnsupportedVersion of version: string
    | InvalidPerDay
    | InvalidDate
```

### In-memory cache
- **Key:** source URL string
- **Value:** `SyndicationFeed` (parsed object)
- **TTL:** 15 minutes (absolute expiry)
- **Scope:** process lifetime; not persisted across restarts

---

## 5. API / Integration Design

### `GET /feed/{blob}`

| Condition | Response |
|---|---|
| Malformed blob | `400 Bad Request` with plain-text error |
| Source URL unreachable | `502 Bad Gateway` |
| Source not valid RSS XML | `502 Bad Gateway` with description |
| Fully caught up | `301 Moved Permanently` → original feed URL |
| Normal operation | `200 OK`, `Content-Type: application/rss+xml` |

### `POST /`

| Condition | Response |
|---|---|
| Validation errors | `200 OK` — form re-rendered with inline error messages |
| Source URL unreachable | `200 OK` — form re-rendered with specific fetch error |
| Success | `200 OK` — form rendered with generated URL + summary |

> ℹ️ **Assumption:** The form always returns HTTP 200 (standard HTML form pattern). Errors are inline, not HTTP error codes, since this is a browser-facing page.

---

## 6. RSS Feed Output Specification

The `/feed` endpoint returns RSS 2.0 XML with:

- `<channel>` metadata copied from source feed (title + " — n/t", description, link, language, etc.)
- `<item>` elements for the unlocked slice, ordered **oldest first** (reverse of typical RSS order)
- `Content-Type: application/rss+xml; charset=utf-8`

> ⚠️ **Risk:** `System.ServiceModel.Syndication` orders items as they appear in the source feed (typically newest-first). The `FeedBuilder` must explicitly reverse the item list before slicing to ensure oldest-first delivery (FR-13).

---

## 7. Security Considerations

- No authentication or authorisation required (per spec).
- The blob encoding is not cryptographically secure — it is obfuscation only. This is explicitly acceptable per requirements.
- All upstream HTTP requests are made server-side. User-supplied URLs are validated by `UrlGuard` before any request is made.

> ℹ️ **SSRF mitigated:** `UrlGuard` rejects non-HTTP/S schemes, loopback addresses, link-local ranges (including cloud metadata endpoints at `169.254.169.254`), and RFC-1918 private ranges. DNS resolution is performed during validation so hostname-based bypasses are not possible.

- No user input is rendered as raw HTML (Giraffe.ViewEngine encodes by default).

---

## 8. Non-Functional Requirements

| Concern | Approach |
|---|---|
| **Performance** | In-memory cache (15 min TTL) absorbs repeated polls. Sufficient for small user count. |
| **Observability** | ASP.NET Core default logging (`ILogger`) to stdout. Structured log on each feed fetch (hit/miss/error). |
| **Reliability** | Errors propagated as typed `Result` values throughout; no unhandled exceptions in business logic. |
| **Deployability** | Single `dotnet publish` artifact; runs on any host with .NET 9 runtime. Suitable for Fly.io, Railway, Render, or a VPS. |
| **Testability** | All business logic is pure or has injected dependencies. No static state. |

---

## 9. Project Structure

```
re-ss/
├── src/
│   └── ReSS/
│       ├── Program.fs              # ASP.NET Core entry point, DI, routing
│       ├── Handlers.fs             # Giraffe HTTP handlers
│       ├── Views.fs                # Giraffe.ViewEngine HTML
│       ├── Domain/
│       │   ├── Types.fs            # Shared types (FeedParams, errors, measures, Clock)
│       │   ├── UrlCodec.fs         # encode / decode (pure)
│       │   ├── UrlGuard.fs         # SSRF protection — scheme + DNS + IP range checks
│       │   ├── DripCalculator.fs   # calculate (pure)
│       │   ├── FeedFetcher.fs      # HTTP + cache (async, injectable)
│       │   └── FeedBuilder.fs      # RSS XML construction (pure-ish)
│       └── ReSS.fsproj
├── tests/
│   ├── ReSS.Tests/
│   │   ├── UrlCodecTests.fs        # xUnit + FsCheck property tests
│   │   ├── DripCalculatorTests.fs  # xUnit + FsCheck property tests
│   │   ├── FeedBuilderTests.fs     # xUnit unit tests
│   │   ├── UrlGuardTests.fs         # xUnit + FsCheck: IP range rejection properties
│   │   ├── FeedFetcherTests.fs     # xUnit integration tests (mock HttpClient)
│   │   ├── HandlerTests.fs         # xUnit integration tests (TestServer)
│   │   └── ReSS.Tests.fsproj
│   └── ReSS.E2E/
│       ├── FormTests.fs            # Playwright: form interaction flows
│       ├── FeedEndpointTests.fs    # Playwright: /feed endpoint scenarios
│       └── ReSS.E2E.fsproj
└── re-ss.sln
```

---

## 10. Known Constraints and Trade-offs

| Constraint | Implication |
|---|---|
| Stateless by design | No rate limiting, no per-user tracking, no analytics — all acceptable per spec |
| In-memory cache only | Cache is lost on restart; acceptable for small deployment |
| `System.ServiceModel.Syndication` | RSS 2.0 only — Atom feeds will fail at parse time (out of scope per spec) |
| Base64url codec | Not tamper-proof; opaque to casual inspection only |
| Oldest-first ordering | Source feeds are newest-first; explicit reversal required in `FeedBuilder` |
| Codec versioned at `v1` | Future breaking changes introduce a new version prefix; old URLs remain decodable by a v1-aware decoder |

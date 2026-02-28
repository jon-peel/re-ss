# Implementation Plan: RSS Catch-Up Feed Generator

**Version:** 1.0
**Date:** 2026-02-27
**Status:** Approved

---

## 1. Delivery Roadmap

```
Phase 1 — Skeleton         Phase 2 — Core Domain       Phase 3 — Web Layer
─────────────────────      ───────────────────────      ──────────────────────
Solution structure    →    UrlCodec (TDD)          →    Form UI + POST handler
Project files              UrlGuard (TDD)               /feed GET handler
DI wiring                  DripCalculator (TDD)         FeedBuilder
Smoke test passes          FeedFetcher (TDD)             Integration tests

Phase 4 — E2E              Phase 5 — Hardening
─────────────────────      ───────────────────────
Playwright setup      →    Docker image
Form flow tests            Deployment config
Feed endpoint tests        Observability / logging
Redirect tests             Final review
```

---

## 2. Phase 1 — Project Skeleton

**Goal:** A compiling, running web server with no business logic. All wiring in place so each subsequent phase drops straight into a working slot.

### Tasks

#### 1.1 Create solution and projects
```
dotnet new sln -n re-ss
dotnet new web -lang F# -n ReSS -o src/ReSS
dotnet new xunit -lang F# -n ReSS.Tests -o tests/ReSS.Tests
dotnet new xunit -lang F# -n ReSS.E2E -o tests/ReSS.E2E
dotnet sln add src/ReSS/ReSS.fsproj
dotnet sln add tests/ReSS.Tests/ReSS.Tests.fsproj
dotnet sln add tests/ReSS.E2E/ReSS.E2E.fsproj
```

#### 1.2 Add NuGet dependencies

**`src/ReSS`**
| Package | Purpose |
|---|---|
| `Giraffe` | HTTP handler composition |
| `Giraffe.ViewEngine` | Server-side HTML DSL |
| `FSharp.Plus` | Railway operators, `Result`/`Option` extensions |
| `Microsoft.Extensions.Caching.Memory` | In-process TTL cache |

**`tests/ReSS.Tests`**
| Package | Purpose |
|---|---|
| `FsCheck.Xunit` | Property-based test integration |
| `Microsoft.AspNetCore.Mvc.Testing` | In-process `TestServer` for handler tests |

**`tests/ReSS.E2E`**
| Package | Purpose |
|---|---|
| `Microsoft.Playwright` | Browser automation |
| `xunit` | Test runner host |

#### 1.3 Scaffold source files

Create all F# source files in compile order (F# requires explicit ordering in `.fsproj`):

```
src/ReSS/
  Domain/Types.fs
  Domain/UrlCodec.fs
  Domain/UrlGuard.fs
  Domain/DripCalculator.fs
  Domain/FeedFetcher.fs
  Domain/FeedBuilder.fs
  Views.fs
  Handlers.fs
  Program.fs
```

Each file gets a module declaration and a `// TODO` placeholder. The project must compile cleanly before Phase 2 begins.

#### 1.4 Wire DI and routing in `Program.fs`

```fsharp
// Registers:
// - IMemoryCache
// - IHttpClientFactory (singleton HttpClient)
// - Clock (fun () -> DateOnly.FromDateTime(DateTime.Today))
// - Giraffe middleware
// - Routes: GET /, POST /, GET /feed/{blob}
```

#### 1.5 Smoke test

Add a single `GET /` test in `ReSS.Tests/HandlerTests.fs` using `TestServer` that asserts HTTP 200. This is the baseline — if it passes, the skeleton is sound.

---

## 3. Phase 2 — Core Domain (TDD)

**Goal:** All business logic modules fully implemented and tested. No HTTP concerns. Each module is built strictly RGR: write a failing test, make it pass, refactor.

> ℹ️ **RGR discipline:** No implementation code is written before a failing test exists. Each task below follows: Red → Green → Refactor → next test.

---

### 2.1 `Types.fs`

Define all shared types up front — no logic, just type declarations:

- `[<Measure>] type articles`
- `[<Measure>] type day`
- `type StartDate = StartDate of DateOnly`
- `type Clock = unit -> DateOnly`
- `type FeedParams`
- `type DecodeError`
- `type UrlGuardError`
- `type FetchError`
- `type DripResult`

No tests for this file — it is pure type definitions.

---

### 2.2 `UrlCodec` — TDD

**Test file:** `tests/ReSS.Tests/UrlCodecTests.fs`

Build in this order, one failing test at a time:

| # | Test | Kind |
|---|---|---|
| 1 | `encode` produces a non-empty string | Unit |
| 2 | `decode` of a fresh `encode` round-trips all fields | Property (FsCheck) |
| 3 | `decode` of a blob with wrong version returns `UnsupportedVersion` | Unit |
| 4 | `decode` of invalid Base64 returns `InvalidBase64` | Unit |
| 5 | `decode` with wrong segment count returns `MalformedSegments` | Unit |
| 6 | `decode` with non-integer perDay returns `InvalidPerDay` | Unit |
| 7 | `decode` with unparseable date returns `InvalidDate` | Unit |
| 8 | `encode → decode` round-trip holds for arbitrary URLs, perDay, dates | Property (FsCheck) |
| 9 | Encoded string contains no `=` padding | Unit |
| 10 | Source URLs containing special characters survive the round-trip | Property (FsCheck) |

**FsCheck generators needed:**
- Arbitrary valid URL strings (http/https, with path/query components)
- Arbitrary `int<articles/day>` in range 1–1000
- Arbitrary `DateOnly` in a sensible range (2000-01-01 to 2100-12-31)

---

### 2.3 `UrlGuard` — TDD

**Test file:** `tests/ReSS.Tests/UrlGuardTests.fs`

| # | Test | Kind |
|---|---|---|
| 1 | `ftp://` scheme is rejected with `NonHttpScheme` | Unit |
| 2 | `file://` scheme is rejected with `NonHttpScheme` | Unit |
| 3 | Malformed string is rejected with `MalformedUrl` | Unit |
| 4 | Loopback `127.0.0.1` is rejected with `PrivateOrLoopbackAddress` | Unit |
| 5 | Loopback `127.x.x.x` range is always rejected | Property (FsCheck) |
| 6 | `::1` (IPv6 loopback) is rejected | Unit |
| 7 | `169.254.x.x` (link-local / metadata) is always rejected | Property (FsCheck) |
| 8 | `10.x.x.x` range is always rejected | Property (FsCheck) |
| 9 | `172.16–31.x.x` range is always rejected | Property (FsCheck) |
| 10 | `192.168.x.x` range is always rejected | Property (FsCheck) |
| 11 | A known public address is accepted | Unit (uses real DNS — mark as integration) |

> ℹ️ Tests 1–10 use constructed `IPAddress` values directly against the IP-range predicate function, keeping them fast and deterministic. Test 11 is marked `[<Trait("Category","Integration")>]` and can be excluded from fast unit runs.

---

### 2.4 `DripCalculator` — TDD

**Test file:** `tests/ReSS.Tests/DripCalculatorTests.fs`

| # | Test | Kind |
|---|---|---|
| 1 | Start date in the future → `ShowItems 0` | Unit |
| 2 | Start date is today → `ShowItems perDay` (day 1) | Unit |
| 3 | Elapsed days × perDay < total → `ShowItems n` | Unit |
| 4 | Elapsed days × perDay = total → `RedirectToSource` | Unit |
| 5 | Elapsed days × perDay > total → `RedirectToSource` (capped) | Unit |
| 6 | perDay = 1, arbitrary elapsed days → unlocked never exceeds total | Property (FsCheck) |
| 7 | Arbitrary valid inputs → unlocked is always in range [0, total] | Property (FsCheck) |
| 8 | Arbitrary valid inputs → result is always one of two DU cases | Property (FsCheck) |
| 9 | `RedirectToSource` iff unlocked ≥ total | Property (FsCheck) |

**FsCheck generators needed:**
- `DripParams`-equivalent: `startDate: DateOnly`, `perDay: int<articles/day>` (1–100), `totalItems: int<articles>` (1–10000), `today: DateOnly`

> ℹ️ `Clock` is trivially stubbed in tests as `fun () -> fixedDate`. No mocking framework needed.

---

### 2.5 `FeedFetcher` — TDD

**Test file:** `tests/ReSS.Tests/FeedFetcherTests.fs`

`HttpClient` is injected via a test `HttpMessageHandler` stub — no live network calls in unit tests.

| # | Test | Kind |
|---|---|---|
| 1 | Valid RSS URL returns `Ok SyndicationFeed` | Unit (stubbed handler) |
| 2 | Non-XML response returns `Error NotXml` | Unit |
| 3 | 404 response returns `Error (HttpError 404)` | Unit |
| 4 | Network exception returns `Error UnreachableUrl` | Unit |
| 5 | Invalid XML returns `Error (ParseFailure _)` | Unit |
| 6 | Second call within TTL returns cached result (handler called once) | Unit |
| 7 | Call after TTL expiry re-fetches (handler called twice) | Unit |

---

### 2.6 `FeedBuilder` — TDD

**Test file:** `tests/ReSS.Tests/FeedBuilderTests.fs`

| # | Test | Kind |
|---|---|---|
| 1 | Output is valid XML | Unit |
| 2 | Output is parseable as RSS 2.0 | Unit |
| 3 | Feed title includes `n/t` progress indicator | Unit |
| 4 | Original feed metadata (description, link, language) is preserved | Unit |
| 5 | Item count in output equals the requested unlocked count | Unit |
| 6 | Items are in oldest-first order | Unit |
| 7 | Items are in oldest-first order for arbitrary item lists | Property (FsCheck) |
| 8 | When unlocked = 0, output has zero items | Unit |
| 9 | Content-Type of response is `application/rss+xml` | Unit |

---

## 4. Phase 3 — Web Layer

**Goal:** Both HTTP endpoints working end-to-end through the full domain pipeline. Integration tests via `TestServer`.

---

### 3.1 `Views.fs` — Form UI

Implement the Giraffe.ViewEngine HTML for:
- Empty form (GET `/`) — source URL field, per-day field, collapsed advanced section with start date
- Result state — generated feed URL, copy button, `n/t` summary message
- Error state — inline error message per failing field or fetch error

No tests at this layer — covered by Playwright E2E in Phase 4.

---

### 3.2 `Handlers.fs` — HTTP Handlers

Implement the three Giraffe handlers, composing domain modules via FSharp.Plus railway operators:

**`GET /`** — render empty form view.

**`POST /`**
```
formData
  >>= validateFormFields        // Result: field-level errors
  >>= UrlGuard.validateUrl      // Result: SSRF guard
  >>= FeedFetcher.fetchFeed     // Result: SyndicationFeed
  |> map (encode + summarise)   // build output URL + n/t
  |> renderView
```

**`GET /feed/{blob}`**
```
blob
  >>= UrlCodec.decode           // Result: FeedParams
  >>= UrlGuard.validateUrl      // Result: SSRF guard
  >>= FeedFetcher.fetchFeed     // Result: SyndicationFeed
  |> map DripCalculator.calculate
  |> either redirect buildFeed
```

---

### 3.3 Handler Integration Tests

**Test file:** `tests/ReSS.Tests/HandlerTests.fs`

Uses `Microsoft.AspNetCore.Mvc.Testing` with a test `WebApplicationFactory`. `HttpClient` and `Clock` are replaced with test doubles via DI override.

| # | Test |
|---|---|
| 1 | `GET /` returns 200 with an HTML form |
| 2 | `POST /` with valid inputs returns 200 with generated URL in body |
| 3 | `POST /` with missing URL returns 200 with inline error |
| 4 | `POST /` with invalid RSS URL returns 200 with fetch error message |
| 5 | `POST /` with private IP URL returns 200 with guard error message |
| 6 | `GET /feed/{validBlob}` returns 200 with `application/rss+xml` |
| 7 | `GET /feed/{validBlob}` when caught up returns 301 to source URL |
| 8 | `GET /feed/{malformedBlob}` returns 400 |
| 9 | `GET /feed/{blobWithPrivateUrl}` returns 400 |
| 10 | `GET /feed/{validBlob}` with unreachable source returns 502 |

---

## 5. Phase 4 — End-to-End Tests (Playwright)

**Goal:** Automated browser tests against the real running server. Covers the full stack including UI interactions.

### Setup

- `ReSS.E2E` project starts the app via `WebApplicationFactory` or a local process on a fixed port.
- Playwright browser is launched in headless mode.
- Tests are tagged `[<Trait("Category","E2E")>]` and excluded from the default unit test run.

### Test Scenarios

**`FormTests.fs`**

| # | Scenario |
|---|---|
| 1 | Page loads with form visible and submit button enabled |
| 2 | Submitting empty form shows validation errors |
| 3 | Entering a valid RSS URL and per-day value → generated URL appears |
| 4 | Copy button copies generated URL to clipboard |
| 5 | Summary message shows correct `n of t articles ready` text |
| 6 | Advanced section is collapsed by default |
| 7 | Expanding advanced section reveals start date field |
| 8 | Setting a future start date → summary shows `0 of t articles ready` |
| 9 | Entering an unreachable URL shows descriptive fetch error inline |
| 10 | Entering a non-RSS URL shows `URL did not return XML` error |

**`FeedEndpointTests.fs`**

| # | Scenario |
|---|---|
| 1 | Valid blob returns RSS XML with correct item count |
| 2 | Feed title contains `n/t` progress indicator |
| 3 | Items are in oldest-first order |
| 4 | Fully caught-up blob redirects to original feed URL |
| 5 | Malformed blob returns 400 |
| 6 | Future start date returns feed with zero items |

---

## 6. Phase 5 — Hardening and Deployment

### 6.1 Dockerfile

```dockerfile
# Multi-stage build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/ReSS/ReSS.fsproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ReSS.dll"]
```

### 6.2 Configuration

All runtime config via environment variables (12-factor):

| Variable | Default | Purpose |
|---|---|---|
| `ASPNETCORE_URLS` | `http://+:8080` | Bind address |
| `CACHE_TTL_MINUTES` | `15` | Override cache TTL |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Log level control |

### 6.3 Observability

Structured logging via ASP.NET Core's built-in `ILogger` to stdout:

| Event | Level | Fields |
|---|---|---|
| Feed cache hit | `Debug` | `sourceUrl` |
| Feed cache miss + fetch | `Information` | `sourceUrl`, `itemCount`, `elapsed` |
| Fetch error | `Warning` | `sourceUrl`, `error` |
| UrlGuard rejection | `Warning` | `url`, `resolvedIp` |
| Decode error on `/feed` | `Warning` | `blob`, `error` |

---

## 7. Testing Strategy Summary

| Layer | Tool | Scope | Run in CI |
|---|---|---|---|
| Unit — pure domain | xUnit + FsCheck | `UrlCodec`, `DripCalculator`, `FeedBuilder`, `UrlGuard` IP predicates | ✅ Always |
| Integration — HTTP | xUnit + `TestServer` | Handlers, `FeedFetcher` (stubbed), full pipeline | ✅ Always |
| E2E — browser | Playwright | Full stack, real browser | ✅ On merge to main |

**Coverage target:** No hard percentage. Every business rule in `DripCalculator`, `UrlCodec`, and `UrlGuard` must have at least one FsCheck property test covering its invariant. All `Result` error branches must have at least one explicit unit test.

---

## 8. CI/CD

### Pipeline (GitHub Actions or equivalent)

```
on: push / pull_request

jobs:
  build:
    - dotnet restore
    - dotnet build --no-restore

  test-unit:
    needs: build
    - dotnet test --filter "Category!=Integration&Category!=E2E"

  test-integration:
    needs: build
    - dotnet test --filter "Category=Integration"

  test-e2e:
    needs: [test-unit, test-integration]
    - dotnet build
    - playwright install --with-deps chromium
    - dotnet test --filter "Category=E2E"

  docker:
    needs: test-e2e
    on: merge to main only
    - docker build
    - docker push (registry of choice)
```

---

## 9. Dependencies, Risks and Open Questions

### Dependencies
| Dependency | Risk | Mitigation |
|---|---|---|
| Third-party RSS feeds | Feed may be slow or down at request time | `FetchError` propagated cleanly; 15-min cache absorbs flakiness |
| DNS resolution in `UrlGuard` | Adds latency to each new URL validation | Acceptable — only on form submit and first `/feed` call per cache window |
| Playwright in CI | Requires browser binary install | `playwright install` step in CI; use official MS action if available |

### Risks
| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Source feed changes item order | Low | Medium | `FeedBuilder` always sorts by `PublishDate` ascending, not by position |
| Source feed has no `PublishDate` on items | Medium | Medium | Fall back to document order (reversed); note in logs |
| Encoded URL grows too long for some RSS readers | Low | Low | Typical URL well under 500 chars; no mitigation needed |

### Open Questions
None outstanding.

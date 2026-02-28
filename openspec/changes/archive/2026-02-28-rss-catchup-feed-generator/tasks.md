## 1. Project Skeleton

- [x] 1.1 Create solution and project files: `dotnet new sln -n re-ss`, `dotnet new web -lang F# -n ReSS -o src/ReSS`, `dotnet new xunit -lang F# -n ReSS.Tests -o tests/ReSS.Tests`, `dotnet new xunit -lang F# -n ReSS.E2E -o tests/ReSS.E2E`, then `dotnet sln add` all three
- [x] 1.2 Add NuGet packages to `src/ReSS`: Giraffe, Giraffe.ViewEngine, FSharp.Plus, Microsoft.Extensions.Caching.Memory
- [x] 1.3 Add NuGet packages to `tests/ReSS.Tests`: FsCheck.Xunit, Microsoft.AspNetCore.Mvc.Testing
- [x] 1.4 Add NuGet packages to `tests/ReSS.E2E`: Microsoft.Playwright
- [x] 1.5 Scaffold all F# source files in compile order in `src/ReSS`: `Domain/Types.fs`, `Domain/UrlCodec.fs`, `Domain/UrlGuard.fs`, `Domain/DripCalculator.fs`, `Domain/FeedFetcher.fs`, `Domain/FeedBuilder.fs`, `Views.fs`, `Handlers.fs`, `Program.fs` — each with module declaration and `// TODO` placeholder
- [x] 1.6 Wire DI and routing in `Program.fs`: register `IMemoryCache`, `HttpClient` (singleton), `Clock` (`fun () -> DateOnly.FromDateTime(DateTime.Today)`), Giraffe middleware, and routes (`GET /`, `POST /`, `GET /feed/{blob}`)
- [x] 1.7 Add smoke test in `tests/ReSS.Tests/HandlerTests.fs` using `TestServer` asserting `GET /` returns HTTP 200 — verify solution compiles and test passes

## 2. Domain — Types

- [x] 2.1 Define all shared types in `Domain/Types.fs`: `[<Measure>] type articles`, `[<Measure>] type day`, `StartDate`, `Clock`, `FeedParams`, `DecodeError`, `UrlGuardError`, `FetchError`, `DripResult`

## 3. Domain — UrlCodec (TDD)

- [x] 3.1 Create `tests/ReSS.Tests/UrlCodecTests.fs` — write failing test: `encode` produces a non-empty string
- [x] 3.2 Implement `UrlCodec.encode` in `Domain/UrlCodec.fs` to make test 3.1 pass
- [x] 3.3 Write failing test: encoded string contains no `=` padding — make pass
- [x] 3.4 Write failing test: `decode` of invalid Base64 returns `InvalidBase64` — implement decoder skeleton to make pass
- [x] 3.5 Write failing test: `decode` with wrong segment count returns `MalformedSegments` — make pass
- [x] 3.6 Write failing test: `decode` with unknown version returns `UnsupportedVersion` — make pass
- [x] 3.7 Write failing test: `decode` with non-integer perDay returns `InvalidPerDay` — make pass
- [x] 3.8 Write failing test: `decode` with unparseable date returns `InvalidDate` — make pass
- [x] 3.9 Write FsCheck property: `encode → decode` round-trips all fields for arbitrary valid inputs — make pass
- [x] 3.10 Write FsCheck property: source URLs with special characters survive round-trip — make pass; refactor

## 4. Domain — UrlGuard (TDD)

- [x] 4.1 Create `tests/ReSS.Tests/UrlGuardTests.fs` — write failing tests for scheme rejection (`ftp://`, `file://`) — implement `UrlGuard.validateUrl` scheme check to make pass
- [x] 4.2 Write failing test: malformed URL returns `MalformedUrl` — make pass
- [x] 4.3 Implement the IP-range predicate as an internal pure function (no DNS) — write FsCheck properties for all blocked ranges (`127.0.0.0/8`, `169.254.0.0/16`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`) and unit tests for `::1` — make all pass
- [x] 4.4 Wire DNS resolution into `validateUrl`: resolve hostname then apply IP-range predicate — write integration test (marked `[<Trait("Category","Integration")>]`) for a known public address — make pass
- [x] 4.5 Write failing test: any resolved IP in blocked range causes rejection — make pass; refactor

## 5. Domain — DripCalculator (TDD)

- [x] 5.1 Create `tests/ReSS.Tests/DripCalculatorTests.fs` — write failing unit tests for future start date (`ShowItems 0`), today's start date (`ShowItems perDay`), partial progress, exact catch-up (`RedirectToSource`), and over-elapsed (`RedirectToSource`)
- [x] 5.2 Implement `DripCalculator.calculate` in `Domain/DripCalculator.fs` using units of measure — make all unit tests pass
- [x] 5.3 Write FsCheck properties: unlocked always in `[0, total]`, result always a valid DU case, `RedirectToSource` iff unlocked ≥ total — make all pass; refactor

## 6. Domain — FeedFetcher (TDD)

- [x] 6.1 Create `tests/ReSS.Tests/FeedFetcherTests.fs` with a stubbed `HttpMessageHandler` helper — write failing tests for: valid RSS returns `Ok`, non-XML returns `NotXml`, 404 returns `HttpError 404`, network exception returns `UnreachableUrl`, invalid XML returns `ParseFailure`
- [x] 6.2 Implement `FeedFetcher.fetchFeed` in `Domain/FeedFetcher.fs` to make all 6.1 tests pass
- [x] 6.3 Write failing test: second call within TTL uses cache (handler called once) — implement caching logic to make pass
- [x] 6.4 Write failing test: call after TTL re-fetches (handler called twice) — make pass; refactor

## 7. Domain — FeedBuilder (TDD)

- [x] 7.1 Create `tests/ReSS.Tests/FeedBuilderTests.fs` — write failing unit tests for: output is valid XML, parseable as RSS 2.0, title contains `n/t`, metadata preserved, item count matches slice, zero items when unlocked = 0
- [x] 7.2 Implement `FeedBuilder.buildFeed` in `Domain/FeedBuilder.fs` — make all 7.1 tests pass
- [x] 7.3 Write failing test: items are oldest-first — implement sort by `PublishDate` ascending with `DateTimeOffset.MinValue` fallback — make pass
- [x] 7.4 Write FsCheck property: oldest-first ordering holds for arbitrary item lists — make pass; refactor

## 8. Web Layer — Views

- [x] 8.1 Implement `Views.fs` using Giraffe.ViewEngine: empty form state (source URL, per-day, collapsed advanced section with start date)
- [x] 8.2 Implement result state view: generated feed URL, summary message (`n of t articles ready`)
- [x] 8.3 Implement error state view: inline per-field errors and form-level fetch/guard error messages

## 9. Web Layer — Handlers

- [x] 9.1 Implement `GET /` handler in `Handlers.fs` — renders empty form view
- [x] 9.2 Implement `POST /` handler: parse form data → validate fields → `UrlGuard.validateUrl` → `FeedFetcher.fetchFeed` → `UrlCodec.encode` + summarise → render result or error view; use FSharp.Plus railway operators
- [x] 9.3 Implement `GET /feed/{blob}` handler: `UrlCodec.decode` → `UrlGuard.validateUrl` → `FeedFetcher.fetchFeed` → `DripCalculator.calculate` → 301 redirect or `FeedBuilder.buildFeed` → 200 RSS XML; return 400 / 502 on errors

## 10. Handler Integration Tests

- [x] 10.1 Extend `tests/ReSS.Tests/HandlerTests.fs` with `WebApplicationFactory` setup — override `HttpClient` and `Clock` via DI
- [x] 10.2 Write and pass integration tests: `GET /` returns 200 with HTML form; `POST /` valid → 200 with generated URL; `POST /` missing URL → 200 with inline error; `POST /` fetch error → 200 with fetch error message; `POST /` private IP → 200 with guard error
- [x] 10.3 Write and pass integration tests: `GET /feed/{validBlob}` → 200 `application/rss+xml`; caught-up blob → 301; malformed blob → 400; private URL blob → 400; unreachable source → 502

## 11. E2E Tests (Playwright)

- [x] 11.1 Configure `tests/ReSS.E2E` project: start app via `WebApplicationFactory` or local process, Playwright headless setup, all tests tagged `[<Trait("Category","E2E")>]`
- [x] 11.2 Implement `FormTests.fs`: page loads with form visible; empty submit shows errors; valid RSS URL → generated URL appears; copy button; summary message correct; advanced section collapsed by default; expand reveals start date; future start date → 0 articles; unreachable URL error; non-RSS URL error
- [x] 11.3 Implement `FeedEndpointTests.fs`: valid blob → correct item count; feed title has `n/t`; items oldest-first; caught-up → redirect; malformed blob → 400; future start date → 0 items

## 12. Hardening and Deployment

- [x] 12.1 Write multi-stage `Dockerfile`: SDK build stage → ASP.NET runtime stage, `EXPOSE 8080`, `ENTRYPOINT ["dotnet", "ReSS.dll"]`
- [x] 12.2 Add environment variable configuration: `ASPNETCORE_URLS` (default `http://+:8080`), `CACHE_TTL_MINUTES` (default `15`), `ASPNETCORE_ENVIRONMENT` (default `Production`)
- [x] 12.3 Add structured `ILogger` logging: cache hit (Debug), cache miss + fetch (Information with `sourceUrl`, `itemCount`, `elapsed`), fetch error (Warning), UrlGuard rejection (Warning), decode error (Warning)
- [x] 12.4 Write GitHub Actions CI pipeline: `build` → `test-unit` (filter `Category!=Integration&Category!=E2E`) → `test-integration` (filter `Category=Integration`) → `test-e2e` (filter `Category=E2E`) → `docker` (merge to main only)

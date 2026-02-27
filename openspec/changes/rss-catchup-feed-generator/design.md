## Context

Greenfield F# / .NET 9 web application. No existing codebase to migrate. The system is a stateless, single-process HTTP server: all feed configuration is carried in the URL itself as an opaque Base64url blob, so there is no database and no session state. The tech stack (Giraffe, FSharp.Plus, System.ServiceModel.Syndication, IMemoryCache) is pre-approved in TECH_SPEC.md.

## Goals / Non-Goals

**Goals:**
- Implement all five domain modules (`UrlCodec`, `UrlGuard`, `DripCalculator`, `FeedFetcher`, `FeedBuilder`) as pure or minimally-effectful F# — no unhandled exceptions, all errors as typed `Result` values.
- Deliver two working HTTP endpoints (form UI + RSS feed) composed from those modules via Giraffe handlers.
- Full test coverage: unit (xUnit + FsCheck property tests) for all business rules, integration via `TestServer`, E2E via Playwright.
- Production-ready Docker image; configuration via environment variables (12-factor).

**Non-Goals:**
- Authentication / authorisation.
- Persistent storage or database.
- Rate limiting or per-user analytics.
- Atom feed support (RSS 2.0 only, per spec).
- Cryptographic URL signing (Base64url is obfuscation only, explicitly acceptable).

## Decisions

### D1 — Single-project domain, no separate class library
**Decision:** All domain modules live under `src/ReSS/Domain/` within the one deployable project rather than a separate `ReSS.Domain` class library.

**Rationale:** The domain has no consumers other than the web handlers in the same process. A separate project adds `.fsproj` ceremony and a project reference with no architectural benefit at this scale. The internal module boundary is enforced by F#'s file-order compilation model — `Domain/*.fs` files are listed before `Handlers.fs` in the `.fsproj`, so handlers can reference domain modules but not vice-versa.

**Alternative considered:** `ReSS.Core` class library referenced by `ReSS` web project. Rejected as over-engineering for a single-process app.

---

### D2 — `Result`-based error handling throughout; no exceptions in domain code
**Decision:** All domain functions return `Result<'T, 'E>` (or `Async<Result<...>>`). Exceptions are only allowed to propagate from third-party library calls (`SyndicationFeed.Load`, `HttpClient`) and must be caught at the module boundary and translated into the appropriate DU case.

**Rationale:** Makes all failure modes visible in the type system. Enables railway-oriented composition via FSharp.Plus (`>>=`, `<!>`) in handlers without defensive `try/catch` scattered through business logic.

**Alternative considered:** Exception-based with a top-level Giraffe error handler. Rejected — hides domain errors and makes testing error paths harder.

---

### D3 — `Clock` abstraction injected at composition root
**Decision:** `DripCalculator.calculate` takes a `Clock = unit -> DateOnly` parameter. The production app wires `fun () -> DateOnly.FromDateTime(DateTime.Today)`; tests supply a fixed date.

**Rationale:** Keeps `DateTime.Today` out of the domain. Makes the calculator a pure, deterministic function — trivially testable without mocking frameworks.

---

### D4 — `HttpClient` injected via DI; `IMemoryCache` injected into `FeedFetcher`
**Decision:** `FeedFetcher.fetchFeed` takes `HttpClient` and `IMemoryCache` as explicit parameters. Both are registered in the ASP.NET Core DI container and resolved at the composition root.

**Rationale:** Enables unit testing with a stubbed `HttpMessageHandler` and a real (or mock) `IMemoryCache`, without a mocking framework. Consistent with the "inject at boundary" principle.

---

### D5 — Codec format versioned with `v1::` prefix
**Decision:** The blob encodes as `Base64url(v1::urlencode(sourceUrl)::perDay::YYYY-MM-DD)`. The decoder checks the version segment first and returns `DecodeError.UnsupportedVersion` for unknown versions.

**Rationale:** Allows future breaking codec changes (e.g., additional fields) without invalidating existing subscriber URLs. Old URLs remain decodable by a version-aware decoder.

---

### D6 — Oldest-first ordering via explicit sort by `PublishDate` in `FeedBuilder`
**Decision:** `FeedBuilder` sorts items by `PublishDate` ascending before slicing, rather than relying on the source feed's document order.

**Rationale:** Source feeds are typically newest-first. Reversing document order is fragile if a feed publishes items out of chronological order. Sorting by `PublishDate` is more robust. Fallback: if an item has no `PublishDate`, use `DateTimeOffset.MinValue` (placed first).

---

### D7 — `UrlGuard` performs DNS resolution; all resolved IPs checked
**Decision:** `UrlGuard.validateUrl` resolves the hostname via `Dns.GetHostAddressesAsync` and rejects the URL if **any** resolved IP falls in a blocked range.

**Rationale:** Hostname-string checks alone are trivially bypassed (a public domain can resolve to a private IP — DNS rebinding / SSRF). Checking all resolved IPs is a defence-in-depth requirement.

---

### D8 — Giraffe.ViewEngine for all HTML; no `.cshtml` / Razor
**Decision:** All HTML is produced by Giraffe.ViewEngine F# combinators. No Razor templates, no `.cshtml` files.

**Rationale:** Type-safe, refactorable, no template-engine dependency. Consistent with the all-F# codebase ethos.

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| Source feed has no `PublishDate` on items | Sort by `PublishDate`, fallback to `DateTimeOffset.MinValue`; log a warning per item missing a date. |
| `System.ServiceModel.Syndication` parses RSS 2.0 only | Atom feeds will fail at `SyndicationFeed.Load`; caught as `ParseFailure`, returned to caller as a descriptive error. Explicitly out of scope. |
| In-memory cache lost on restart | Acceptable per spec — 15-min TTL means at most 15 min of repeated upstream fetches after a restart. |
| DNS resolution adds latency to `UrlGuard` | Only executed on form `POST` and first `/feed` call per cache window. Acceptable. |
| Base64url blob not tamper-proof | Explicitly acceptable — the codec is obfuscation only, not a security boundary. |
| Long encoded URLs for feeds with complex query strings | Typical URL is well under 500 chars. No mitigation needed. |

## Open Questions

None — all decisions resolved in TECH_SPEC.md and IMPLEMENTATION_PLAN.md prior to this proposal.

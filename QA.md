# QA Review — RSS Catch-Up Feed Generator

**Reviewer:** Quinn (QA)
**Date:** 2026-02-28
**Status:** ❌ BLOCKED — 3 issues must be resolved before merge

---

## How to use this file

Work through the **Blocking Issues** first. Each issue has a Problem, the exact change
required, and an Acceptance Criteria checklist. Do not mark a checkbox until the code
change and its test(s) are both in place.

The **Recommended Fixes** are not blocking but should be done in the same pass where
practical.

---

## Blocking Issues

---

### B-1 — Dockerfile targets .NET 9 but project targets `net10.0`

**File:** `Dockerfile`, lines 1 and 11

**Problem:**
The project's `<TargetFramework>` is `net10.0` and all NuGet packages pin to `10.x`
versions (`Microsoft.Extensions.Caching.Memory 10.0.3`, `System.ServiceModel.Syndication
10.0.3`). The Dockerfile pulls `.NET 9` SDK and runtime images. The `dotnet publish`
step inside Docker will fail with a TFM mismatch error. Every Docker build on CI and
locally is broken.

**Fix:**
Update both `FROM` lines in `Dockerfile`:

```dockerfile
# Before
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
...
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# After
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
...
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
```

No other changes are needed. The rest of the Dockerfile is correct.

**Acceptance criteria:**
- [ ] `docker build -t re-ss:test .` completes without error locally or in CI.
- [ ] The built image runs and `GET /` returns HTTP 200.

---

### B-2 — `0.0.0.0` is not blocked by the SSRF guard

**File:** `src/ReSS/Domain/UrlGuard.fs`

**Problem:**
`http://0.0.0.0/internal-api` passes `isPrivateOrLoopback` because `0.0.0.0` does not
fall inside any of the five checked ranges (`127.x`, `169.254.x`, `10.x`, `172.16-31.x`,
`192.168.x`). On Linux, connecting to `0.0.0.0` on a TCP port binds to the wildcard
interface and connects to localhost. A crafted feed URL using this address bypasses the
guard and reaches internal services. There are no existing tests covering this address.

**Fix:**
Add an exact equality check inside the `AddressFamily.InterNetwork` branch of
`isPrivateOrLoopback`:

```fsharp
| AddressFamily.InterNetwork ->
    ip.Equals(IPAddress.Any)         ||   // 0.0.0.0 — connects to localhost on Linux
    inRange ip "127.0.0.0"   8       ||   // loopback
    inRange ip "169.254.0.0" 16      ||   // link-local
    inRange ip "10.0.0.0"    8       ||   // RFC1918 class A
    inRange ip "172.16.0.0"  12      ||   // RFC1918 class B
    inRange ip "192.168.0.0" 16           // RFC1918 class C
```

`IPAddress.Any` is the pre-defined `0.0.0.0` constant — no parsing required.

Then add a unit test in `tests/ReSS.Tests/UrlGuardTests.fs`:

```fsharp
[<Fact>]
let ``0.0.0.0 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("0.0.0.0")))

[<Fact>]
let ``http URL with 0.0.0.0 literal IP rejected`` () =
    let result = validateUrl "http://0.0.0.0/feed" |> Async.RunSynchronously
    match result with
    | Error (PrivateOrLoopbackAddress _) -> ()
    | other -> Assert.Fail(sprintf "Expected PrivateOrLoopbackAddress, got %A" other)
```

**Acceptance criteria:**
- [ ] `isPrivateOrLoopback (IPAddress.Parse("0.0.0.0"))` returns `true`.
- [ ] `validateUrl "http://0.0.0.0/feed"` returns `Error (PrivateOrLoopbackAddress _)`.
- [ ] Both unit tests above are added to `UrlGuardTests.fs` and pass.
- [ ] All existing `UrlGuardTests` still pass.

---

### B-3 — DNS fail-open has no test coverage and no documented mitigation

**File:** `src/ReSS/Domain/UrlGuard.fs`, lines 52–59

**Problem:**
When DNS resolution fails (NXDOMAIN, timeout, network error), the guard returns an empty
`ips` array, `Array.tryFind` finds nothing to block, and `validateUrl` returns `Ok uri`.
The URL passes the SSRF guard silently. The code comment acknowledges this but it is
categorised as an "accepted trade-off" with no test and no operator documentation.

The risk: if an attacker can cause DNS to fail for their hostname at guard-check time (e.g.
temporary NXDOMAIN), the guard is bypassed. The subsequent `fetchFeed` will likely fail with
`UnreachableUrl`, but that is not guaranteed if the hostname resolves differently on retry.

This needs two things: a test that locks in the fail-open behaviour so it cannot silently
change to fail-closed (breaking prod), and a note in `REQUIREMENTS.md` or `appsettings.json`
so operators know this is a known limitation.

**Fix — Part 1: add a test that documents the behaviour**

Add to `tests/ReSS.Tests/UrlGuardTests.fs`:

```fsharp
[<Fact>]
let ``unresolvable hostname passes guard (fail-open)`` () =
    // A hostname that cannot resolve should pass the guard rather than
    // rejecting the URL, because we cannot confirm it is private.
    // The subsequent fetch will fail with UnreachableUrl.
    // This is a documented, accepted trade-off — see REQUIREMENTS.md §Security.
    let result =
        validateUrl "https://this-hostname-does-not-exist.invalid/feed"
        |> Async.RunSynchronously
    // Either Ok (fail-open, expected) or a guard error is acceptable;
    // what must NOT happen is an unhandled exception.
    match result with
    | Ok _
    | Error (PrivateOrLoopbackAddress _)
    | Error MalformedUrl -> ()
    | Error e -> Assert.Fail(sprintf "Unexpected guard error for unresolvable host: %A" e)
```

**Fix — Part 2: add a note to `REQUIREMENTS.md`**

Append the following section (or merge into an existing Security section):

```markdown
## Security — Known Limitations

### SSRF Guard: DNS fail-open
`UrlGuard.validateUrl` resolves the hostname via DNS and checks resolved IPs against
known private/loopback ranges. If DNS resolution fails (NXDOMAIN, timeout, resolver
outage), the guard passes the URL through. The subsequent HTTP fetch will fail with
`UnreachableUrl` in the common case.

**Accepted trade-off:** Rejecting on DNS failure would break legitimate feeds when the
resolver is temporarily unavailable. Operators who need strict fail-closed behaviour
should run the application behind a network policy that blocks RFC1918 destinations
regardless of application-layer checks.
```

**Acceptance criteria:**
- [ ] The `unresolvable hostname passes guard (fail-open)` test is added and passes.
- [ ] The test does not throw any exception (the guard must handle all DNS failure modes).
- [ ] A Security section documenting this limitation is added to `REQUIREMENTS.md`.

---

## Recommended Fixes

These are not blocking but should be addressed in the same development pass where
practical.

---

### R-1 — XSS via `Host` header in copy-button `onclick`

**File:** `src/ReSS/Views.fs`, lines ~84–89

**Problem:**
The copy button's `onclick` attribute is built by string interpolation:

```fsharp
attr "onclick" (sprintf "navigator.clipboard.writeText('%s')" url)
```

`url` contains `ctx.Request.Host.Value` (the raw HTTP `Host` header). Giraffe.ViewEngine
HTML-encodes attribute values, but a Host header containing a single quote — e.g.
`evil.com';alert(1)//` — breaks the JavaScript string literal inside the attribute. If
the app is exposed without a reverse proxy that normalises Host headers, an attacker can
craft a URL that renders malicious JS for anyone who receives the share link.

**Fix:**
Remove the inline JS and use a `data-` attribute instead. Giraffe.ViewEngine
HTML-encodes attribute values, making `data-` injection safe:

```fsharp
// In Views.fs, replace the button node:
button [
    _type "button"
    _class "copy-btn"
    attr "data-copy-url" url
] [ str "Copy" ]
```

Add a small script block at the bottom of `layout`'s `<body>`:

```fsharp
script [] [ rawText """
  document.querySelectorAll('.copy-btn[data-copy-url]').forEach(function(b) {
    b.addEventListener('click', function() {
      navigator.clipboard.writeText(b.getAttribute('data-copy-url'));
    });
  });
""" ]
```

This eliminates the Host-header injection surface entirely.

**Acceptance criteria:**
- [ ] The `onclick` attribute no longer contains a JS string literal wrapping `url`.
- [ ] Clicking the copy button still copies the generated URL to the clipboard (verify
      manually or in the E2E `copy button is present` test).
- [ ] A Host header value containing `'` does not produce a JS syntax error in the page.

---

### R-2 — `DripCalculator` silent integer overflow on extreme inputs

**File:** `src/ReSS/Domain/DripCalculator.fs`, line 7

**Problem:**
```fsharp
let unlocked = min (daysElapsed * int perDay * 1<articles>) total
```
F# arithmetic defaults to unchecked (wrapping) overflow. For a start date far in the
past with a high `perDay`, `daysElapsed * int perDay` can overflow `Int32.MaxValue`,
wrapping to a large negative number. `min (negative) total` returns the negative, and
`ShowItems (negative<articles>)` is returned — serving no items forever despite being
caught up.

**Fix:**
Use an intermediate `int64` calculation before clamping back to `int`:

```fsharp
let calculate (clock: Clock) (startDate: DateOnly) (perDay: int<articles/day>) (total: int<articles>) : DripResult =
    let today        = clock ()
    let daysElapsed  = if today < startDate then 0 else today.DayNumber - startDate.DayNumber + 1
    let unlockedRaw  = min (int64 daysElapsed * int64 (int perDay)) (int64 (int total))
    let unlocked     = int unlockedRaw * 1<articles>
    if unlocked >= total then RedirectToSource
    else ShowItems unlocked
```

Add a regression test to `DripCalculatorTests.fs`:

```fsharp
[<Fact>]
let ``very old start date with high perDay returns RedirectToSource without overflow`` () =
    let veryOldStart = DateOnly(1900, 1, 1)
    let result = calculate (clock today) veryOldStart 1000<articles/day> 100<articles>
    Assert.Equal(RedirectToSource, result)
```

**Acceptance criteria:**
- [ ] The test above is added and passes.
- [ ] All existing `DripCalculatorTests` still pass.

---

### R-3 — No upper-bound validation on `perDay` in `UrlCodec.decode`

**File:** `src/ReSS/Domain/UrlCodec.fs`, line 29

**Problem:**
`decode` only checks `perDay > 0`. A crafted blob with `perDay = Int32.MaxValue`
(2,147,483,647) is accepted and passed to `DripCalculator`, compounding the overflow
risk described in R-2.

The form enforces `max="1000"` on the browser side, but blobs can be crafted manually.

**Fix:**
Extend the positivity guard to include an upper bound:

```fsharp
// Change:
let! _ = Result.require InvalidPerDay (perDay > 0)

// To:
let! _ = Result.require InvalidPerDay (perDay > 0 && perDay <= 1000)
```

Add a unit test to `UrlCodecTests.fs`:

```fsharp
[<Fact>]
let ``decode with perDay = 1001 returns InvalidPerDay`` () =
    let payload = "v1::https%3A%2F%2Fexample.com::1001::2025-01-01"
    let bad =
        Convert.ToBase64String(Text.Encoding.UTF8.GetBytes(payload))
        |> fun s -> s.TrimEnd('=').Replace('+','-').Replace('/','_')
    Assert.Equal(Error InvalidPerDay, decode bad)
```

**Acceptance criteria:**
- [ ] `decode` of a blob with `perDay = 1001` returns `Error InvalidPerDay`.
- [ ] `decode` of a blob with `perDay = 1000` returns `Ok`.
- [ ] All existing `UrlCodecTests` still pass.

---

### R-4 — Dead `futureClock` binding in `HandlerTests.fs`

**File:** `tests/ReSS.Tests/HandlerTests.fs`, lines 64–65

**Problem:**
```fsharp
let private todayClock  = fixedClock (DateOnly(2025, 1, 15))
let private futureClock = fixedClock (DateOnly(2025, 1, 15))  // ← same date, unused
```
`futureClock` is never referenced. The comment is misleading (same date as `todayClock`).
This will likely produce a compiler warning and confuses future maintainers.

**Fix:**
Delete the `futureClock` binding entirely. If a future test needs a "future" clock, define
it locally in that test with a clearly future date.

**Acceptance criteria:**
- [ ] `futureClock` binding is removed.
- [ ] All `HandlerTests` still pass.
- [ ] No unused-binding compiler warning on that file.

---

### R-5 — E2E `items are returned oldest-first` asserts on a fixture title substring

**File:** `tests/ReSS.E2E/FeedEndpointTests.fs`, lines ~98–101

**Problem:**
```fsharp
let firstTitle = its.[0].Element(XName.Get("title")).Value
Assert.Contains("oldest", firstTitle)
```
This works only because the fixture happens to include `"(oldest)"` in the first item's
title. If the fixture is ever cleaned up, or if items come back in wrong order with
`"oldest"` in a non-first item by coincidence, the test would give a false positive or
silently break. The test should assert on the `pubDate` ordering directly.

**Fix:**
Replace the title-substring check with an explicit date-ordering check:

```fsharp
[<Fact>]
member _.``items are returned oldest-first`` () =
    let startDate = DateOnly(2025, 5, 31)
    let blob      = makeBlob FeedUrl 1 startDate
    let handler   = StubHandler [FeedUrl, makeRssResponse()]
    withApi handler (fixedClock today) (fun _ apiCtx -> task {
        let! resp = apiCtx.GetAsync(sprintf "/feed/%s" blob)
        let! body = resp.TextAsync()
        let its   = items body
        Assert.Equal(2, its.Length)
        let pubDates =
            its
            |> List.map (fun i ->
                DateTimeOffset.Parse(i.Element(XName.Get("pubDate")).Value))
        Assert.True(
            pubDates.[0] <= pubDates.[1],
            sprintf "Expected oldest-first but got %A then %A" pubDates.[0] pubDates.[1])
    })
```

**Acceptance criteria:**
- [ ] The test no longer references any title substring.
- [ ] The test passes with the current fixture and would fail if items were returned
      newest-first.

---

## Re-review Checklist

When resubmitting, confirm each item is resolved:

**Blocking:**
- [ ] B-1: Dockerfile updated to `dotnet/sdk:10.0` and `dotnet/aspnet:10.0`; Docker build passes
- [ ] B-2: `0.0.0.0` blocked in `isPrivateOrLoopback`; two new unit tests added and passing
- [ ] B-3: Fail-open test added and passing; DNS limitation documented in `REQUIREMENTS.md`

**Recommended:**
- [ ] R-1: Copy button uses `data-` attribute; no `Host` header injection surface
- [ ] R-2: `DripCalculator` uses `int64` intermediate; overflow regression test added
- [ ] R-3: `perDay` upper-bound check added; unit test for `perDay = 1001` added
- [ ] R-4: Dead `futureClock` binding removed
- [ ] R-5: E2E ordering test asserts on `pubDate` comparison, not title substring

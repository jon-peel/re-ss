# QA Review — RSS Catch-Up Feed Generator
**Branch:** `implement/rss-catchup-feed-generator`
**Reviewer:** Quinn (QA)
**Status:** ❌ BLOCKED — resolve all High items before merge

---

## Blocking Issues (must fix before merge)

---

### B-1 — FR-07 Missing: No Copy-to-Clipboard Button

**File:** `src/ReSS/Views.fs`

**Problem:**
FR-07 requires a copy-to-clipboard button alongside the generated URL. The result section
currently renders only a `<code>` block with no button.

**What to implement:**
Add a `<button>` adjacent to the `<code>` element in the `Success` branch of `resultSection`.
The button should call `navigator.clipboard.writeText(...)` with the generated URL as its argument.

A minimal inline approach (no external JS file needed):

```html
<button onclick="navigator.clipboard.writeText('GENERATED_URL')">Copy</button>
```

In Giraffe ViewEngine this looks like:

```fsharp
button [
    _type "button"
    attr "onclick" (sprintf "navigator.clipboard.writeText('%s')" url)
] [ str "Copy" ]
```

Add a style entry for this button so it doesn't inherit the `button[type=submit]` styles —
either scope the existing rule to `button[type=submit]` only (it already is) or add a
`.copy-btn` class with its own style.

**Acceptance criteria:**
- [ ] A button is visible in the result section after successful form submission.
- [ ] Clicking it copies the generated URL to the clipboard.
- [ ] The button is styled distinctly from the submit button.
- [ ] The E2E test `valid RSS URL produces generated feed URL` (FormTests) is extended or a new
      test added to assert the copy button is present.

---

### B-2 — `UrlCodec.decode` Accepts `perDay ≤ 0`

**File:** `src/ReSS/Domain/UrlCodec.fs`

**Problem:**
`Int32.TryParse` succeeds for `0` and negative integers. A crafted blob with `perDay = 0`
decodes as `Ok` and is passed to `DripCalculator`, which returns `ShowItems 0` forever —
silently serving a permanently empty feed. `perDay = -5` is equally nonsensical.

**What to implement:**
After the `Int32.TryParse` step in `decode`, add a positivity guard. The idiomatic place is
immediately after parsing, still inside the `monad` block:

```fsharp
let! perDay    = Int32.TryParse(parts.[2]) |> Result.ofTryParse InvalidPerDay
let! _         = Result.require InvalidPerDay (perDay > 0)
```

> **On unsigned types:** `uint32` still admits `0`, so it doesn't eliminate the need for an
> explicit `> 0` check. Keeping `int` with the explicit guard is simpler and more idiomatic.
> Don't change the type.

**Acceptance criteria:**
- [ ] `decode` of a blob containing `perDay = 0` returns `Error InvalidPerDay`.
- [ ] `decode` of a blob containing `perDay = -1` returns `Error InvalidPerDay`.
- [ ] The existing round-trip property test still passes (it generates `perDay` in `1..100`).
- [ ] Add unit tests to `UrlCodecTests.fs`:
  - `decode with perDay = 0 returns InvalidPerDay`
  - `decode with perDay = -1 returns InvalidPerDay`

---

### B-3 — `UrlGuard` IPv6 Private Ranges Incomplete

**File:** `src/ReSS/Domain/UrlGuard.fs`

**Problem:**
Two IPv6 private address classes are not blocked:

1. **`fc00::/7` — Unique Local Addresses (ULA).** These are IPv6's equivalent of RFC1918.
   Any address where the first 7 bits are `1111110x` (i.e. first byte `0xFC` or `0xFD`) is a
   private LAN address. Example: `http://[fd12:3456:789a::1]/feed` currently passes the guard.

2. **`::ffff:0:0/96` — IPv4-mapped IPv6 addresses.** A URL like
   `http://[::ffff:192.168.1.1]/feed` encodes a private IPv4 address in IPv6 notation. The
   current guard only checks `AddressFamily.InterNetwork` for private ranges, so the
   IPv6-mapped form bypasses it entirely.

**What to implement:**
Extend the `InterNetworkV6` branch of `isPrivateOrLoopback`:

```fsharp
| AddressFamily.InterNetworkV6 ->
    ip.Equals(IPAddress.IPv6Loopback)
    // fe80::/10 — link-local
    || (ip.GetAddressBytes().[0] = 0xfeuy && (ip.GetAddressBytes().[1] &&& 0xC0uy) = 0x80uy)
    // fc00::/7 — Unique Local (ULA): first byte is 0xFC or 0xFD
    || (ip.GetAddressBytes().[0] &&& 0xFEuy = 0xFCuy)
    // ::ffff:0:0/96 — IPv4-mapped: extract the embedded IPv4 and re-check
    || (ip.IsIPv4MappedToIPv6 && isPrivateOrLoopback (ip.MapToIPv4()))
```

> Note: the last line is a recursive call. Because `MapToIPv4()` returns an
> `AddressFamily.InterNetwork` address, it will hit the IPv4 branch — no infinite recursion.
> `IPAddress.IsIPv4MappedToIPv6` is available on .NET 6+.

**Acceptance criteria:**
- [ ] `isPrivateOrLoopback (IPAddress.Parse("fd00::1"))` returns `true`.
- [ ] `isPrivateOrLoopback (IPAddress.Parse("fc00::1"))` returns `true`.
- [ ] `isPrivateOrLoopback (IPAddress.Parse("::ffff:192.168.1.1"))` returns `true`.
- [ ] `isPrivateOrLoopback (IPAddress.Parse("::ffff:8.8.8.8"))` returns `false`.
- [ ] `isPrivateOrLoopback (IPAddress.Parse("2001:db8::1"))` returns `false` (public).
- [ ] Add unit tests to `UrlGuardTests.fs` covering all of the above cases.
- [ ] Add an FsCheck property for the full ULA range (`fc00::` – `fdff::...`), following the
      same pattern as the existing `127.x.x.x` range property.

---

## Recommended Fixes (non-blocking, same pass preferred)

---

### R-1 — FR-08 Summary Message Wording Mismatch

**File:** `src/ReSS/Views.fs`, line 58

**Problem:**
FR-08 specifies: `"Your new feed has n of t articles ready"`.
Current output: `"n of t articles ready"`.

**Fix:** Change the format string:
```fsharp
// before
str (sprintf "%d of %d articles ready" n t)

// after
str (sprintf "Your new feed has %d of %d articles ready" n t)
```

Update any E2E test that asserts on `"articles ready"` — the assertion `Assert.Contains("articles ready", ...)` will still pass, but update `Assert.StartsWith("0 of", ...)` in `FormTests` to `Assert.StartsWith("Your new feed has 0 of", ...)`.

---

### R-2 — DNS Resolution Blocks a ThreadPool Thread

**File:** `src/ReSS/Domain/UrlGuard.fs`, line 54

**Problem:**
`Dns.GetHostAddresses(host)` is synchronous and blocks a ThreadPool thread for the full
DNS resolution time (potentially 30+ seconds on a slow resolver) inside an `async` block.

**Fix:** Replace with the async overload:
```fsharp
// before
try Dns.GetHostAddresses(host)
with _ -> [||]

// after
try
    let! resolved = Dns.GetHostAddressesAsync(host) |> Async.AwaitTask
    resolved
with _ -> [||]
```

The surrounding `let ips = ...` will need to become a `let! ips = async { ... }` binding.
Restructure the block so `ips` is bound with `let!`:

```fsharp
let! ips =
    match IPAddress.TryParse(host) with
    | true, ip -> async { return [| ip |] }
    | false, _ ->
        async {
            try
                return! Dns.GetHostAddressesAsync(host) |> Async.AwaitTask
            with _ ->
                return [||]
        }
```

---

### R-3 — Unresolvable Hostname Silently Passes Guard

**File:** `src/ReSS/Domain/UrlGuard.fs`, lines 54–55

**Problem:**
If DNS resolution fails (NXDOMAIN, timeout, network error), `ips` is `[||]`, and
`Array.tryFind` returns `None`, so the guard returns `Ok uri`. The URL silently passes
the security check.

**Fix (lightweight):** Add a comment making the decision explicit. If you want strictness,
treat an empty `ips` result as a separate `UrlGuardError` (e.g. `UnresolvableHost`). For
the current audience this is an acceptable risk — the fetch will fail with `UnreachableUrl`
in any case — but the intent should be documented:

```fsharp
// If DNS resolution fails, ips will be empty and the URL will pass the guard.
// The subsequent fetch will fail with UnreachableUrl in that case.
// This is an accepted trade-off for the current use case.
```

---

### R-4 — Dead `AggregateException` Handler in `FeedFetcher`

**File:** `src/ReSS/Domain/FeedFetcher.fs`, lines 69–73

**Problem:**
`Async.AwaitTask` automatically unwraps `AggregateException` and re-raises the inner
exception before the F# `with` handler sees it. The `AggregateException` arm is never
reached — it is dead code.

**Fix:** Remove the arm entirely:
```fsharp
// Remove these lines:
| :? AggregateException as ae
    when ae.InnerExceptions |> Seq.exists (fun e -> e :? HttpRequestException) ->
    logger |> Option.iter (fun l ->
        l.LogWarning("Unreachable URL: {sourceUrl}", url))
    return Error UnreachableUrl
```

---

### R-5 — Items Sorted Twice

**Files:** `src/ReSS/Handlers.fs` line 145, `src/ReSS/Domain/FeedBuilder.fs` lines 9–13

**Problem:**
`getFeedHandler` sorts items by `PublishDate` before slicing. `buildFeed` then sorts them
again. The second sort is redundant. The handler's sort also doesn't handle
`DateTimeOffset.MinValue` (items with no pubDate), while `buildFeed`'s sort does —
the edge case handling is split across two places.

**Fix:** Remove the sort from the handler and let `buildFeed` own it exclusively:
```fsharp
// In getFeedHandler, change:
let slice = allItems |> List.sortBy (fun i -> i.PublishDate) |> List.truncate (int n)

// To:
let slice = allItems |> List.truncate (int n)
```

`buildFeed` already sorts correctly with `MinValue` handling and will produce the right
oldest-first order.

> **Important:** This changes the slicing behaviour. Previously the handler sorted then sliced,
> meaning the `n` oldest items were returned. After this change, the handler truncates the raw
> (upstream-ordered) list, then `buildFeed` sorts. If the upstream feed is newest-first
> (common in RSS), `List.truncate n` on the unsorted list takes the `n` newest, and
> `buildFeed` sorts those oldest-first — which is **wrong**.
>
> The correct fix is therefore the opposite: keep the sort-then-truncate in the handler, and
> **remove the redundant sort from `buildFeed`**, replacing it with a trust that its input
> is already sorted. Add a comment to `buildFeed` noting the expected precondition.

---

### R-6 — No HTTP Timeout on `HttpClient`

**File:** `src/ReSS/Program.fs`, line 27

**Problem:**
`HttpClient.Timeout` defaults to 100 seconds. For arbitrary user-submitted URLs, a slow or
misbehaving upstream server could hold a connection for close to two minutes.

**Fix:** Configure a timeout when registering the client:
```fsharp
// Replace:
builder.Services.AddHttpClient() |> ignore

// With:
builder.Services.AddHttpClient(fun (client: Net.Http.HttpClient) ->
    client.Timeout <- TimeSpan.FromSeconds(15.0)
) |> ignore
```

---

## Test Coverage Gaps

The following scenarios have no test at any layer and should be added:

| Gap | Suggested location |
|-----|--------------------|
| `perDay = 0` blob decodes to `InvalidPerDay` | `UrlCodecTests.fs` |
| `perDay = -1` blob decodes to `InvalidPerDay` | `UrlCodecTests.fs` |
| IPv6 ULA address (`fd00::1`) is blocked | `UrlGuardTests.fs` |
| IPv4-mapped IPv6 private (`::ffff:192.168.1.1`) is blocked | `UrlGuardTests.fs` |
| IPv4-mapped IPv6 public (`::ffff:8.8.8.8`) is not blocked | `UrlGuardTests.fs` |
| Copy button is present in result section | `FormTests.fs` (E2E) |

---

## Re-review Checklist

When resubmitting, confirm:

- [ ] B-1: Copy button present, works, styled, E2E test updated/added
- [ ] B-2: `perDay ≤ 0` returns `InvalidPerDay`; two new unit tests added
- [ ] B-3: ULA and IPv4-mapped IPv6 blocked; unit tests and property test added
- [ ] R-1: Summary wording matches FR-08
- [ ] R-2: DNS uses async overload
- [ ] R-4: Dead `AggregateException` arm removed
- [ ] R-5: Dual sort resolved (one owner)
- [ ] R-6: HTTP timeout configured

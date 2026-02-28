# Requirements: RSS Catch-Up Feed Generator

## 1. Project Overview

A web-based tool that takes an existing RSS feed and produces a new feed URL that releases articles from the original feed at a controlled rate — n articles per day, starting from a given date, oldest first. This allows a user to "catch up" on a backlog of RSS content gradually rather than being overwhelmed by all historical articles at once.

The system is stateless: all configuration is encoded into the generated feed URL. No database or persistent storage is required.

## 2. Goals & Success Criteria

- A user can paste an RSS URL and specify a drip rate, then receive a new feed URL they can add to any RSS reader.
- The new feed releases articles from the original feed at the configured rate, oldest first.
- Once the user has fully caught up, the feed transparently redirects to the original.
- The system is reliable enough to share with a small number of trusted users.

## 3. Users & Stakeholders

**Primary user:** The developer/owner of the system, and a small number of trusted individuals they may share it with. Users are technically comfortable, familiar with RSS readers, and capable of adding a custom feed URL to their reader of choice.

There are no admin roles, no public registration, and no user accounts.

## 4. Functional Requirements

### 4.1 Feed Generation Form (Web Page)

- FR-01: The page shall present a form with two required fields: an RSS source URL and a number representing articles to release per day.
- FR-02: The form shall include an "Advanced" or "Extra" section, collapsed by default, containing an optional start date field.
- FR-03: The start date field shall default to today's date when not explicitly set by the user.
- FR-04: On form submission, the system shall attempt to fetch and parse the RSS feed at the provided URL before generating any output.
- FR-05: If the URL is unreachable, not XML, or cannot be parsed as a valid RSS feed, the form shall display a specific, descriptive inline error message (e.g. "Invalid URL", "URL did not return XML", "Unable to parse feed").
- FR-06: On successful validation, the page shall display the generated `/feed` URL.
- FR-07: A copy-to-clipboard button shall be displayed alongside the generated URL.
- FR-08: Following successful validation, the page shall display a summary message in the form: "Your new feed has n of t articles ready", where n is the number of articles currently unlocked and t is the total number of articles in the original feed.

### 4.2 Generated Feed URL

- FR-09: The generated `/feed` URL shall encode all required parameters (source URL, articles per day, start date) as an opaque blob within the URL itself.
- FR-10: No configuration shall be stored server-side; the URL alone must contain everything needed to reconstruct the feed.

### 4.3 /feed Endpoint (RSS Output)

- FR-11: The `/feed` route shall decode the parameters from the URL, fetch the original RSS feed, and return a valid XML RSS feed.
- FR-12: The number of articles returned shall be calculated as: `days since start date (inclusive) × articles per day`, capped at the total number of articles in the original feed. On the start date itself, the first batch of articles is immediately available (day 1 of the schedule).
- FR-13: Articles shall be returned in oldest-first order (i.e. working through the backlog chronologically).
- FR-14: If the calculated number of unlocked articles equals or exceeds the total number of articles in the original feed, the system shall return a permanent (3xx) redirect to the original RSS URL.
- FR-15: If the start date is in the future, the feed shall return zero articles until that date arrives.
- FR-16: The returned feed shall preserve the original feed's metadata as closely as possible (title, description, link, language, etc.), with the exception of the feed title, which shall be appended with a progress indicator in the format `n/t` (e.g. "My Podcast — 12/84").
- FR-17: If the encoded parameters are malformed, or the original RSS source is unreachable at request time, the system shall return the error it receives (HTTP error code or descriptive error response).

### 4.4 Caching

- FR-18: The system shall cache fetched RSS feeds in memory to avoid redundant upstream requests when multiple users subscribe to the same source feed.
- FR-19: Cached responses shall expire after a TTL of 15 minutes.

## 5. Data Requirements

- No user data is stored persistently.
- All feed configuration lives in the encoded `/feed` URL.
- The in-memory cache holds fetched RSS feed content, keyed by source URL, with a TTL-based expiry. Cache is not persisted across server restarts.

## 6. Integration Requirements

- IR-01: The system must fetch and parse standard RSS feeds from arbitrary third-party URLs provided by users.
- IR-02: The system must return standards-compliant RSS XML that can be consumed by any RSS reader.

## 7. Non-Functional Requirements

- **Performance:** The system is expected to serve a small number of users (fewer than a handful of concurrent users). No specific response time SLA required.
- **Availability:** No formal uptime requirement. Best-effort availability is acceptable.
- **Security:** No authentication or authorisation required. The encoded feed URL should not be trivially reversible (opaque encoding preferred over plain query strings), though this is a convenience preference, not a hard security requirement.
- **Scalability:** Not a current concern. In-memory caching is sufficient for the expected load.
- **Compliance:** No regulatory or data residency requirements.
- **Localisation:** No multi-language requirements.

## 8. Constraints

- No external database or persistent storage should be required.
- The system must be stateless by design (all state lives in the URL).

## 9. Assumptions

- The original RSS feeds being proxied are publicly accessible without authentication.
- RSS readers will poll the `/feed` URL on a regular schedule; a short in-memory cache TTL is sufficient to prevent hammering upstream sources.
- "Articles" maps to individual `<item>` elements in the RSS feed.
- The total article count `t` is determined live at the time of each request (form submission or `/feed` poll) by fetching the current upstream feed. If the upstream feed grows new articles over time, those new articles are treated as part of the backlog and are only released after all previously available articles have been drip-fed. The catch-up redirect (FR-14) will only trigger once the drip count meets or exceeds the live total.
- A small number of users means no abuse prevention, rate limiting, or authentication is needed at this stage.

## 10. Out of Scope

- User accounts, authentication, or authorisation.
- Persistent storage of feeds, configurations, or user preferences.
- Support for Atom feeds or other non-RSS feed formats.
- A feed preview or article browser in the UI.
- Notifications or alerts when catch-up is complete.
- Any admin interface or usage analytics.
- Rate limiting or abuse prevention.

## 11. Open Questions

There are no outstanding open questions.

## 12. Security — Known Limitations

### SSRF Guard: DNS fail-open
`UrlGuard.validateUrl` resolves the hostname via DNS and checks resolved IPs against
known private/loopback ranges. If DNS resolution fails (NXDOMAIN, timeout, resolver
outage), the guard passes the URL through. The subsequent HTTP fetch will fail with
`UnreachableUrl` in the common case.

**Accepted trade-off:** Rejecting on DNS failure would break legitimate feeds when the
resolver is temporarily unavailable. Operators who need strict fail-closed behaviour
should run the application behind a network policy that blocks RFC1918 destinations
regardless of application-layer checks.

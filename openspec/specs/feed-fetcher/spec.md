## ADDED Requirements

### Requirement: Fetch and parse an upstream RSS feed, returning a typed Result
The system SHALL perform an HTTP GET to the supplied URL, validate that the response content-type is XML, parse it with `SyndicationFeed.Load`, and return `Ok SyndicationFeed` on success.

#### Scenario: Valid RSS URL returns Ok
- **WHEN** `fetchFeed` is called with a URL that returns a valid RSS 2.0 XML response
- **THEN** it returns `Ok SyndicationFeed`

#### Scenario: Non-XML response returns NotXml error
- **WHEN** the upstream response has a non-XML content-type
- **THEN** `fetchFeed` returns `Error NotXml`

#### Scenario: HTTP error status returns HttpError
- **WHEN** the upstream server returns a non-2xx status code (e.g., 404)
- **THEN** `fetchFeed` returns `Error (HttpError <statusCode>)`

#### Scenario: Network exception returns UnreachableUrl
- **WHEN** the HTTP request throws a network-level exception (e.g., no route to host)
- **THEN** `fetchFeed` returns `Error UnreachableUrl`

#### Scenario: Invalid XML returns ParseFailure
- **WHEN** the upstream response is XML but cannot be parsed as a syndication feed
- **THEN** `fetchFeed` returns `Error (ParseFailure <message>)`

---

### Requirement: Cache feed responses for 15 minutes to avoid redundant upstream requests
The system SHALL check `IMemoryCache` (keyed by source URL) before making an HTTP request. On a cache miss the fetched feed SHALL be stored with an absolute TTL of 15 minutes.

#### Scenario: Cache hit skips HTTP request
- **WHEN** `fetchFeed` is called twice for the same URL within the TTL window
- **THEN** the HTTP handler is invoked only once

#### Scenario: Cache miss after TTL re-fetches
- **WHEN** `fetchFeed` is called after the TTL has expired
- **THEN** the HTTP handler is invoked again

---

### Requirement: HttpClient and IMemoryCache are injected dependencies
The system SHALL accept `HttpClient` and `IMemoryCache` as explicit parameters to `fetchFeed`, enabling test doubles to be supplied without a mocking framework.

#### Scenario: Stubbed HttpClient controls test responses
- **WHEN** a test supplies a custom `HttpMessageHandler` that returns a fixed response
- **THEN** `fetchFeed` uses that handler exclusively

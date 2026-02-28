## ADDED Requirements

### Requirement: Encode feed parameters into a URL-safe blob
The system SHALL encode a source URL, articles-per-day value, and start date into a single opaque Base64url string with no `=` padding, using the format `v1::urlencode(sourceUrl)::perDay::YYYY-MM-DD`.

#### Scenario: Encode produces a non-empty, padding-free string
- **WHEN** `encode` is called with a valid URL, a positive perDay integer, and a valid start date
- **THEN** the returned string is non-empty and contains no `=` characters

#### Scenario: Encoded string is valid Base64url
- **WHEN** `encode` is called with any valid inputs
- **THEN** the returned string consists only of Base64url-safe characters (`A-Z`, `a-z`, `0-9`, `-`, `_`)

---

### Requirement: Decode a blob back into feed parameters
The system SHALL decode a blob produced by `encode` back into a `FeedParams` record with identical field values, returning `Result.Ok FeedParams` on success.

#### Scenario: Round-trip encode → decode preserves all fields
- **WHEN** `encode` is called with a URL, perDay, and startDate, and the result is passed to `decode`
- **THEN** `decode` returns `Ok` with `SourceUrl`, `PerDay`, and `StartDate` equal to the original inputs

#### Scenario: Round-trip holds for arbitrary valid inputs
- **WHEN** `encode → decode` is executed with arbitrary valid URLs, perDay values (1–1000), and dates (2000-01-01 to 2100-12-31)
- **THEN** the round-trip always returns `Ok` with matching fields (FsCheck property)

---

### Requirement: Reject blobs with an unknown version prefix
The system SHALL return `DecodeError.UnsupportedVersion` when the version segment of the decoded blob is not `v1`.

#### Scenario: Unknown version string
- **WHEN** `decode` is called with a blob whose decoded version segment is not `v1`
- **THEN** `decode` returns `Error (UnsupportedVersion <version>)`

---

### Requirement: Reject malformed Base64 input
The system SHALL return `DecodeError.InvalidBase64` when the blob is not valid Base64url.

#### Scenario: Invalid Base64 characters
- **WHEN** `decode` is called with a string containing characters outside the Base64url alphabet
- **THEN** `decode` returns `Error InvalidBase64`

---

### Requirement: Reject blobs with the wrong number of segments
The system SHALL return `DecodeError.MalformedSegments` when the decoded blob does not contain exactly four `::` -separated segments.

#### Scenario: Too few segments
- **WHEN** `decode` is called with a blob that decodes to fewer than four `::` -separated parts
- **THEN** `decode` returns `Error MalformedSegments`

---

### Requirement: Reject blobs with a non-integer perDay value
The system SHALL return `DecodeError.InvalidPerDay` when the perDay segment is not a parseable positive integer.

#### Scenario: Non-numeric perDay
- **WHEN** `decode` is called with a blob whose perDay segment is not a valid integer
- **THEN** `decode` returns `Error InvalidPerDay`

---

### Requirement: Reject blobs with an unparseable start date
The system SHALL return `DecodeError.InvalidDate` when the date segment is not a valid `YYYY-MM-DD` date.

#### Scenario: Invalid date string
- **WHEN** `decode` is called with a blob whose date segment is not a valid ISO 8601 date
- **THEN** `decode` returns `Error InvalidDate`

---

### Requirement: Source URLs with special characters survive encoding
The system SHALL correctly percent-encode source URLs before embedding them in the blob, preserving all characters through a round-trip.

#### Scenario: URL with query string and special characters round-trips correctly
- **WHEN** `encode → decode` is executed with a URL containing query parameters, percent-encoded characters, or path segments with special characters
- **THEN** the decoded `SourceUrl` is identical to the original (FsCheck property)

## ADDED Requirements

### Requirement: GET / renders an empty HTML form
The system SHALL respond to `GET /` with HTTP 200 and an HTML page containing the feed configuration form.

#### Scenario: GET / returns 200 with HTML form
- **WHEN** a client sends `GET /`
- **THEN** the response is HTTP 200 with `Content-Type: text/html` containing a form element

---

### Requirement: POST / validates inputs and returns inline errors on failure
The system SHALL validate all form fields on `POST /` and return HTTP 200 with the form re-rendered including inline error messages when any field is invalid.

#### Scenario: Missing source URL returns form with error
- **WHEN** `POST /` is submitted with no source URL
- **THEN** response is HTTP 200 with an inline error message for the URL field

#### Scenario: Private IP URL returns form with guard error
- **WHEN** `POST /` is submitted with a URL resolving to a private IP
- **THEN** response is HTTP 200 with a guard error message inline

#### Scenario: Unreachable source URL returns form with fetch error
- **WHEN** `POST /` is submitted with a URL that cannot be fetched
- **THEN** response is HTTP 200 with a descriptive fetch error inline

---

### Requirement: POST / returns generated feed URL on success
The system SHALL encode the validated parameters into a blob, construct the `/feed/{blob}` URL, and render it in the response on successful form submission.

#### Scenario: Valid submission returns generated URL
- **WHEN** `POST /` is submitted with a valid RSS URL and positive perDay value
- **THEN** response is HTTP 200 containing the generated `/feed/{blob}` URL in the page body

---

### Requirement: GET /feed/{blob} returns RSS XML for normal operation
The system SHALL decode the blob, fetch the upstream feed, compute unlocked articles, and respond with HTTP 200 and `Content-Type: application/rss+xml` containing the sliced RSS feed.

#### Scenario: Valid blob returns RSS XML
- **WHEN** `GET /feed/{blob}` is called with a valid, non-expired blob
- **THEN** response is HTTP 200 with `Content-Type: application/rss+xml`

---

### Requirement: GET /feed/{blob} redirects when fully caught up
The system SHALL respond with HTTP 301 redirecting to the original source feed URL when `DripCalculator` returns `RedirectToSource`.

#### Scenario: Fully caught up returns 301
- **WHEN** `GET /feed/{blob}` is called and the calculated unlocked count ≥ total items
- **THEN** response is HTTP 301 with `Location` set to the original source URL

---

### Requirement: GET /feed/{blob} returns 400 for a malformed blob
The system SHALL respond with HTTP 400 when `UrlCodec.decode` returns an error.

#### Scenario: Malformed blob returns 400
- **WHEN** `GET /feed/{blob}` is called with a blob that fails decoding
- **THEN** response is HTTP 400

---

### Requirement: GET /feed/{blob} returns 400 for a private source URL
The system SHALL respond with HTTP 400 when `UrlGuard.validateUrl` rejects the decoded source URL.

#### Scenario: Private URL in blob returns 400
- **WHEN** `GET /feed/{blob}` is called and the decoded URL resolves to a private IP
- **THEN** response is HTTP 400

---

### Requirement: GET /feed/{blob} returns 502 for an unreachable or invalid upstream feed
The system SHALL respond with HTTP 502 when `FeedFetcher.fetchFeed` returns an error.

#### Scenario: Unreachable upstream returns 502
- **WHEN** `GET /feed/{blob}` is called and the upstream feed cannot be fetched
- **THEN** response is HTTP 502

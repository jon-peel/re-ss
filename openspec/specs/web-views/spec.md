## ADDED Requirements

### Requirement: Render a feed configuration form for GET /
The system SHALL render an HTML form with fields for source URL, articles-per-day, and (in a collapsed advanced section) start date.

#### Scenario: Form is visible on initial page load
- **WHEN** the page is loaded
- **THEN** the source URL input, per-day input, and submit button are visible

#### Scenario: Advanced section is collapsed by default
- **WHEN** the page is loaded
- **THEN** the start date field is not visible until the user expands the advanced section

#### Scenario: Expanding advanced section reveals start date field
- **WHEN** the user expands the advanced section
- **THEN** the start date input becomes visible

---

### Requirement: Display inline validation and fetch errors on form submission failure
The system SHALL re-render the form with an inline error message adjacent to the relevant field or as a form-level error when a fetch or guard error occurs.

#### Scenario: Validation error displayed inline
- **WHEN** the form is submitted with an invalid or missing field value
- **THEN** an error message is shown adjacent to the offending field

#### Scenario: Fetch error displayed as form-level message
- **WHEN** the form is submitted with a URL that fails fetching
- **THEN** a descriptive error message is shown at the form level

---

### Requirement: Display the generated feed URL and n/t summary on success
The system SHALL render the generated `/feed/{blob}` URL and a summary message of the form "n of t articles ready" after a successful form submission.

#### Scenario: Generated URL appears in result state
- **WHEN** the form is submitted successfully
- **THEN** the generated `/feed/{blob}` URL is visible in the page

#### Scenario: Summary message shows correct article count
- **WHEN** the form is submitted successfully
- **THEN** the page contains text of the form "n of t articles ready"

#### Scenario: Future start date shows zero articles ready
- **WHEN** the form is submitted with a start date in the future
- **THEN** the summary message shows "0 of t articles ready"

---

### Requirement: All user-supplied content is HTML-encoded in rendered output
The system SHALL produce no raw user-supplied HTML — Giraffe.ViewEngine encodes all values by default.

#### Scenario: User input is HTML-encoded
- **WHEN** a user submits a value containing HTML special characters (e.g., `<`, `>`, `"`)
- **THEN** those characters are rendered as HTML entities, not as raw markup

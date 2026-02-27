## ADDED Requirements

### Requirement: Produce valid RSS 2.0 XML from a feed slice
The system SHALL construct a well-formed RSS 2.0 XML string from a `SyndicationFeed` and a pre-sliced list of `SyndicationItem` values, suitable for direct HTTP response.

#### Scenario: Output is valid XML
- **WHEN** `buildFeed` is called with a valid feed and item list
- **THEN** the returned string is parseable XML with no errors

#### Scenario: Output is parseable as RSS 2.0
- **WHEN** the output string is loaded by `SyndicationFeed.Load`
- **THEN** it succeeds without throwing

---

### Requirement: Inject an n/t progress indicator into the feed title
The system SHALL append ` — n/t` to the channel title, where `n` is `unlockedCount` and `t` is `totalCount`.

#### Scenario: Title contains progress indicator
- **WHEN** `buildFeed` is called with unlockedCount = 5 and totalCount = 20
- **THEN** the output feed title contains `5/20`

---

### Requirement: Preserve source feed metadata
The system SHALL copy the source feed's description, link, language, and other channel-level metadata into the output feed unchanged.

#### Scenario: Metadata is preserved
- **WHEN** `buildFeed` is called with a feed that has description, link, and language set
- **THEN** the output feed has the same description, link, and language values

---

### Requirement: Output item count equals the unlocked slice size
The system SHALL include exactly as many `<item>` elements as there are items in the supplied list.

#### Scenario: Item count matches slice
- **WHEN** `buildFeed` is called with a list of k items
- **THEN** the output feed contains exactly k items

#### Scenario: Zero items when unlocked = 0
- **WHEN** `buildFeed` is called with an empty item list
- **THEN** the output feed contains zero items

---

### Requirement: Items are ordered oldest-first
The system SHALL sort items by `PublishDate` ascending (oldest first), regardless of the order supplied by the caller. Items with no `PublishDate` are placed first.

#### Scenario: Items in output are oldest-first
- **WHEN** `buildFeed` is called with a list of items in any order
- **THEN** the items in the output are sorted by `PublishDate` ascending

#### Scenario: Oldest-first ordering holds for arbitrary item lists
- **WHEN** `buildFeed` is called with an arbitrary item list (FsCheck property)
- **THEN** the output items are always in non-decreasing `PublishDate` order

## ADDED Requirements

### Requirement: Reject non-HTTP/S URL schemes
The system SHALL return `UrlGuardError.NonHttpScheme` for any URL whose scheme is not `http` or `https`.

#### Scenario: FTP scheme rejected
- **WHEN** `validateUrl` is called with a URL beginning with `ftp://`
- **THEN** it returns `Error NonHttpScheme`

#### Scenario: File scheme rejected
- **WHEN** `validateUrl` is called with a URL beginning with `file://`
- **THEN** it returns `Error NonHttpScheme`

---

### Requirement: Reject malformed URLs
The system SHALL return `UrlGuardError.MalformedUrl` for any string that cannot be parsed as a valid URL.

#### Scenario: Unparseable string
- **WHEN** `validateUrl` is called with a string that is not a valid URL
- **THEN** it returns `Error MalformedUrl`

---

### Requirement: Resolve hostname via DNS before checking IP ranges
The system SHALL resolve the hostname of a validated URL to one or more IP addresses using DNS before performing IP range checks, ensuring hostname-based SSRF bypasses are not possible.

#### Scenario: DNS resolution is performed
- **WHEN** `validateUrl` is called with a URL containing a hostname (not a bare IP)
- **THEN** the system resolves that hostname to IP addresses before checking ranges

---

### Requirement: Reject loopback addresses
The system SHALL return `UrlGuardError.PrivateOrLoopbackAddress` when any resolved IP falls in the IPv4 loopback range `127.0.0.0/8` or the IPv6 loopback address `::1`.

#### Scenario: IPv4 loopback rejected
- **WHEN** `validateUrl` is called with a URL resolving to `127.0.0.1`
- **THEN** it returns `Error (PrivateOrLoopbackAddress "127.0.0.1")`

#### Scenario: Entire 127.x.x.x range rejected
- **WHEN** `validateUrl` is called with a URL resolving to any address in `127.0.0.0/8`
- **THEN** it returns `Error (PrivateOrLoopbackAddress _)` (FsCheck property)

#### Scenario: IPv6 loopback rejected
- **WHEN** `validateUrl` is called with a URL resolving to `::1`
- **THEN** it returns `Error (PrivateOrLoopbackAddress "::1")`

---

### Requirement: Reject link-local addresses
The system SHALL return `UrlGuardError.PrivateOrLoopbackAddress` when any resolved IP falls in `169.254.0.0/16` (IPv4 link-local / cloud metadata) or `fe80::/10` (IPv6 link-local).

#### Scenario: Cloud metadata endpoint rejected
- **WHEN** `validateUrl` is called with a URL resolving to `169.254.169.254`
- **THEN** it returns `Error (PrivateOrLoopbackAddress _)`

#### Scenario: Entire 169.254.x.x range rejected
- **WHEN** `validateUrl` is called with a URL resolving to any address in `169.254.0.0/16`
- **THEN** it returns `Error (PrivateOrLoopbackAddress _)` (FsCheck property)

---

### Requirement: Reject RFC-1918 private addresses
The system SHALL return `UrlGuardError.PrivateOrLoopbackAddress` when any resolved IP falls in `10.0.0.0/8`, `172.16.0.0/12`, or `192.168.0.0/16`.

#### Scenario: Class A private range rejected
- **WHEN** `validateUrl` is called with a URL resolving to any address in `10.0.0.0/8`
- **THEN** it returns `Error (PrivateOrLoopbackAddress _)` (FsCheck property)

#### Scenario: Class B private range rejected
- **WHEN** `validateUrl` is called with a URL resolving to any address in `172.16.0.0/12`
- **THEN** it returns `Error (PrivateOrLoopbackAddress _)` (FsCheck property)

#### Scenario: Class C private range rejected
- **WHEN** `validateUrl` is called with a URL resolving to any address in `192.168.0.0/16`
- **THEN** it returns `Error (PrivateOrLoopbackAddress _)` (FsCheck property)

---

### Requirement: Accept valid public URLs
The system SHALL return `Ok url` for a well-formed HTTP/S URL whose resolved IPs are all in public address space.

#### Scenario: Known public hostname accepted
- **WHEN** `validateUrl` is called with a URL resolving to a known public IP
- **THEN** it returns `Ok url`

---

### Requirement: Reject if any resolved IP is private (multi-address hostnames)
The system SHALL reject a URL if the hostname resolves to multiple addresses and **any one** of them falls in a blocked range.

#### Scenario: Mixed public/private resolution rejected
- **WHEN** a hostname resolves to both a public IP and a private IP
- **THEN** `validateUrl` returns `Error (PrivateOrLoopbackAddress _)`

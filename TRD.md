# Technical Requirements Document (TRD)

## Metadata

| Field | Value |
| --- | --- |
| Document ID | TRD-002 |
| Product | Smart Parking Navigator |
| Repository | `justinyoo-hackathon/spn-imda` |
| Related Documents | `PRD.md`, `IDEATION.md` |
| Related Issue | Closes `justinyoo-hackathon/spn-imda#2` |
| Status | Draft |
| Version | 0.1.0 |
| Last Updated | 2026-08-19 |
| Authors | GitHub Copilot Coding Agent |
| Reviewers | Workshop participant, repository maintainers |

## Revision History

| Version | Date | Author | Summary |
| --- | --- | --- | --- |
| 0.1.0 | 2026-08-19 | GitHub Copilot Coding Agent | Initial workshop-sized MVP TRD draft |

## 1. Purpose

This TRD translates the approved workshop MVP in `PRD.md` into an
implementation-ready technical design for the prepared .NET Aspire starter. It
defines the responsibilities, data flow, contracts, processing rules, error
handling, and test strategy for a Singapore-only Smart Parking Navigator that
uses HDB static metadata plus live data.gov.sg availability.

This document defines technical requirements only. It does not implement
application behavior, deployment, database persistence, MCP integration, or
agentic AI.

## 2. Architecture Constraints

- Keep the existing Aspire solution structure:
  - `CarparkAvailability.WebApp`
  - `CarparkAvailability.ApiApp`
  - `CarparkAvailability.AppHost`
  - `CarparkAvailability.ServiceDefaults`
- Keep Google Maps usage in the browser-facing WebApp only.
- Keep data.gov.sg access on the server side in ApiApp only.
- Treat `data/CarparkAvailability.json` as the authoritative API contract.
- Use `data/carpark-availability-sample.json` as the offline fixture for
  contract and integration tests.
- Use Singapore Standard Time (SGT) for interpreting source timestamps and
  freshness messaging.

## 3. System Responsibilities

### 3.1 `CarparkAvailability.WebApp`

- Render the mobile-first user experience for destination search, nearby car
  parks, filters, and detail viewing.
- Load Google Maps JavaScript and related browser-side place search/geocoding
  features using `GoogleMaps__ApiKey`.
- Send destination coordinates and filter selections to ApiApp through an
  Aspire-configured HTTP client.
- Render loading, empty, stale, unavailable, and error states returned from or
  derived from the backend response.
- Never call data.gov.sg directly.
- Never receive or log the `DataGovSg__ApiKey`.

### 3.2 `CarparkAvailability.ApiApp`

- Own the JSON HTTP API consumed by WebApp.
- Read and validate static HDB CSV data.
- Convert HDB SVY21 coordinates to WGS84 latitude/longitude.
- Call the data.gov.sg Car Park Availability API using `DataGovSg__ApiKey`.
- Parse, validate, and normalize live availability payloads using the published
  API contract.
- Join live and static datasets by `car_park_no` ↔ `carpark_number`.
- Filter to nearby HDB car parks within 500 metres of the destination.
- Calculate derived fields such as distance, occupancy, and freshness state.
- Preserve and serve last-known-good availability data when live refresh fails.
- Never expose `DataGovSg__ApiKey` to the browser or API responses.

### 3.3 `CarparkAvailability.AppHost`

- Remain the local development entry point.
- Wire WebApp to ApiApp with Aspire service discovery.
- Provide `GoogleMaps__ApiKey` only to WebApp.
- Provide `DataGovSg__ApiKey` only to ApiApp.
- Ensure WebApp waits for ApiApp readiness during local startup.

### 3.4 `CarparkAvailability.ServiceDefaults`

- Provide shared health endpoints, OpenTelemetry, service discovery, and HTTP
  resilience defaults.
- Apply consistent HTTP client behavior for service-to-service communication.
- Remain generic infrastructure support and not contain parking domain logic.

## 4. Data Sources

### 4.1 Static HDB Metadata

Source: `data/HDBCarparkInformation.csv`

Required fields:

- `car_park_no`
- `address`
- `x_coord`
- `y_coord`
- `car_park_type`
- `type_of_parking_system`
- `short_term_parking`
- `free_parking`
- `night_parking`
- `car_park_decks`
- `gantry_height`
- `car_park_basement`

Technical requirements:

- Parse the CSV into a strongly typed internal model.
- Reject rows with missing `car_park_no`.
- Preserve source strings for parking-condition fields whose business meaning is
  not formally documented.
- Safely parse numeric coordinate and gantry/deck fields.
- Treat malformed rows as data-quality issues that are logged and excluded from
  query results rather than crashing the API.

### 4.2 Live Availability

Source: data.gov.sg Car Park Availability API defined by
`data/CarparkAvailability.json`

Required live fields:

- Root timestamp for data acquisition
- Per-car-park `carpark_number`
- Per-car-park `update_datetime`
- Per-entry `carpark_info[].lot_type`
- Per-entry `carpark_info[].total_lots`
- Per-entry `carpark_info[].lots_available`

Technical requirements:

- Treat string-encoded numeric values as untrusted input and parse them safely.
- Support published lot types only:
  - `C` Cars
  - `H` Heavy vehicles
  - `S` Motorcycles with sidecar
  - `Y` Motorcycles
- Tolerate backward-compatible additive fields.
- Treat missing required fields, incompatible types, or structural mismatches as
  schema-validation failures.

## 5. Data Processing Flow

1. ApiApp loads HDB CSV metadata on startup and keeps it in memory for fast
   lookup.
2. ApiApp converts valid HDB SVY21 coordinates to WGS84 latitude/longitude.
3. WebApp submits a Singapore destination plus filter options to ApiApp.
4. ApiApp requests the latest live availability from data.gov.sg.
5. ApiApp validates and normalizes live availability data.
6. ApiApp joins HDB and live records using the normalized car-park identifier.
7. ApiApp computes distance from the destination to each matched HDB car park.
8. ApiApp filters to car parks within 500 metres.
9. ApiApp applies user-selected filters.
10. ApiApp derives occupancy, freshness, and ranking.
11. ApiApp returns a response tailored for WebApp rendering, including UI-state
    information needed for fresh, stale, unavailable, or error handling.

## 6. CSV Ingestion and Validation

Technical design:

- Load `HDBCarparkInformation.csv` once during application startup.
- Normalize `car_park_no` by trimming surrounding whitespace and comparing
  identifiers case-insensitively.
- Preserve original source values for display fields such as
  `short_term_parking` and `free_parking`.
- Convert `x_coord` and `y_coord` from CSV strings/numbers into numeric values
  before coordinate conversion.
- Exclude any row that cannot produce a valid unique HDB car-park identifier or
  valid map coordinates.

Observability requirements:

- Log total rows loaded, excluded-row count, and unmatched live-record count.
- Do not log secrets or raw end-user destination input beyond normal request
  telemetry.

## 7. SVY21 to WGS84 Conversion

Technical requirements:

- Perform coordinate conversion on the server side in ApiApp.
- Convert HDB `x_coord` and `y_coord` values from SVY21 to WGS84 latitude and
  longitude before any map rendering or distance calculation.
- Use a deterministic conversion implementation suitable for Singapore mapping.
- Treat conversion failure for a row as a data-quality exclusion for that row.

Validation requirements:

- Unit tests must verify conversion behavior against known sample points.
- Converted coordinates must be suitable for Google Maps display and backend
  geospatial distance calculations.

## 8. Live Availability Retrieval and Polling

Technical requirements:

- ApiApp calls the live endpoint from the server side only.
- The system targets the latest availability data and follows the source
  guidance to refresh at most once per minute.
- To avoid unnecessary upstream calls during active use, ApiApp may cache the
  most recent successful live response in memory for up to 60 seconds.
- The latest successful live response becomes the last-known-good snapshot.

Failure-handling requirements:

- If a refresh attempt fails but a last-known-good snapshot exists, serve joined
  results using that snapshot and mark the freshness state as stale or
  unavailable as appropriate.
- If no valid live snapshot exists, return an unavailable/error response without
  fabricated lot values.
- Do not silently mix partial malformed live records into an otherwise valid
  response without recording the exclusion.

## 9. Joining and Normalization

Identifier requirements:

- Join HDB rows to live rows using `car_park_no` from the CSV and
  `carpark_number` from the API.
- Normalize both identifiers by trimming and applying consistent casing before
  comparison.

Join requirements:

- Matched records produce the enriched car-park result used by WebApp.
- Unmatched live records must be ignored for UI result rendering because the MVP
  is HDB-only, but the unmatched count should be observable.
- HDB rows with no current live match may still be eligible for stale/unavailable
  display only if backed by a last-known-good snapshot.

## 10. Distance Calculation and Ranking

Distance requirements:

- Calculate straight-line distance in metres between the selected destination
  and each HDB car park using WGS84 coordinates.
- Exclude car parks farther than 500 metres from the destination.

Ranking requirements:

- Sort by ascending distance as the primary ranking factor.
- Use live suitability as tie-breakers:
  1. Higher available lots for the selected lot type
  2. Lower occupancy rate when totals are present
  3. Stable deterministic fallback such as car-park identifier

## 11. Filters and Derived Fields

### 11.1 Filters

Required supported filters:

- `availableOnly`
- `lotType`
- `nightParking`
- `carParkType`

Filtering rules:

- `availableOnly` uses the selected lot type when one is present; otherwise it
  uses the default lot type for the current view.
- `nightParking` and `carParkType` are evaluated from HDB static metadata.
- Filters must be applied after the static/live join and distance bounding.

### 11.2 Derived Fields

ApiApp must derive:

- `distanceMeters`
- `occupancyRate` when `totalLots > 0`
- `freshnessState`
- `sourceUpdateTimeSgt`
- `dataRetrievedAtSgt`

## 12. Freshness and Last-Known-Good States

Freshness rules aligned to `PRD.md`:

- `fresh`: selected result data has a source update time within 2 minutes of the
  current SGT-based evaluation time.
- `stale`: selected result data is older than 2 minutes but backed by a valid
  last-known-good snapshot.
- `unavailable`: no valid live lot data is available for the requested view.
- `error`: the backend cannot complete the request because of an unrecoverable
  failure such as invalid destination input or an upstream/schema failure with
  no usable snapshot.

WebApp rendering requirements:

- WebApp must clearly distinguish fresh from stale data.
- Stale and unavailable states must never be rendered as if they were fresh.
- The displayed source update time must correspond to the snapshot actually used
  in the response.

## 13. API Boundaries and Response Contract

### 13.1 Boundary Rules

- WebApp may call ApiApp only.
- ApiApp may call data.gov.sg only.
- Browser JavaScript may use Google Maps APIs only with `GoogleMaps__ApiKey`.
- Browser JavaScript must never call data.gov.sg directly.

### 13.2 WebApp to ApiApp API

ApiApp should expose a single search-focused endpoint for the MVP.

Proposed request shape:

- Destination coordinates in WGS84 latitude/longitude
- Optional destination label
- Optional filter parameters:
  - lot type
  - available-only
  - night parking
  - car-park type

Proposed response shape:

- Request context:
  - destination label
  - destination latitude/longitude
  - applied filters
- Result metadata:
  - freshness state
  - source update time
  - retrieval time
  - whether live or last-known-good data was used
  - any user-visible warning message
- Car-park results:
  - HDB identifier
  - address
  - latitude/longitude
  - distance in metres
  - car-park type
  - parking system
  - short-term parking
  - free parking
  - night parking
  - decks
  - gantry height
  - basement flag
  - per-lot-type total/available counts
  - occupancy rate
  - per-car-park update time

Contract requirements:

- ApiApp returns JSON only.
- Invalid client input must return a problem-details style error response.
- The response must never contain secrets or upstream authentication details.

## 14. Security Requirements

- Keep `DataGovSg__ApiKey` in AppHost user secrets and ApiApp environment
  configuration only.
- Keep `GoogleMaps__ApiKey` restricted to browser-side map features and website
  origins only.
- Do not commit API keys, connection strings, or `.env` files.
- Do not log secrets, raw authentication headers, or upstream key values.
- Validate and constrain incoming destination/filter inputs before using them.
- Do not expose internal stack traces in production responses.

## 15. Accessibility and UX Reliability

- WebApp must provide a usable list-first experience even when map interactions
  are unavailable or delayed.
- Loading, empty, stale, unavailable, and error states must be visible in text
  and not rely on color alone.
- Filters and result selection must be keyboard accessible.
- Important timestamps and parking conditions must be readable on mobile-sized
  screens.

## 16. Error Handling Requirements

### User Input Errors

- Invalid or non-Singapore destination results in a clear no-results or
  validation error state.

### Data Quality Errors

- Malformed CSV rows are excluded and logged.
- Malformed live records are excluded or the snapshot is rejected, depending on
  whether required contract fields are missing.

### Upstream Errors

- HTTP/network errors, non-success responses, or schema-validation failures from
  data.gov.sg must trigger last-known-good fallback when available.

### Internal Errors

- Unexpected server failures must return standardized problem details and avoid
  leaking implementation secrets.

## 17. Testing Strategy

Tests will be added during implementation and must be runnable with:

```bash
dotnet test CarparkAvailability.slnx
```

Required test coverage:

### Unit Tests

- CSV parsing and row validation
- Identifier normalization and join logic
- SVY21-to-WGS84 conversion
- Distance calculation
- Occupancy calculation
- Freshness-state classification
- Filter evaluation

### Contract Tests

- Validate `data/carpark-availability-sample.json` against the published
  contract in `data/CarparkAvailability.json`
- Accept additive unknown fields while failing missing required fields or
  incompatible structural changes

### Integration Tests

- ApiApp endpoint behavior for fresh, stale, unavailable, and error responses
- Last-known-good fallback behavior after a simulated upstream failure
- WebApp-to-ApiApp interaction using the prepared Aspire wiring

### Manual Verification

- Run the AppHost locally
- Confirm WebApp loads and ApiApp is healthy
- Confirm destination search, filtering, detail display, and freshness labeling
  once implementation exists

## 18. Explicitly Deferred Scope

The following remain out of scope for this MVP and this technical design:

- Deployment architecture and hosting environments
- Databases, historical persistence, and scheduled background storage
- Favorites, alerts, forecasting, reservations, and payments
- Traffic, weather, and external enrichment sources beyond Google Maps and
  data.gov.sg
- MCP integration
- Agentic AI features, which are deferred to workshop step 05

## 19. Implementation Readiness Checklist

- The design fits the existing Aspire projects without adding new services.
- Frontend and backend credential boundaries are explicit.
- HDB CSV ingestion, live polling, joining, coordinate conversion, distance, and
  freshness behavior are defined.
- Error handling and last-known-good behavior are specified.
- Testing requirements map to the accepted PRD.
